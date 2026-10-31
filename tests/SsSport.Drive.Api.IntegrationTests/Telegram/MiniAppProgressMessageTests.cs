using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// One progress message per player (T-6a): the owning analyst gets a single bot message when the player is created,
/// and that same message is edited in place as the player changes; never a new message per change. Telegram trouble
/// never fails the request.
/// </summary>
public sealed class MiniAppProgressMessageTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:progress-test-token";

    /// <summary>The analyst who creates the players.</summary>
    private const long OwnerTelegramId = 901;

    /// <summary>Another analyst.</summary>
    private const long OtherTelegramId = 902;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Records the bot's messages.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own name, Volleybox id and folder.</summary>
    private int _created;

    /// <summary>Starts PostgreSQL, boots the API and stores two analysts.</summary>
    /// <returns>A task that completes when everything is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:BotToken"] = BotToken,
                ["Telegram:Mode"] = "Webhook",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.UseInlineBotNotices();
                services.Replace(ServiceDescriptor.Singleton<IDriveFolderReader>(new FakeDriveFolderReader()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(new InMemoryObjectStorage()));
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        foreach (var telegramId in new[] { OwnerTelegramId, OtherTelegramId })
        {
            var analyst = User.RequestAccess(telegramId, $"analyst_{telegramId}", $"Analyst {telegramId}", DateTimeOffset.UtcNow);
            analyst.Approve(Role.Analyst);
            users.Add(analyst);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        _bot.Calls.Clear();
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Creating a player sends exactly one message to its analyst, naming the player first.</summary>
    [Fact]
    public async Task Creating_a_player_sends_one_progress_message()
    {
        var (_, name) = await CreatePlayerAsync();

        var message = _bot.ToChat(OwnerTelegramId).Should().ContainSingle().Subject;
        message.Kind.Should().Be("Send");
        message.Text.Should().Be(string.Join('\n',
            $"⏳ {name}",
            "✅ Links verified",
            "✅ 1 Drive folder linked",
            "⬜ Photo: add it in the Mini App",
            "⬜ Waiting for the details above"));
    }

    /// <summary>Uploading the photo edits the same message: the photo is done and the player waits for an admin.</summary>
    [Fact]
    public async Task Photo_upload_edits_the_same_message()
    {
        var (id, name) = await CreatePlayerAsync();
        var sent = _bot.ToChat(OwnerTelegramId).Single();

        using var upload = await PutPhotoAsync(OwnerTelegramId, id);

        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var calls = _bot.ToChat(OwnerTelegramId);
        calls.Should().HaveCount(2);
        var edit = calls[1];
        edit.Kind.Should().Be("Edit");
        edit.MessageId.Should().Be(sent.MessageId);
        edit.Text.Should().StartWith($"⏳ {name}").And.Contain("✅ Photo added").And.EndWith("⏳ Waiting for an admin to publish");
    }

    /// <summary>A change the message does not show (the nationality) sends nothing: no edit without a visible change.</summary>
    [Fact]
    public async Task Invisible_changes_send_nothing()
    {
        var (id, _) = await CreatePlayerAsync();

        using var edit = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { nationalityCode = "IT" });

        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        _bot.ToChat(OwnerTelegramId).Should().ContainSingle("only the first message was sent");
    }

    /// <summary>A folder added by someone else updates the owner's progress message too.</summary>
    [Fact]
    public async Task Added_folder_updates_the_message()
    {
        var (id, _) = await CreatePlayerAsync();
        var sent = _bot.ToChat(OwnerTelegramId).Single();

        using var added = await SendAsync(OtherTelegramId, HttpMethod.Post, $"/miniapp/players/{id}/folders", new { driveFolderUrl = "progressExtra_0001" });

        added.StatusCode.Should().Be(HttpStatusCode.Created);
        _bot.ToChat(OwnerTelegramId).Should().Contain(call => call.Kind == "Edit" && call.MessageId == sent.MessageId && call.Text!.Contains("✅ 2 Drive folders linked"));
        _bot.ToChat(OtherTelegramId).Should().BeEmpty("the progress message belongs to the owner");
    }

    /// <summary>If the message cannot be edited any more (the analyst deleted it), a new one is sent and used from then on.</summary>
    [Fact]
    public async Task Deleted_message_is_sent_again()
    {
        var (id, _) = await CreatePlayerAsync();
        _bot.FailEdits = true;

        using var upload = await PutPhotoAsync(OwnerTelegramId, id);
        _bot.FailEdits = false;
        var resent = _bot.ToChat(OwnerTelegramId).Last();
        using var folder = await SendAsync(OwnerTelegramId, HttpMethod.Post, $"/miniapp/players/{id}/folders", new { driveFolderUrl = "progressExtra_0002" });

        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        resent.Kind.Should().Be("Send");
        resent.Text.Should().Contain("✅ Photo added");
        _bot.ToChat(OwnerTelegramId).Last().Should().Match<BotCall>(call => call.Kind == "Edit" && call.MessageId == resent.MessageId);
    }

    /// <summary>When Telegram is down, the player is still created and the request still succeeds.</summary>
    [Fact]
    public async Task Telegram_outage_never_fails_the_request()
    {
        _bot.FailSends = true;
        try
        {
            var (id, _) = await CreatePlayerAsync();

            (await ScalarAsync<long>("SELECT count(*) FROM players WHERE id = @p", id)).Should().Be(1);
        }
        finally
        {
            _bot.FailSends = false;
        }
    }

    /// <summary>Encodes a JPEG large enough for a player photo.</summary>
    /// <returns>The bytes.</returns>
    private static byte[] Jpeg()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 900, 1100);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>Parses a JSON response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The document; the caller disposes it.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>Creates a player owned by the owner analyst.</summary>
    /// <returns>The player id and name.</returns>
    private async Task<(Guid Id, string Name)> CreatePlayerAsync()
    {
        var n = Interlocked.Increment(ref _created);
        var name = $"Progress Player {(char)('A' + n)}";
        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Post, "/miniapp/players", new
        {
            category = "Men",
            name,
            nationalityCode = "IR",
            positions = new[] { "OH" },
            volleyboxUrl = $"https://volleybox.net/progress-player-p{90000 + n}",
            driveFolderUrl = $"https://drive.google.com/drive/folders/progressFolder_{n:D4}",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return (body.RootElement.GetProperty("id").GetGuid(), name);
    }

    /// <summary>Uploads a photo.</summary>
    /// <param name="telegramId">Who uploads.</param>
    /// <param name="playerId">The player.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> PutPhotoAsync(long telegramId, Guid playerId)
    {
        using var client = _api.CreateClient();
        var content = new ByteArrayContent(Jpeg());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/miniapp/players/{playerId}/photo", UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, telegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Calls the API as a team member.</summary>
    /// <param name="telegramId">Who calls.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> SendAsync(long telegramId, HttpMethod method, string path, object? body = null)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, telegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Reads one value straight from the database.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="sql">The query, with an <c>@p</c> parameter.</param>
    /// <param name="parameter">The parameter value.</param>
    /// <returns>The value.</returns>
    private async Task<T> ScalarAsync<T>(string sql, object parameter)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p", parameter);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
