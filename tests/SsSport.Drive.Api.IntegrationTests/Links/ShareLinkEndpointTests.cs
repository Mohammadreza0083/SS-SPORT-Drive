using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Links;

/// <summary>
/// Personal share links (L-1): admins create a link for a recipient and published players, list the links, pause,
/// resume, revoke and change the expiry; every change is audited. Anyone can ask whether <c>/r/{code}</c> opens, without
/// learning anything about links that do not.
/// </summary>
public sealed class ShareLinkEndpointTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    private const string BotToken = "123456:share-links-test-token";

    /// <summary>An admin (signs in through the Mini App until the console session exists, A-1).</summary>
    private const long AdminTelegramId = 701;

    /// <summary>An analyst.</summary>
    private const long AnalystTelegramId = 702;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>The admin's user id.</summary>
    private Guid _adminId;

    /// <summary>Counts created players so each gets its own name and Volleybox id.</summary>
    private int _players;

    /// <summary>Starts PostgreSQL, boots the API and stores an admin and an analyst.</summary>
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
                ["Telegram:WebhookSecret"] = "share-links-secret",
                ["Telegram:Mode"] = "Webhook",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.UseInlineBotNotices();
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var admin = User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), DateTimeOffset.UtcNow);
        var analyst = User.RequestAccess(AnalystTelegramId, "analyst", "Analyst", DateTimeOffset.UtcNow);
        analyst.Approve(Role.Analyst);
        users.Add(admin);
        users.Add(analyst);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        _adminId = admin.Id;
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>An admin creates a Personal link: a six-character code, the recipient, the player, the note, the expiry and the defaults.</summary>
    [Fact]
    public async Task An_admin_creates_a_personal_link()
    {
        var (playerId, playerName) = await AddPlayerAsync(published: true);
        var before = DateTimeOffset.UtcNow;

        using var response = await SendAsync(AdminTelegramId, HttpMethod.Post, "/admin/links", new
        {
            recipient = new { name = "Marco Rossi", organisation = "Lube Volley", countryCode = "IT", emails = new[] { "Marco@Lube.it" } },
            playerIds = new[] { playerId },
            note = "Watch the second set.",
            expiresInDays = 30,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        var link = body.RootElement;
        var code = link.GetProperty("code").GetString()!;
        code.Should().MatchRegex("^[A-Za-z2-9]{6}$");
        link.GetProperty("path").GetString().Should().Be($"/r/{code}");
        link.GetProperty("type").GetString().Should().Be("Personal");
        link.GetProperty("status").GetString().Should().Be("Active");
        link.GetProperty("access").GetString().Should().Be("Open");
        link.GetProperty("note").GetString().Should().Be("Watch the second set.");
        link.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(before.AddDays(30), TimeSpan.FromMinutes(1));
        link.GetProperty("verifyOnNewDevice").GetBoolean().Should().BeTrue();
        link.GetProperty("allowForwarding").GetBoolean().Should().BeTrue();
        link.GetProperty("forwardingScope").GetString().Should().Be("AnyEmail");
        link.GetProperty("notifyOnOpen").GetBoolean().Should().BeFalse();
        link.GetProperty("createdByUserId").GetGuid().Should().Be(_adminId);
        var recipient = link.GetProperty("recipient");
        recipient.GetProperty("name").GetString().Should().Be("Marco Rossi");
        recipient.GetProperty("organisation").GetString().Should().Be("Lube Volley");
        recipient.GetProperty("countryCode").GetString().Should().Be("IT");
        recipient.GetProperty("emails").EnumerateArray().Select(email => email.GetString()).Should().Equal("marco@lube.it");
        var player = link.GetProperty("players").EnumerateArray().Should().ContainSingle().Subject;
        player.GetProperty("id").GetGuid().Should().Be(playerId);
        player.GetProperty("name").GetString().Should().Be(playerName);

        (await AuditActionsAsync(link.GetProperty("id").GetGuid())).Should().Equal("share_link.created");
    }

    /// <summary>A link to several players is a Roster; a link can reuse an existing recipient; "never" leaves no expiry.</summary>
    [Fact]
    public async Task A_roster_link_reuses_a_recipient()
    {
        var (first, _) = await AddPlayerAsync(published: true);
        var (second, _) = await AddPlayerAsync(published: true);
        var recipientId = await CreateLinkAsync(first);

        using var response = await SendAsync(AdminTelegramId, HttpMethod.Post, "/admin/links", new
        {
            recipientId,
            playerIds = new[] { first, second },
            expiresInDays = (int?)null,
            allowForwarding = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("type").GetString().Should().Be("Roster");
        body.RootElement.GetProperty("recipient").GetProperty("id").GetGuid().Should().Be(recipientId);
        body.RootElement.GetProperty("players").EnumerateArray().Select(player => player.GetProperty("id").GetGuid()).Should().Equal(first, second);
        body.RootElement.GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("allowForwarding").GetBoolean().Should().BeFalse();
    }

    /// <summary>A link for a waiting player is refused (decisions §4).</summary>
    [Fact]
    public async Task A_waiting_player_cannot_be_shared()
    {
        var (playerId, _) = await AddPlayerAsync(published: false);

        using var response = await SendAsync(AdminTelegramId, HttpMethod.Post, "/admin/links", NewLinkForm(playerId));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("share_link.player_not_published");
    }

    /// <summary>Unknown players or recipients are 404s.</summary>
    [Fact]
    public async Task Unknown_players_and_recipients_are_404()
    {
        var (playerId, _) = await AddPlayerAsync(published: true);

        using var unknownPlayer = await SendAsync(AdminTelegramId, HttpMethod.Post, "/admin/links", NewLinkForm(Guid.NewGuid()));
        using var unknownRecipient = await SendAsync(AdminTelegramId, HttpMethod.Post, "/admin/links", new { recipientId = Guid.NewGuid(), playerIds = new[] { playerId } });

        unknownPlayer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownRecipient.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Malformed forms are 400s naming the field.</summary>
    /// <param name="form">Which malformed form.</param>
    /// <param name="field">The field the error is keyed by.</param>
    [Theory]
    [InlineData("no-recipient", "recipient")]
    [InlineData("both-recipients", "recipient")]
    [InlineData("bad-expiry", "expiresInDays")]
    [InlineData("no-players", "playerIds")]
    [InlineData("bad-scope", "forwardingScope")]
    public async Task Malformed_forms_are_400(string form, string field)
    {
        var (playerId, _) = await AddPlayerAsync(published: true);
        var recipient = new { name = "Marco Rossi", emails = new[] { "marco@lube.it" } };
        object body = form switch
        {
            "no-recipient" => new { playerIds = new[] { playerId } },
            "both-recipients" => new { recipientId = Guid.NewGuid(), recipient, playerIds = new[] { playerId } },
            "bad-expiry" => new { recipient, playerIds = new[] { playerId }, expiresInDays = 45 },
            "no-players" => new { recipient, playerIds = Array.Empty<Guid>() },
            _ => new { recipient, playerIds = new[] { playerId }, forwardingScope = "Everyone" },
        };

        using var response = await SendAsync(AdminTelegramId, HttpMethod.Post, "/admin/links", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    /// <summary>Only admins manage links: an analyst gets 403, a caller without a signature 401.</summary>
    [Fact]
    public async Task Only_admins_manage_links()
    {
        var (playerId, _) = await AddPlayerAsync(published: true);

        using var analyst = await SendAsync(AnalystTelegramId, HttpMethod.Post, "/admin/links", NewLinkForm(playerId));
        using var analystList = await SendAsync(AnalystTelegramId, HttpMethod.Get, "/admin/links");
        using var client = _api.CreateClient();
        using var anonymous = await client.GetAsync(new Uri("/admin/links", UriKind.Relative), TestContext.Current.CancellationToken);

        analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        analystList.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The list is paged, newest first, with the recipient and player names; it filters by status and player.</summary>
    [Fact]
    public async Task Links_are_listed_newest_first_and_filtered()
    {
        var (first, firstName) = await AddPlayerAsync(published: true);
        var (second, _) = await AddPlayerAsync(published: true);
        await CreateLinkAsync(first);
        await CreateLinkAsync(second);
        var paused = await CreateLinkIdAsync(first);
        using (await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{paused}/pause"))
        {
        }

        using var all = await SendAsync(AdminTelegramId, HttpMethod.Get, "/admin/links?pageSize=2");
        using var forPlayer = await SendAsync(AdminTelegramId, HttpMethod.Get, $"/admin/links?playerId={first}");
        using var pausedOnly = await SendAsync(AdminTelegramId, HttpMethod.Get, "/admin/links?status=paused");

        all.StatusCode.Should().Be(HttpStatusCode.OK);
        using var allBody = await ReadJsonAsync(all);
        allBody.RootElement.GetProperty("totalCount").GetInt32().Should().Be(3);
        allBody.RootElement.GetProperty("totalPages").GetInt32().Should().Be(2);
        var newest = allBody.RootElement.GetProperty("items")[0];
        newest.GetProperty("id").GetGuid().Should().Be(paused);
        newest.GetProperty("status").GetString().Should().Be("Paused");
        newest.GetProperty("recipientName").GetString().Should().Be("Marco Rossi");
        newest.GetProperty("recipientOrganisation").GetString().Should().Be("Lube Volley");
        newest.GetProperty("playerNames").EnumerateArray().Select(name => name.GetString()).Should().Equal(firstName);
        using var forPlayerBody = await ReadJsonAsync(forPlayer);
        forPlayerBody.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        using var pausedBody = await ReadJsonAsync(pausedOnly);
        pausedBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Should().Equal(paused);
    }

    /// <summary>An unknown status filter or a page size over 100 is a 400.</summary>
    /// <param name="query">The query string.</param>
    /// <param name="field">The field the error is keyed by.</param>
    [Theory]
    [InlineData("?status=deleted", "status")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task Bad_list_requests_are_400(string query, string field)
    {
        using var response = await SendAsync(AdminTelegramId, HttpMethod.Get, "/admin/links" + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    /// <summary>Pause, resume, change the expiry and revoke: each answers the link, and only real changes are audited.</summary>
    [Fact]
    public async Task Pausing_resuming_extending_and_revoking_are_audited()
    {
        var (playerId, _) = await AddPlayerAsync(published: true);
        var id = await CreateLinkIdAsync(playerId);

        using var paused = await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{id}/pause");
        using var pausedAgain = await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{id}/pause");
        using var resumed = await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{id}/resume");
        using var extended = await SendAsync(AdminTelegramId, HttpMethod.Patch, $"/admin/links/{id}/expiry", new { expiresInDays = 90 });
        using var revoked = await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{id}/revoke");
        using var resumeRevoked = await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{id}/resume");

        using (var body = await ReadJsonAsync(paused))
        {
            body.RootElement.GetProperty("status").GetString().Should().Be("Paused");
            body.RootElement.GetProperty("access").GetString().Should().Be("Paused");
        }

        pausedAgain.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await ReadJsonAsync(extended))
        {
            body.RootElement.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), TimeSpan.FromMinutes(1));
        }

        using (var body = await ReadJsonAsync(revoked))
        {
            body.RootElement.GetProperty("status").GetString().Should().Be("Revoked");
            body.RootElement.GetProperty("revokedAt").ValueKind.Should().Be(JsonValueKind.String);
        }

        resumed.StatusCode.Should().Be(HttpStatusCode.OK);
        resumeRevoked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using (var body = await ReadJsonAsync(resumeRevoked))
        {
            body.RootElement.GetProperty("code").GetString().Should().Be("share_link.revoked");
        }

        (await AuditActionsAsync(id)).Should().Equal(
            "share_link.created", "share_link.paused", "share_link.resumed", "share_link.expiry_changed", "share_link.revoked");
    }

    /// <summary>A single link reads back by id; an unknown id is 404.</summary>
    [Fact]
    public async Task A_link_reads_back_by_id()
    {
        var (playerId, _) = await AddPlayerAsync(published: true);
        var id = await CreateLinkIdAsync(playerId);

        using var found = await SendAsync(AdminTelegramId, HttpMethod.Get, $"/admin/links/{id}");
        using var missing = await SendAsync(AdminTelegramId, HttpMethod.Get, $"/admin/links/{Guid.NewGuid()}");

        found.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(found);
        body.RootElement.GetProperty("id").GetGuid().Should().Be(id);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Anyone may ask whether a code opens: an open link answers its type, its player count and a masked hint of the
    /// recipient's email (for the L-2 gate), and nothing else.
    /// </summary>
    [Fact]
    public async Task An_open_link_resolves_without_signing_in()
    {
        var (playerId, _) = await AddPlayerAsync(published: true);
        var code = await CreateLinkCodeAsync(playerId);

        using var client = _api.CreateClient();
        using var response = await client.GetAsync(new Uri($"/links/{code}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
        body.RootElement.GetProperty("type").GetString().Should().Be("Personal");
        body.RootElement.GetProperty("playerCount").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("recipientHint").GetString().Should().Be("m***@lube.it");
        body.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("code", "type", "playerCount", "recipientHint");
    }

    /// <summary>A paused, revoked, unknown or malformed code all answer the same 404, so codes cannot be probed.</summary>
    [Fact]
    public async Task Links_that_do_not_open_all_look_the_same()
    {
        var (playerId, _) = await AddPlayerAsync(published: true);
        var pausedId = await CreateLinkIdAsync(playerId);
        var revokedId = await CreateLinkIdAsync(playerId);
        using (await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{pausedId}/pause"))
        using (await SendAsync(AdminTelegramId, HttpMethod.Post, $"/admin/links/{revokedId}/revoke"))
        {
        }

        var codes = new[] { await CodeOfAsync(pausedId), await CodeOfAsync(revokedId), "Zz9Zz9", "bad-code" };
        using var client = _api.CreateClient();
        var bodies = new List<string>();
        foreach (var code in codes)
        {
            using var response = await client.GetAsync(new Uri($"/links/{code}", UriKind.Relative), TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var body = await ReadJsonAsync(response);
            body.RootElement.GetProperty("code").GetString().Should().Be("share_link.unavailable");
            bodies.Add(body.RootElement.GetProperty("detail").GetString()!);
        }

        bodies.Distinct().Should().ContainSingle();
    }

    /// <summary>A new-link form with a new recipient.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The form.</returns>
    private static object NewLinkForm(Guid playerId) => new
    {
        recipient = new { name = "Marco Rossi", organisation = "Lube Volley", emails = new[] { "marco@lube.it" } },
        playerIds = new[] { playerId },
        expiresInDays = 30,
    };

    /// <summary>Creates a link for a new recipient and returns the recipient id.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The recipient id.</returns>
    private async Task<Guid> CreateLinkAsync(Guid playerId)
    {
        using var body = await CreateLinkJsonAsync(playerId);
        return body.RootElement.GetProperty("recipient").GetProperty("id").GetGuid();
    }

    /// <summary>Creates a link and returns its id.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The link id.</returns>
    private async Task<Guid> CreateLinkIdAsync(Guid playerId)
    {
        using var body = await CreateLinkJsonAsync(playerId);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Creates a link and returns its code.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The code.</returns>
    private async Task<string> CreateLinkCodeAsync(Guid playerId)
    {
        using var body = await CreateLinkJsonAsync(playerId);
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Creates a link and returns the response body.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The body.</returns>
    private async Task<JsonDocument> CreateLinkJsonAsync(Guid playerId)
    {
        using var response = await SendAsync(AdminTelegramId, HttpMethod.Post, "/admin/links", NewLinkForm(playerId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
    }

    /// <summary>Reads a link's code.</summary>
    /// <param name="id">The link id.</param>
    /// <returns>The code.</returns>
    private async Task<string> CodeOfAsync(Guid id)
    {
        using var response = await SendAsync(AdminTelegramId, HttpMethod.Get, $"/admin/links/{id}");
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Stores a complete player, published or still waiting.</summary>
    /// <param name="published">Whether to publish it.</param>
    /// <returns>The player id and name.</returns>
    private async Task<(Guid Id, string Name)> AddPlayerAsync(bool published)
    {
        var n = Interlocked.Increment(ref _players);
        var fullName = $"Shared Player {(char)('A' + n)}{(char)('a' + n)}";
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        var name = FullName.Create(fullName);
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/shared-player-p{70000 + n}"));
        if (published)
        {
            player.Publish(DateTimeOffset.UtcNow);
        }

        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().Add(player);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return (player.Id, fullName);
    }

    /// <summary>Reads the audit actions recorded for a link, oldest first.</summary>
    /// <param name="linkId">The link.</param>
    /// <returns>The actions.</returns>
    private async Task<List<string>> AuditActionsAsync(Guid linkId)
    {
        using var scope = _api.Services.CreateScope();
        var target = linkId.ToString();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditEntry>()
            .Where(entry => entry.TargetType == "ShareLink" && entry.TargetId == target)
            .OrderBy(entry => entry.At)
            .ThenBy(entry => entry.Id)
            .Select(entry => entry.Action)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Sends a request signed as a Telegram user.</summary>
    /// <param name="telegramId">Who sends it.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, if any.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> SendAsync(long telegramId, HttpMethod method, string path, object? body = null)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, telegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Parses a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON document.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
