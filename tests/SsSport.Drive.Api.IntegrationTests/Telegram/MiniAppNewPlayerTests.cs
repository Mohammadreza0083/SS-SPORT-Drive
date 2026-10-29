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
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// The Mini App's new-player form (T-4a): the country list, the live Volleybox check, and creating a player that waits
/// for an admin, owned by the analyst who sent it. Duplicates are refused with stable codes.
/// </summary>
public sealed class MiniAppNewPlayerTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:new-player-test-token";

    /// <summary>The Telegram id of the active analyst used by most tests.</summary>
    private const long AnalystTelegramId = 601;

    /// <summary>A valid Drive folder id.</summary>
    private const string FolderId = "1AbC-dEf_GhIjKlMnOp";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>The analyst's user id.</summary>
    private Guid _analystId;

    /// <summary>Starts PostgreSQL, migrates it, boots the API and stores an active analyst.</summary>
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
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger())));
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        var analyst = User.RequestAccess(AnalystTelegramId, "analyst_601", "Analyst 601", DateTimeOffset.UtcNow);
        analyst.Approve(Role.Analyst);
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().Add(analyst);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        _analystId = analyst.Id;
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>The nationality picker gets the whole fixed catalogue of 245 countries, by code and English name.</summary>
    [Fact]
    public async Task Lists_the_countries()
    {
        using var response = await SendAsync(HttpMethod.Get, "/miniapp/countries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetArrayLength().Should().Be(245);
        body.RootElement.EnumerateArray().Should().Contain(country =>
            country.GetProperty("code").GetString() == "IR" && country.GetProperty("name").GetString() == "Iran");
    }

    /// <summary>Every form endpoint needs a signed-in team member.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    [Theory]
    [InlineData("GET", "/miniapp/countries")]
    [InlineData("GET", "/miniapp/volleybox?url=volleybox.net/a-p1")]
    [InlineData("POST", "/miniapp/players")]
    public async Task Form_endpoints_need_sign_in(string method, string path)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method == "POST")
        {
            request.Content = JsonContent.Create(NewPlayer());
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A new Volleybox player URL yields its id and the normalised URL, and no existing player.</summary>
    [Fact]
    public async Task Volleybox_check_for_a_new_player()
    {
        using var response = await SendAsync(HttpMethod.Get, "/miniapp/volleybox?url=" + Uri.EscapeDataString("http://www.volleybox.net/arash-tehrani-p48213/clubs"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("volleyboxId").GetInt32().Should().Be(48213);
        body.RootElement.GetProperty("url").GetString().Should().Be("https://volleybox.net/arash-tehrani-p48213");
        body.RootElement.GetProperty("existingPlayerId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>A Volleybox id that already belongs to a player names that player (the form switches to Update, T-5).</summary>
    [Fact]
    public async Task Volleybox_check_finds_an_existing_player()
    {
        using var created = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer());
        using var createdBody = await ReadJsonAsync(created);

        using var response = await SendAsync(HttpMethod.Get, "/miniapp/volleybox?url=" + Uri.EscapeDataString("https://volleybox.net/arash-tehrani-p48213"));

        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("existingPlayerId").GetGuid().Should().Be(createdBody.RootElement.GetProperty("id").GetGuid());
        body.RootElement.GetProperty("existingPlayerName").GetString().Should().Be("Arash Tehrani");
    }

    /// <summary>A team page and a non-Volleybox link are refused with their codes.</summary>
    /// <param name="url">The pasted URL.</param>
    /// <param name="code">The expected code.</param>
    [Theory]
    [InlineData("https://volleybox.net/paykan-tehran-t1234", "player.volleybox_not_a_player")]
    [InlineData("https://example.com/arash-p1", "player.volleybox_url_invalid")]
    public async Task Volleybox_check_refuses_other_pages(string url, string code)
    {
        using var response = await SendAsync(HttpMethod.Get, "/miniapp/volleybox?url=" + Uri.EscapeDataString(url));

        await AssertCodeAsync(response, code);
    }

    /// <summary>
    /// A filled form creates a waiting player owned by the analyst, with title-cased name, slug, nationality, positions
    /// and Volleybox, and links the Drive folder. Only the photo is still missing (T-4b).
    /// </summary>
    [Fact]
    public async Task Creates_a_waiting_player_owned_by_the_analyst()
    {
        using var response = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer(name: "  arash   tehrani ") with { Positions = ["oh", "OPP"] });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        var id = root.GetProperty("id").GetGuid();
        root.GetProperty("name").GetString().Should().Be("Arash Tehrani");
        root.GetProperty("slug").GetString().Should().Be("arash-tehrani");
        root.GetProperty("category").GetString().Should().Be("Men");
        root.GetProperty("nationalityCode").GetString().Should().Be("IR");
        root.GetProperty("positions").EnumerateArray().Select(position => position.GetString()).Should().Equal("OH", "OPP");
        root.GetProperty("volleyboxId").GetInt32().Should().Be(48213);
        root.GetProperty("status").GetString().Should().Be("Waiting");
        root.GetProperty("missingDetails").EnumerateArray().Select(detail => detail.GetString()).Should().Equal("Photo");

        (await ScalarAsync<Guid>("SELECT analyst_id FROM players WHERE id = @id", id)).Should().Be(_analystId);
        (await ScalarAsync<string>("SELECT drive_folder_id FROM drive_folders WHERE player_id = @id", id)).Should().Be(FolderId);
    }

    /// <summary>Two players with the same name get distinct slugs.</summary>
    [Fact]
    public async Task Same_name_gets_a_numbered_slug()
    {
        using var first = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer());
        using var second = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer(volleybox: "https://volleybox.net/arash-tehrani-p50000", folder: "2ZyX-wVu_TsRqPoNmLk"));

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(second);
        body.RootElement.GetProperty("slug").GetString().Should().Be("arash-tehrani-2");
    }

    /// <summary>A Volleybox id that is already taken is refused.</summary>
    [Fact]
    public async Task Duplicate_volleybox_is_refused()
    {
        using var first = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer());

        using var second = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer(name: "Someone Else", folder: "2ZyX-wVu_TsRqPoNmLk"));

        await AssertCodeAsync(second, "player.volleybox_taken");
    }

    /// <summary>A Drive folder already linked to a player is refused.</summary>
    [Fact]
    public async Task Duplicate_drive_folder_is_refused()
    {
        using var first = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer());

        using var second = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer(name: "Someone Else", volleybox: "https://volleybox.net/someone-else-p50001"));

        await AssertCodeAsync(second, "drive_folder.taken");
        (await ScalarAsync<long>("SELECT count(*) FROM players WHERE slug = @slug", "someone-else")).Should().Be(0, "nothing is saved when a check fails");
    }

    /// <summary>Broken business rules come back with the domain's codes.</summary>
    /// <param name="field">The field to break.</param>
    /// <param name="code">The expected code.</param>
    [Theory]
    [InlineData("name", "player.name_invalid_characters")]
    [InlineData("nationality", "player.nationality_invalid")]
    [InlineData("volleybox", "player.volleybox_not_a_player")]
    [InlineData("folder", "drive_folder.link_invalid")]
    [InlineData("positions", "player.duplicate_position")]
    public async Task Broken_rules_return_their_code(string field, string code)
    {
        var form = field switch
        {
            "name" => NewPlayer(name: "Arash 7"),
            "nationality" => NewPlayer() with { NationalityCode = "QQ" },
            "volleybox" => NewPlayer(volleybox: "https://volleybox.net/paykan-t12"),
            "folder" => NewPlayer(folder: "https://drive.google.com/file/d/1AbC-dEf_GhIjKlMnOp/view"),
            _ => NewPlayer() with { Positions = ["OH", "OH"] },
        };

        using var response = await SendAsync(HttpMethod.Post, "/miniapp/players", form);

        await AssertCodeAsync(response, code);
    }

    /// <summary>Missing or malformed fields are a 400 with the field names.</summary>
    [Fact]
    public async Task Missing_fields_are_a_validation_error()
    {
        using var response = await SendAsync(HttpMethod.Post, "/miniapp/players", new NewPlayerForm(null, "", null, [], null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        var errors = body.RootElement.GetProperty("errors");
        foreach (var field in new[] { "category", "name", "nationalityCode", "positions", "volleyboxUrl", "driveFolderUrl" })
        {
            errors.TryGetProperty(field, out _).Should().BeTrue($"{field} is required");
        }
    }

    /// <summary>More than two positions, or an unknown position, are a 400.</summary>
    /// <param name="positions">The positions sent.</param>
    [Theory]
    [InlineData("OH", "OPP", "MB")]
    [InlineData("XX")]
    public async Task Positions_are_validated(params string[] positions)
    {
        using var response = await SendAsync(HttpMethod.Post, "/miniapp/players", NewPlayer() with { Positions = positions });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("errors").TryGetProperty("positions", out _).Should().BeTrue();
    }

    /// <summary>The three endpoints are documented under "Mini App" with the initData security scheme.</summary>
    [Fact]
    public async Task Endpoints_are_documented()
    {
        using var client = _api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var (path, method, success) in new[] { ("/miniapp/countries", "get", "200"), ("/miniapp/volleybox", "get", "200"), ("/miniapp/players", "post", "201") })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("Mini App");
            operation.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("TelegramMiniApp", out _)).Should().BeTrue();
            var responses = operation.GetProperty("responses");
            responses.GetProperty(success).GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue($"{path} shows an example");
            responses.TryGetProperty("401", out _).Should().BeTrue();
            responses.TryGetProperty("403", out _).Should().BeTrue();
        }

        var create = paths.GetProperty("/miniapp/players").GetProperty("post");
        create.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue();
        create.GetProperty("responses").TryGetProperty("400", out _).Should().BeTrue();
        create.GetProperty("responses").TryGetProperty("422", out _).Should().BeTrue();
    }

    /// <summary>Builds a valid form, with the parts the tests vary.</summary>
    /// <param name="name">The name.</param>
    /// <param name="volleybox">The Volleybox URL.</param>
    /// <param name="folder">The Drive folder link.</param>
    /// <returns>The form.</returns>
    private static NewPlayerForm NewPlayer(
        string name = "Arash Tehrani",
        string volleybox = "https://volleybox.net/arash-tehrani-p48213",
        string folder = "https://drive.google.com/drive/folders/" + FolderId) =>
        new("Men", name, "IR", ["OH"], volleybox, folder);

    /// <summary>Asserts a 422 ProblemDetails with a code.</summary>
    /// <param name="response">The response.</param>
    /// <param name="code">The expected code.</param>
    /// <returns>A task that completes when checked.</returns>
    private static async Task AssertCodeAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    /// <summary>Parses a JSON response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The document; the caller disposes it.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>Calls the API as the active analyst.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path and query.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, AnalystTelegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Reads one value straight from the database.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="sql">The query, with one <c>@</c> parameter.</param>
    /// <param name="parameter">The parameter value.</param>
    /// <returns>The value.</returns>
    private async Task<T> ScalarAsync<T>(string sql, object parameter)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(sql.Contains("@slug", StringComparison.Ordinal) ? "slug" : "id", parameter);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>The form body as the Mini App sends it.</summary>
    /// <param name="Category">Men or Women.</param>
    /// <param name="Name">The full name.</param>
    /// <param name="NationalityCode">The country code.</param>
    /// <param name="Positions">The position codes, primary first.</param>
    /// <param name="VolleyboxUrl">The Volleybox player URL.</param>
    /// <param name="DriveFolderUrl">The Drive folder link.</param>
    private sealed record NewPlayerForm(string? Category, string? Name, string? NationalityCode, string[] Positions, string? VolleyboxUrl, string? DriveFolderUrl);
}
