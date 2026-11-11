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
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// My players (T-7a): an analyst lists their own players and an admin lists everyone's, paged, searched and filtered;
/// the player detail lists the files in page order; the bot's My players button opens the screen in the Mini App.
/// The edit-video sheet (T-7b) changes a file's title, tags and hero (owner or admin) and hides it (admins only).
/// </summary>
public sealed class MiniAppMyPlayersTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:my-players-test-token";

    /// <summary>The webhook secret for bot updates.</summary>
    private const string Secret = "my-players-secret";

    /// <summary>Where the Mini App is served.</summary>
    private const string MiniAppUrl = "https://drive.ss-sport.co/miniapp";

    /// <summary>The analyst who creates most players.</summary>
    private const long OwnerTelegramId = 901;

    /// <summary>Another analyst.</summary>
    private const long OtherTelegramId = 902;

    /// <summary>An admin.</summary>
    private const long AdminTelegramId = 903;

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

    /// <summary>Counts bot updates.</summary>
    private int _updateId;

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
                ["Telegram:WebhookSecret"] = Secret,
                ["Telegram:MiniAppUrl"] = MiniAppUrl,
                ["Telegram:Mode"] = "Webhook",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.UseInlineBotNotices();
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
        _bot.Calls.Clear();
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>An analyst sees only their own players, newest first; an admin sees every player.</summary>
    /// <param name="telegramId">Who lists.</param>
    /// <param name="expected">How many players they see.</param>
    [Theory]
    [InlineData(OwnerTelegramId, 2)]
    [InlineData(OtherTelegramId, 1)]
    [InlineData(AdminTelegramId, 3)]
    public async Task Lists_own_players_or_all_for_an_admin(long telegramId, int expected)
    {
        await CreatePlayerAsync(OwnerTelegramId);
        await CreatePlayerAsync(OwnerTelegramId);
        await CreatePlayerAsync(OtherTelegramId);

        using var response = await SendAsync(telegramId, HttpMethod.Get, "/miniapp/players");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(expected);
        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(20);
        var created = body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        created.Should().BeInDescendingOrder();
    }

    /// <summary>A row carries what the list shows: photo flag, status, counts and whether it needs attention.</summary>
    [Fact]
    public async Task Rows_show_status_and_counts()
    {
        var (id, name) = await CreatePlayerAsync(OwnerTelegramId);
        await AddAssetAsync(id, "highlights.mp4", 0, asset => asset.Fail("ffmpeg exited with code 1"));

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Get, "/miniapp/players");

        using var body = await ReadJsonAsync(response);
        var row = body.RootElement.GetProperty("items")[0];
        row.GetProperty("id").GetGuid().Should().Be(id);
        row.GetProperty("name").GetString().Should().Be(name);
        row.GetProperty("nationalityCode").GetString().Should().Be("IR");
        row.GetProperty("positions").EnumerateArray().Select(position => position.GetString()).Should().Equal("OH");
        row.GetProperty("hasPhoto").GetBoolean().Should().BeFalse();
        row.GetProperty("status").GetString().Should().Be("Waiting");
        row.GetProperty("isComplete").GetBoolean().Should().BeFalse();
        row.GetProperty("fileCount").GetInt32().Should().Be(1);
        row.GetProperty("failedCount").GetInt32().Should().Be(1);
        row.GetProperty("needsAttention").GetBoolean().Should().BeTrue();
    }

    /// <summary>The search and the filter chips narrow the list.</summary>
    [Fact]
    public async Task Search_and_filter_narrow_the_list()
    {
        var (first, firstName) = await CreatePlayerAsync(OwnerTelegramId);
        await CreatePlayerAsync(OwnerTelegramId);
        await AddAssetAsync(first, "queued.mp4", 0);

        using var search = await SendAsync(OwnerTelegramId, HttpMethod.Get, "/miniapp/players?search=" + Uri.EscapeDataString(firstName.ToUpperInvariant()));
        using var processing = await SendAsync(OwnerTelegramId, HttpMethod.Get, "/miniapp/players?filter=processing");
        using var live = await SendAsync(OwnerTelegramId, HttpMethod.Get, "/miniapp/players?filter=Live");

        using var searchBody = await ReadJsonAsync(search);
        searchBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Should().Equal(first);
        using var processingBody = await ReadJsonAsync(processing);
        processingBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Should().Equal(first);
        using var liveBody = await ReadJsonAsync(live);
        liveBody.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    /// <summary>An unknown filter, a too long search and a page size over 100 are 400s naming the field.</summary>
    /// <param name="query">The query string.</param>
    /// <param name="field">The field the error is keyed by.</param>
    [Theory]
    [InlineData("?filter=archived", "filter")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?search=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "search")]
    public async Task Bad_list_requests_are_400(string query, string field)
    {
        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Get, "/miniapp/players" + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    /// <summary>The player detail lists the files in page order, with the hero marked.</summary>
    [Fact]
    public async Task Files_are_listed_in_page_order()
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var third = await AddAssetAsync(id, "report.pdf", 2, asset => MakeReady(asset, 4));
        var first = await AddAssetAsync(id, "highlights.mp4", 0);
        var second = await AddAssetAsync(id, "full match.mp4", 1, asset => asset.Hide());
        await SetHeroAsync(id, first);

        using var response = await SendAsync(OtherTelegramId, HttpMethod.Get, $"/miniapp/players/{id}/files");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(3);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(first, second, third);
        items[0].GetProperty("isHero").GetBoolean().Should().BeTrue();
        items[0].GetProperty("kind").GetString().Should().Be("Video");
        items[0].GetProperty("state").GetString().Should().Be("Queued");
        items[0].GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Equal("Highlight");
        items[1].GetProperty("visibility").GetString().Should().Be("Hidden");
        items[2].GetProperty("isHero").GetBoolean().Should().BeFalse();
        items[2].GetProperty("pageCount").GetInt32().Should().Be(4);
    }

    /// <summary>The files of an unknown player are 404.</summary>
    [Fact]
    public async Task Files_of_an_unknown_player_are_404()
    {
        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Get, $"/miniapp/players/{Guid.CreateVersion7()}/files");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The My players button and /players send a button that opens the screen in the Mini App.</summary>
    /// <param name="viaCommand">Whether the analyst typed /players instead of pressing the button.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bot_opens_my_players_in_the_mini_app(bool viaCommand)
    {
        if (viaCommand)
        {
            await MessageAsync(OwnerTelegramId, "/players");
        }
        else
        {
            await CallbackAsync(OwnerTelegramId, "menu:players");
        }

        var sent = _bot.ToChat(OwnerTelegramId).Should().ContainSingle(call => call.Kind == "Send").Which;
        var button = sent.Keyboard!.Rows.Should().ContainSingle().Which.Should().ContainSingle().Which;
        button.WebAppUrl.Should().Be(MiniAppUrl + "/players");
    }

    /// <summary>
    /// The New player button starts the chat (T-10a) and still offers the new-player form in the Mini App.
    /// </summary>
    [Fact]
    public async Task Bot_offers_the_new_player_form()
    {
        await CallbackAsync(OwnerTelegramId, "menu:new");

        _bot.ToChat(OwnerTelegramId).Should().ContainSingle(call => call.Kind == "Send")
            .Which.Keyboard!.Rows.SelectMany(row => row).Should().Contain(button => button.WebAppUrl == MiniAppUrl + "/new");
    }

    /// <summary>/players from someone outside the team gets the welcome, not the Mini App.</summary>
    [Fact]
    public async Task Players_command_from_a_stranger_gets_the_welcome()
    {
        await MessageAsync(950, "/players");

        _bot.ToChat(950).Should().ContainSingle().Which.ButtonData.Should().Contain("access:request");
    }

    /// <summary>Both endpoints are documented under "Mini App" with an example page and the initData scheme.</summary>
    [Fact]
    public async Task Endpoints_are_documented()
    {
        using var client = _api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in new[] { "/miniapp/players", "/miniapp/players/{id}/files" })
        {
            var operation = paths.GetProperty(path).GetProperty("get");
            operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("Mini App");
            operation.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("TelegramMiniApp", out _)).Should().BeTrue();
            var responses = operation.GetProperty("responses");
            responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("example").GetProperty("items").GetArrayLength().Should().BePositive();
            responses.TryGetProperty("400", out _).Should().BeTrue();
            responses.TryGetProperty("401", out _).Should().BeTrue();
        }

        paths.GetProperty("/miniapp/players").GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString()).Should().Contain(["search", "filter", "page", "pageSize"]);
        paths.GetProperty("/miniapp/players/{id}/files").GetProperty("get").GetProperty("responses").TryGetProperty("404", out _).Should().BeTrue();
    }

    /// <summary>The owner changes a file's title and tags; the title then stops following Drive renames.</summary>
    [Fact]
    public async Task Owner_edits_title_and_tags()
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var file = await AddAssetAsync(id, "highlights.mp4", 0);

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { title = "Best spikes 25-26", tags = new[] { "Spike", "highlight" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("title").GetString().Should().Be("Best spikes 25-26");
        body.RootElement.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Equal("Highlight", "Spike");
        (await ScalarAsync<bool>("SELECT title_is_custom FROM assets WHERE id = @p", file)).Should().BeTrue();
    }

    /// <summary>Fields left out stay as they are, and an empty tag list clears the tags.</summary>
    [Fact]
    public async Task Missing_file_fields_are_unchanged()
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var file = await AddAssetAsync(id, "highlights.mp4", 0);

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { tags = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("title").GetString().Should().Be("highlights");
        body.RootElement.GetProperty("tags").GetArrayLength().Should().Be(0);
        (await ScalarAsync<bool>("SELECT title_is_custom FROM assets WHERE id = @p", file)).Should().BeFalse();
    }

    /// <summary>The owner makes a video the hero; choosing another moves it, and false clears it.</summary>
    [Fact]
    public async Task Owner_chooses_the_hero()
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var first = await AddAssetAsync(id, "highlights.mp4", 0);
        var second = await AddAssetAsync(id, "full match.mp4", 1);

        using var chooseFirst = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{first}", new { isHero = true });
        using var chooseSecond = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{second}", new { isHero = true });

        chooseFirst.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await ReadJsonAsync(chooseSecond))
        {
            body.RootElement.GetProperty("isHero").GetBoolean().Should().BeTrue();
        }

        (await ScalarAsync<Guid>("SELECT hero_asset_id FROM players WHERE id = @p", id)).Should().Be(second);

        using var clear = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{second}", new { isHero = false });

        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ScalarAsync<object>("SELECT hero_asset_id FROM players WHERE id = @p", id)).Should().Be(DBNull.Value);
    }

    /// <summary>Only a visible video can be the hero.</summary>
    /// <param name="fileName">The file.</param>
    /// <param name="hidden">Whether it is hidden.</param>
    /// <param name="code">The rule it breaks.</param>
    [Theory]
    [InlineData("stats.pdf", false, "asset.hero_not_video")]
    [InlineData("highlights.mp4", true, "asset.hero_hidden")]
    public async Task Hero_must_be_a_visible_video(string fileName, bool hidden, string code)
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var file = await AddAssetAsync(id, fileName, 0, hidden ? asset => asset.Hide() : null);

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { isHero = true });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    /// <summary>
    /// Only an admin hides and shows a file; both are audited with the admin as the actor, and hiding the hero clears it.
    /// </summary>
    [Fact]
    public async Task Admin_hides_and_shows_a_file()
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var file = await AddAssetAsync(id, "highlights.mp4", 0);
        using (var hero = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { isHero = true }))
        {
            hero.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var byOwner = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { hidden = true });
        using var hide = await SendAsync(AdminTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { hidden = true });

        byOwner.StatusCode.Should().Be(HttpStatusCode.Forbidden, "only an admin hides files");
        hide.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await ReadJsonAsync(hide))
        {
            body.RootElement.GetProperty("visibility").GetString().Should().Be("Hidden");
            body.RootElement.GetProperty("isHero").GetBoolean().Should().BeFalse();
        }

        (await ScalarAsync<object>("SELECT hero_asset_id FROM players WHERE id = @p", id)).Should().Be(DBNull.Value, "hiding the hero clears it");
        (await ScalarAsync<long>("SELECT a.telegram_id FROM audit_entries e JOIN users a ON a.id = e.actor_user_id WHERE e.action = 'asset.hidden' AND e.target_id = @p", file.ToString()))
            .Should().Be(AdminTelegramId);

        using var show = await SendAsync(AdminTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { hidden = false });

        show.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ScalarAsync<long>("SELECT count(*) FROM audit_entries WHERE action = 'asset.shown' AND target_id = @p", file.ToString())).Should().Be(1);
    }

    /// <summary>Another analyst cannot edit; an admin can edit any player's file.</summary>
    [Fact]
    public async Task Files_are_edited_by_the_owner_or_an_admin()
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var file = await AddAssetAsync(id, "highlights.mp4", 0);

        using var other = await SendAsync(OtherTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { title = "Mine now" });
        using var admin = await SendAsync(AdminTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", new { title = "Checked by admin" });

        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The card tells the Mini App who may edit files and who may hide them.</summary>
    /// <param name="telegramId">Who reads the card.</param>
    /// <param name="canEdit">Whether they may edit files.</param>
    /// <param name="canHide">Whether they may hide files.</param>
    [Theory]
    [InlineData(OwnerTelegramId, true, false)]
    [InlineData(OtherTelegramId, false, false)]
    [InlineData(AdminTelegramId, true, true)]
    public async Task Card_says_who_may_edit_files(long telegramId, bool canEdit, bool canHide)
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);

        using var response = await SendAsync(telegramId, HttpMethod.Get, $"/miniapp/players/{id}");

        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("canEditFiles").GetBoolean().Should().Be(canEdit);
        body.RootElement.GetProperty("canHideFiles").GetBoolean().Should().Be(canHide);
    }

    /// <summary>An unknown file, or a file of another player, is 404.</summary>
    [Fact]
    public async Task Files_of_other_players_are_404()
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var (other, _) = await CreatePlayerAsync(OwnerTelegramId);
        var otherFile = await AddAssetAsync(other, "highlights.mp4", 0);

        using var unknown = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{Guid.CreateVersion7()}", new { title = "x" });
        using var wrongPlayer = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{otherFile}", new { title = "x" });

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongPlayer.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>An empty or too long title and an unknown tag are 400s naming the field.</summary>
    /// <param name="field">The field sent wrong.</param>
    [Theory]
    [InlineData("title")]
    [InlineData("longTitle")]
    [InlineData("tags")]
    public async Task Bad_file_edits_are_400(string field)
    {
        var (id, _) = await CreatePlayerAsync(OwnerTelegramId);
        var file = await AddAssetAsync(id, "highlights.mp4", 0);
        object edit = field switch
        {
            "title" => new { title = "  " },
            "longTitle" => new { title = new string('a', 201) },
            _ => new { tags = new[] { "Highlight", "Dunk" } },
        };

        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Patch, $"/miniapp/players/{id}/files/{file}", edit);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("errors").TryGetProperty(field == "longTitle" ? "title" : field, out _).Should().BeTrue();
    }

    /// <summary>The edit endpoint is documented with request and response examples and every refusal.</summary>
    [Fact]
    public async Task Edit_endpoint_is_documented()
    {
        using var client = _api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/miniapp/players/{id}/files/{fileId}").GetProperty("patch");

        operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("Mini App");
        operation.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("TelegramMiniApp", out _)).Should().BeTrue();
        operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue();
        var responses = operation.GetProperty("responses");
        responses.GetProperty("200").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue();
        foreach (var status in new[] { "400", "401", "403", "404", "409", "422" })
        {
            responses.TryGetProperty(status, out _).Should().BeTrue($"{status} is documented");
        }
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

    /// <summary>Parses a JSON response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The document; the caller disposes it.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>Takes a document through processing, as ingest will.</summary>
    /// <param name="asset">The queued document.</param>
    /// <param name="pages">Its page count.</param>
    private static void MakeReady(Asset asset, int pages)
    {
        asset.AttachOriginal(StorageKey.Create($"originals/{asset.Id}"));
        asset.CompleteDocument(pages);
    }

    /// <summary>Creates a player through the Mini App as an analyst.</summary>
    /// <param name="telegramId">The analyst.</param>
    /// <returns>The player id and name.</returns>
    private async Task<(Guid Id, string Name)> CreatePlayerAsync(long telegramId)
    {
        var n = Interlocked.Increment(ref _created);
        var name = $"Listed Player {(char)('A' + n)}{(char)('a' + n)}";
        using var response = await SendAsync(telegramId, HttpMethod.Post, "/miniapp/players", new
        {
            category = "Men",
            name,
            nationalityCode = "IR",
            positions = new[] { "OH" },
            volleyboxUrl = $"https://volleybox.net/listed-player-p{90000 + n}",
            driveFolderUrl = $"https://drive.google.com/drive/folders/listedFolder_{n:D4}",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return (body.RootElement.GetProperty("id").GetGuid(), name);
    }

    /// <summary>Stores a file of a player, as ingest will.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fileName">The file name; its words give the tags.</param>
    /// <param name="sortOrder">Its position on the page.</param>
    /// <param name="change">Moves the file to another state, if given.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddAssetAsync(Guid playerId, string fileName, int sortOrder, Action<Asset>? change = null)
    {
        using var scope = _api.Services.CreateScope();
        var tags = AssetTagger.Classify([], fileName).Tags;
        var asset = Asset.Add(playerId, null, AssetSource.Drive, null, AssetFile.Create(fileName, Guid.NewGuid().ToString("N"), 2048), Path.GetFileNameWithoutExtension(fileName), tags, sortOrder, DateTimeOffset.UtcNow);
        change?.Invoke(asset);
        scope.ServiceProvider.GetRequiredService<IRepository<Asset, Guid>>().Add(asset);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return asset.Id;
    }

    /// <summary>Makes a file the player's hero video.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="assetId">The file.</param>
    /// <returns>A task that completes when it is saved.</returns>
    private async Task SetHeroAsync(Guid playerId, Guid assetId)
    {
        using var scope = _api.Services.CreateScope();
        var player = await scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().GetByIdAsync(playerId, TestContext.Current.CancellationToken);
        var asset = await scope.ServiceProvider.GetRequiredService<IRepository<Asset, Guid>>().GetByIdAsync(assetId, TestContext.Current.CancellationToken);
        player!.SetHero(asset!);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
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

    /// <summary>Posts a text message update from a user.</summary>
    /// <param name="fromId">The sender's Telegram id.</param>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the API answered 200.</returns>
    private Task MessageAsync(long fromId, string text) => PostUpdateAsync(new
    {
        update_id = Interlocked.Increment(ref _updateId),
        message = new
        {
            message_id = 1,
            date = 1761379200,
            chat = new { id = fromId, type = "private" },
            from = new { id = fromId, is_bot = false, first_name = "Reza", username = $"reza_{fromId}" },
            text,
        },
    });

    /// <summary>Posts a button press from a user.</summary>
    /// <param name="fromId">The presser's Telegram id.</param>
    /// <param name="data">The button's callback data.</param>
    /// <returns>A task that completes when the API answered 200.</returns>
    private Task CallbackAsync(long fromId, string data) => PostUpdateAsync(new
    {
        update_id = Interlocked.Increment(ref _updateId),
        callback_query = new
        {
            id = $"cb{Interlocked.Increment(ref _updateId)}",
            from = new { id = fromId, is_bot = false, first_name = "Reza", username = $"reza_{fromId}" },
            chat_instance = "1",
            data,
            message = new { message_id = 5, date = 1761379200, chat = new { id = fromId, type = "private" }, text = "…" },
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
}
