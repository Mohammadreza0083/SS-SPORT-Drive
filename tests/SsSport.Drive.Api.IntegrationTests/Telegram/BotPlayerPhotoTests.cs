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
/// The player photo straight from the bot chat (T-4d): a photo replying to a player's progress message goes to that
/// player; otherwise the bot offers the analyst's players that still need a photo. The photo gets the same checks and
/// processing as the Mini App upload, and the bot says what happened.
/// </summary>
public sealed class BotPlayerPhotoTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    private const string BotToken = "123456:bot-photo-test-token";

    /// <summary>The webhook secret.</summary>
    private const string Secret = "photo_Secret-1";

    /// <summary>The analyst who creates the players.</summary>
    private const long OwnerTelegramId = 1101;

    /// <summary>An analyst with no players.</summary>
    private const long IdleTelegramId = 1102;

    /// <summary>A Telegram user outside the team.</summary>
    private const long StrangerTelegramId = 1199;

    /// <summary>The update id counter.</summary>
    private static long _updateId = 50_000;

    /// <summary>The message id counter for the photos users send.</summary>
    private static int _userMessageId = 5000;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Records the bot's messages and serves the sent files.</summary>
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
                ["Telegram:WebhookSecret"] = Secret,
                ["Telegram:Mode"] = "Webhook",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.Replace(ServiceDescriptor.Singleton<IDriveFolderReader>(new FakeDriveFolderReader()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(new InMemoryObjectStorage()));
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        foreach (var telegramId in new[] { OwnerTelegramId, IdleTelegramId })
        {
            var analyst = User.RequestAccess(telegramId, $"analyst_{telegramId}", $"Analyst {telegramId}", DateTimeOffset.UtcNow);
            analyst.Approve(Role.Analyst);
            users.Add(analyst);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        _bot.Files["good"] = Jpeg(900, 1200);
        _bot.Files["small"] = Jpeg(600, 800);
        _bot.Calls.Clear();
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// A photo replying to the player's progress message is saved for that player; the bot confirms in a reply and the
    /// progress message shows the photo.
    /// </summary>
    [Fact]
    public async Task Reply_to_the_progress_message_saves_the_photo()
    {
        var (id, name) = await CreatePlayerAsync();
        var progress = _bot.ToChat(OwnerTelegramId).Last(call => call.Kind == "Send");

        var photoMessageId = await SendPhotoAsync(OwnerTelegramId, "good", replyTo: progress.MessageId);

        (await PhotoKeyAsync(id)).Should().MatchRegex($"^players/{id}/photo-[0-9]+\\.webp$");
        _bot.ToChat(OwnerTelegramId).Should().Contain(call => call.Kind == "Send" && call.ReplyTo == photoMessageId && call.Text == $"✅ Photo saved for {name}.");
        _bot.ToChat(OwnerTelegramId).Should().Contain(call => call.Kind == "Edit" && call.MessageId == progress.MessageId && call.Text!.Contains("✅ Photo added"));
    }

    /// <summary>An image sent as a File is accepted the same way.</summary>
    [Fact]
    public async Task Image_file_is_accepted()
    {
        var (id, _) = await CreatePlayerAsync();
        var progress = _bot.ToChat(OwnerTelegramId).Last(call => call.Kind == "Send");

        await SendPhotoAsync(OwnerTelegramId, "good", replyTo: progress.MessageId, asFile: true);

        (await PhotoKeyAsync(id)).Should().NotBeNull();
    }

    /// <summary>
    /// Without a reply, the bot answers the photo with the analyst's players that still need one; tapping a player saves
    /// the photo for them and turns the picker into the confirmation.
    /// </summary>
    [Fact]
    public async Task Without_a_reply_the_bot_offers_the_players()
    {
        var (first, _) = await CreatePlayerAsync();
        var (second, secondName) = await CreatePlayerAsync();

        var photoMessageId = await SendPhotoAsync(OwnerTelegramId, "good", replyTo: null);
        var picker = _bot.ToChat(OwnerTelegramId).Last();

        picker.Kind.Should().Be("Send");
        picker.ReplyTo.Should().Be(photoMessageId);
        picker.ButtonData.Should().Contain([$"photo:{first:N}", $"photo:{second:N}"]);

        await PressAsync(OwnerTelegramId, $"photo:{second:N}", picker.MessageId!.Value, photoMessageId, "good");

        (await PhotoKeyAsync(second)).Should().NotBeNull();
        (await PhotoKeyAsync(first)).Should().BeNull();
        _bot.ToChat(OwnerTelegramId).Should().Contain(call => call.Kind == "Edit" && call.MessageId == picker.MessageId && call.Text == $"✅ Photo saved for {secondName}.");
    }

    /// <summary>The picker pages five players at a time (decisions §2b), newest first.</summary>
    [Fact]
    public async Task Picker_is_paged()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add((await CreatePlayerAsync()).Id);
        }

        var photoMessageId = await SendPhotoAsync(OwnerTelegramId, "good", replyTo: null);
        var picker = _bot.ToChat(OwnerTelegramId).Last();
        picker.ButtonData.Count(data => data.StartsWith("photo:", StringComparison.Ordinal)).Should().Be(5);
        picker.ButtonData.Should().Contain("photo-page:2").And.Contain($"photo:{ids[^1]:N}", "newest first");

        await PressAsync(OwnerTelegramId, "photo-page:2", picker.MessageId!.Value, photoMessageId, "good");

        var second = _bot.ToChat(OwnerTelegramId).Last();
        second.Kind.Should().Be("Edit");
        second.ButtonData.Should().Contain([$"photo:{ids[0]:N}", "photo-page:1"]).And.HaveCount(2);
    }

    /// <summary>A photo Telegram shrank below 800 px is refused with the tip to send it as a File.</summary>
    [Fact]
    public async Task Small_photo_asks_for_a_file()
    {
        var (id, _) = await CreatePlayerAsync();
        var progress = _bot.ToChat(OwnerTelegramId).Last(call => call.Kind == "Send");

        await SendPhotoAsync(OwnerTelegramId, "small", replyTo: progress.MessageId);

        (await PhotoKeyAsync(id)).Should().BeNull();
        _bot.ToChat(OwnerTelegramId).Last().Text.Should().Contain("800 px").And.Contain("as a File");
    }

    /// <summary>A file over 15 MB is refused before it is downloaded.</summary>
    [Fact]
    public async Task Oversized_file_is_refused_without_downloading()
    {
        var (_, _) = await CreatePlayerAsync();
        var progress = _bot.ToChat(OwnerTelegramId).Last(call => call.Kind == "Send");

        await SendPhotoAsync(OwnerTelegramId, "not-served", replyTo: progress.MessageId, asFile: true, fileSize: (15 * 1024 * 1024) + 1);

        _bot.ToChat(OwnerTelegramId).Last().Text.Should().Contain("15 MB");
    }

    /// <summary>An analyst with no player waiting for a photo is told how to change a photo instead.</summary>
    [Fact]
    public async Task No_waiting_players_explains_the_reply()
    {
        await SendPhotoAsync(IdleTelegramId, "good", replyTo: null);

        _bot.ToChat(IdleTelegramId).Single().Text.Should().Contain("reply to the player's progress message");
    }

    /// <summary>Someone outside the team is pointed to /start.</summary>
    [Fact]
    public async Task Stranger_is_asked_for_access()
    {
        await SendPhotoAsync(StrangerTelegramId, "good", replyTo: null);

        _bot.ToChat(StrangerTelegramId).Single().Text.Should().Contain("/start");
    }

    /// <summary>Encodes a plain JPEG.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Jpeg(int width, int height)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, (uint)width, (uint)height);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>Builds the Telegram user a message comes from.</summary>
    /// <param name="telegramId">The Telegram id.</param>
    /// <returns>The user object.</returns>
    private static object From(long telegramId) => new { id = telegramId, is_bot = false, first_name = $"User {telegramId}" };

    /// <summary>Builds a message carrying a photo, as Telegram sends it.</summary>
    /// <param name="telegramId">The sender.</param>
    /// <param name="messageId">The message id.</param>
    /// <param name="fileId">The file id.</param>
    /// <param name="asFile">Whether it was sent as a File.</param>
    /// <param name="fileSize">The size Telegram reports.</param>
    /// <param name="replyTo">The message it replies to, or <see langword="null"/>.</param>
    /// <returns>The message object.</returns>
    private static Dictionary<string, object?> PhotoMessage(long telegramId, int messageId, string fileId, bool asFile, long fileSize, int? replyTo)
    {
        var message = new Dictionary<string, object?>
        {
            ["message_id"] = messageId,
            ["date"] = 1761379200,
            ["chat"] = new { id = telegramId, type = "private" },
            ["from"] = From(telegramId),
        };
        if (asFile)
        {
            message["document"] = new { file_id = fileId, file_unique_id = $"u{fileId}", file_name = "player.jpg", mime_type = "image/jpeg", file_size = fileSize };
        }
        else
        {
            message["photo"] = new[] { new { file_id = fileId, file_unique_id = $"u{fileId}", width = 960, height = 1280, file_size = fileSize } };
        }

        if (replyTo is { } original)
        {
            message["reply_to_message"] = new { message_id = original, date = 1761379200, chat = new { id = telegramId, type = "private" }, text = "⏳" };
        }

        return message;
    }

    /// <summary>Sends a photo to the bot.</summary>
    /// <param name="telegramId">The sender.</param>
    /// <param name="fileId">The file id.</param>
    /// <param name="replyTo">The message it replies to, or <see langword="null"/>.</param>
    /// <param name="asFile">Whether it is sent as a File.</param>
    /// <param name="fileSize">The size Telegram reports.</param>
    /// <returns>The photo message's id.</returns>
    private async Task<int> SendPhotoAsync(long telegramId, string fileId, int? replyTo, bool asFile = false, long fileSize = 200_000)
    {
        var messageId = Interlocked.Increment(ref _userMessageId);
        await PostUpdateAsync(new Dictionary<string, object?>
        {
            ["update_id"] = Interlocked.Increment(ref _updateId),
            ["message"] = PhotoMessage(telegramId, messageId, fileId, asFile, fileSize, replyTo),
        });
        return messageId;
    }

    /// <summary>Presses a button on the bot's picker, which replies to the photo.</summary>
    /// <param name="telegramId">Who presses.</param>
    /// <param name="data">The callback data.</param>
    /// <param name="pickerMessageId">The picker message.</param>
    /// <param name="photoMessageId">The photo message the picker replies to.</param>
    /// <param name="fileId">The photo's file id.</param>
    /// <returns>A task that completes when the API answered.</returns>
    private Task PressAsync(long telegramId, string data, int pickerMessageId, int photoMessageId, string fileId) =>
        PostUpdateAsync(new Dictionary<string, object?>
        {
            ["update_id"] = Interlocked.Increment(ref _updateId),
            ["callback_query"] = new Dictionary<string, object?>
            {
                ["id"] = $"cb{Interlocked.Increment(ref _updateId)}",
                ["from"] = From(telegramId),
                ["chat_instance"] = "1",
                ["data"] = data,
                ["message"] = new Dictionary<string, object?>
                {
                    ["message_id"] = pickerMessageId,
                    ["date"] = 1761379200,
                    ["chat"] = new { id = telegramId, type = "private" },
                    ["text"] = "Which player is this photo for?",
                    ["reply_to_message"] = PhotoMessage(telegramId, photoMessageId, fileId, false, 200_000, null),
                },
            },
        });

    /// <summary>Posts an update to the webhook and expects 200.</summary>
    /// <param name="update">The update.</param>
    /// <returns>A task that completes when the API answered.</returns>
    private async Task PostUpdateAsync(object update)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/telegram/webhook", UriKind.Relative)) { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Creates a player owned by the owner analyst through the Mini App.</summary>
    /// <returns>The player id and name.</returns>
    private async Task<(Guid Id, string Name)> CreatePlayerAsync()
    {
        var n = Interlocked.Increment(ref _created);
        var name = $"Photo Bot {(char)('A' + n)}";
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/miniapp/players", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                category = "Men",
                name,
                nationalityCode = "IR",
                positions = new[] { "OH" },
                volleyboxUrl = $"https://volleybox.net/photo-bot-p{95000 + n}",
                driveFolderUrl = $"https://drive.google.com/drive/folders/photoBotFolder_{n:D4}",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, OwnerTelegramId, DateTimeOffset.UtcNow));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), name);
    }

    /// <summary>Reads a player's photo key.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The key, or <see langword="null"/>.</returns>
    private async Task<string?> PhotoKeyAsync(Guid playerId)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT photo_key FROM players WHERE id = @p", connection);
        command.Parameters.AddWithValue("p", playerId);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
    }
}
