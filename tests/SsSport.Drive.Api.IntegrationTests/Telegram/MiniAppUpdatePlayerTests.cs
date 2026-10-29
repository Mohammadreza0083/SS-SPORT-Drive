using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// Update-player mode (T-5): every team member can read a player's card and add a Drive folder to it (the owning
/// analyst is told when someone else does); details change only by the owner while the player waits, or by an admin.
/// </summary>
public sealed class MiniAppUpdatePlayerTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:update-test-token";

    /// <summary>The analyst who creates the players.</summary>
    private const long OwnerTelegramId = 801;

    /// <summary>Another analyst.</summary>
    private const long OtherTelegramId = 802;

    /// <summary>An admin.</summary>
    private const long AdminTelegramId = 803;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Stands in for Google Drive.</summary>
    private readonly FakeDriveFolderReader _drive = new();

    /// <summary>Records the bot's messages.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own name, Volleybox id and folder.</summary>
    private int _created;

    /// <summary>Starts PostgreSQL, boots the API and stores two analysts and an admin.</summary>
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
                services.Replace(ServiceDescriptor.Singleton<IDriveFolderReader>(_drive));
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

        users.Add(User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), DateTimeOffset.UtcNow));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);

        // Approving the analysts sent them the T-2 welcome; the tests only look at what comes after.
        _bot.Calls.Clear();
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Anyone in the team reads the card; the flags say what each caller may change.</summary>
    /// <param name="telegramId">Who reads.</param>
    /// <param name="canEdit">Whether they may edit details and the photo.</param>
    [Theory]
    [InlineData(OwnerTelegramId, true)]
    [InlineData(OtherTelegramId, false)]
    [InlineData(AdminTelegramId, true)]
    public async Task Reads_the_player_card(long telegramId, bool canEdit)
    {
        var (id, name) = await CreatePlayerAsync();

        using var response = await SendAsync(telegramId, HttpMethod.Get, $"/miniapp/players/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(id);
        root.GetProperty("name").GetString().Should().Be(name);
        root.GetProperty("status").GetString().Should().Be("Waiting");
        root.GetProperty("analystName").GetString().Should().Be($"Analyst {OwnerTelegramId}");
        root.GetProperty("hasPhoto").GetBoolean().Should().BeFalse();
        root.GetProperty("folderCount").GetInt32().Should().Be(1);
        root.GetProperty("folders")[0].GetProperty("name").GetString().Should().StartWith("SS-SPORT ");
        root.GetProperty("canEditDetails").GetBoolean().Should().Be(canEdit);
        root.GetProperty("canChangePhoto").GetBoolean().Should().Be(canEdit);
    }

    /// <summary>An unknown player is 404.</summary>
    [Fact]
    public async Task Unknown_player_is_404()
    {
        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Get, $"/miniapp/players/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Another analyst adds a second folder: it is linked under its Drive name, the first folder stays, and the owner
    /// is told by the bot.
    /// </summary>
    [Fact]
    public async Task Another_analyst_adds_a_folder_and_the_owner_is_told()
    {
        var (id, name) = await CreatePlayerAsync();

        using var response = await SendAsync(OtherTelegramId, HttpMethod.Post, $"/miniapp/players/{id}/folders", new { driveFolderUrl = "https://drive.google.com/drive/folders/extraFolder_0001" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("driveFolderId").GetString().Should().Be("extraFolder_0001");
        body.RootElement.GetProperty("name").GetString().Should().Be("SS-SPORT extraFolder_0001");
        (await ScalarAsync<long>("SELECT count(*) FROM drive_folders WHERE player_id = @p", id)).Should().Be(2, "a new folder is added, never a replacement");
        // The owner's chat also holds the player's progress message (T-6a); this test looks at the folder notice only.
        _bot.ToChat(OwnerTelegramId).Should().ContainSingle(call => call.Kind == "Send" && call.Text!.Contains("added a Drive folder"))
            .Which.Text.Should().Contain(name).And.Contain($"Analyst {OtherTelegramId}").And.Contain("SS-SPORT extraFolder_0001");
    }

    /// <summary>The owner adding a folder to their own player is not told about it.</summary>
    [Fact]
    public async Task Owner_adding_a_folder_gets_no_notice()
    {
        var (id, _) = await CreatePlayerAsync();

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Post, $"/miniapp/players/{id}/folders", new { driveFolderUrl = "extraFolder_0002" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _bot.ToChat(OwnerTelegramId).Should().NotContain(call => call.Text!.Contains("added a Drive folder"), "the progress message updates, but no notice is sent");
    }

    /// <summary>Adding a folder runs the same checks as creating a player.</summary>
    /// <param name="problem">What goes wrong.</param>
    /// <param name="status">The expected status.</param>
    /// <param name="code">The expected code.</param>
    [Theory]
    [InlineData("taken", HttpStatusCode.UnprocessableEntity, "drive_folder.taken")]
    [InlineData("no_access", HttpStatusCode.UnprocessableEntity, "drive_folder.no_access")]
    [InlineData("invalid", HttpStatusCode.UnprocessableEntity, "drive_folder.link_invalid")]
    [InlineData("drive_down", HttpStatusCode.ServiceUnavailable, "drive.unavailable")]
    public async Task Adding_a_folder_is_checked(string problem, HttpStatusCode status, string code)
    {
        var (id, _) = await CreatePlayerAsync();
        var (other, _) = await CreatePlayerAsync();
        var folder = problem switch
        {
            "taken" => await ScalarAsync<string>("SELECT drive_folder_id FROM drive_folders WHERE player_id = @p", other),
            "invalid" => "https://drive.google.com/file/d/1AbC-dEf_GhIjKlMnOp/view",
            _ => "extraFolder_0003",
        };
        if (problem == "no_access")
        {
            _drive.Set(folder, DriveFolderStatus.NotFound);
        }

        _drive.Unavailable = problem == "drive_down";

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Post, $"/miniapp/players/{id}/folders", new { driveFolderUrl = folder });

        response.StatusCode.Should().Be(status);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
        (await ScalarAsync<long>("SELECT count(*) FROM drive_folders WHERE player_id = @p", id)).Should().Be(1);
    }

    /// <summary>The live Drive check names the player a folder already belongs to, so the form can open it.</summary>
    [Fact]
    public async Task Drive_check_names_the_player_of_a_taken_folder()
    {
        var (id, name) = await CreatePlayerAsync();
        var folder = await ScalarAsync<string>("SELECT drive_folder_id FROM drive_folders WHERE player_id = @p", id);

        using var response = await SendAsync(OtherTelegramId, HttpMethod.Get, "/miniapp/drive-folder?url=" + folder);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("existingPlayerId").GetGuid().Should().Be(id);
        body.RootElement.GetProperty("existingPlayerName").GetString().Should().Be(name);
    }

    /// <summary>The owner edits the details of a waiting player; a new name gets its own slug.</summary>
    [Fact]
    public async Task Owner_edits_a_waiting_player()
    {
        var (id, _) = await CreatePlayerAsync();

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new
        {
            name = "kian moradi",
            nationalityCode = "IT",
            positions = new[] { "S" },
            category = "Women",
            volleyboxUrl = "https://volleybox.net/kian-moradi-p91001",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.GetProperty("name").GetString().Should().Be("Kian Moradi");
        root.GetProperty("slug").GetString().Should().Be("kian-moradi");
        root.GetProperty("nationalityCode").GetString().Should().Be("IT");
        root.GetProperty("positions").EnumerateArray().Select(position => position.GetString()).Should().Equal("S");
        root.GetProperty("category").GetString().Should().Be("Women");
        root.GetProperty("volleyboxId").GetInt32().Should().Be(91001);
    }

    /// <summary>Fields left out stay as they are.</summary>
    [Fact]
    public async Task Missing_fields_are_unchanged()
    {
        var (id, name) = await CreatePlayerAsync();

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { nationalityCode = "PL" });

        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("name").GetString().Should().Be(name);
        body.RootElement.GetProperty("nationalityCode").GetString().Should().Be("PL");
    }

    /// <summary>Another analyst cannot edit; after Publish only an admin can.</summary>
    [Fact]
    public async Task Details_are_guarded()
    {
        var (id, _) = await CreatePlayerAsync();

        using var other = await SendAsync(OtherTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { nationalityCode = "PL" });
        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await ExecuteAsync("UPDATE players SET status = 'Published', published_at = now() WHERE id = @p", id);

        using var owner = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { nationalityCode = "PL" });
        using var admin = await SendAsync(AdminTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { nationalityCode = "PL" });
        using var card = await SendAsync(OwnerTelegramId, HttpMethod.Get, $"/miniapp/players/{id}");
        using var cardBody = await ReadJsonAsync(card);

        owner.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a published player is changed by an admin only");
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
        cardBody.RootElement.GetProperty("canEditDetails").GetBoolean().Should().BeFalse();
        cardBody.RootElement.GetProperty("canChangePhoto").GetBoolean().Should().BeTrue("the owner keeps the photo");
    }

    /// <summary>A Volleybox id another player has is refused; a name another player has gets a numbered slug.</summary>
    [Fact]
    public async Task Edits_keep_volleybox_and_slugs_unique()
    {
        var (id, _) = await CreatePlayerAsync();
        var (other, otherName) = await CreatePlayerAsync();
        var otherVolleybox = await ScalarAsync<int>("SELECT volleybox_player_id FROM players WHERE id = @p", other);

        using var volleybox = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { volleyboxUrl = $"https://volleybox.net/someone-p{otherVolleybox}" });
        using var name = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { name = otherName });

        volleybox.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var volleyboxBody = await ReadJsonAsync(volleybox);
        volleyboxBody.RootElement.GetProperty("code").GetString().Should().Be("player.volleybox_taken");
        using var nameBody = await ReadJsonAsync(name);
        nameBody.RootElement.GetProperty("slug").GetString().Should().EndWith("-2");
    }

    /// <summary>Broken values in an edit are refused like in the form.</summary>
    [Fact]
    public async Task Bad_edits_are_refused()
    {
        var (id, _) = await CreatePlayerAsync();

        using var positions = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { positions = new[] { "OH", "OPP", "MB" } });
        using var nationality = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}", new { nationalityCode = "QQ" });

        positions.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        nationality.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>The three endpoints are documented under "Mini App" with the initData security scheme.</summary>
    [Fact]
    public async Task Endpoints_are_documented()
    {
        using var client = _api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var (path, method, success) in new[] { ("/miniapp/players/{id}", "get", "200"), ("/miniapp/players/{id}", "patch", "200"), ("/miniapp/players/{id}/folders", "post", "201") })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("Mini App");
            operation.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("TelegramMiniApp", out _)).Should().BeTrue();
            var responses = operation.GetProperty("responses");
            responses.GetProperty(success).GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue($"{method} {path} shows an example");
            responses.TryGetProperty("404", out _).Should().BeTrue();
        }

        paths.GetProperty("/miniapp/players/{id}").GetProperty("patch").GetProperty("responses").TryGetProperty("403", out _).Should().BeTrue();
        paths.GetProperty("/miniapp/players/{id}/folders").GetProperty("post").GetProperty("responses").TryGetProperty("503", out _).Should().BeTrue();
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
        // Names are stored Title Cased, so build them that way.
        var name = $"Update Player {(char)('A' + n)}";
        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Post, "/miniapp/players", new
        {
            category = "Men",
            name,
            nationalityCode = "IR",
            positions = new[] { "OH" },
            volleyboxUrl = $"https://volleybox.net/update-player-p{80000 + n}",
            driveFolderUrl = $"https://drive.google.com/drive/folders/updateFolder_{n:D4}",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return (body.RootElement.GetProperty("id").GetGuid(), name);
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

    /// <summary>Runs a statement straight against the database.</summary>
    /// <param name="sql">The statement, with an <c>@p</c> parameter.</param>
    /// <param name="parameter">The parameter value.</param>
    /// <returns>A task that completes when it has run.</returns>
    private async Task ExecuteAsync(string sql, object parameter)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p", parameter);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
