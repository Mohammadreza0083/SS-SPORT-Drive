using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Links;

/// <summary>
/// What a signed-in viewer streams and reads behind <c>/r/{code}</c>:
/// <list type="bullet">
/// <item>V-5b: a signed edge URL for one video's HLS stream and one URL per document page, bound to the viewer's
/// session, for 30 minutes;</item>
/// <item>V-8: the player's DataVolley scout files, and one file's match header and this player's stats per skill,
/// parsed on the server (the file itself never leaves the API).</item>
/// </list>
/// The checks come in the same order as the other content reads: a link that does not open is 404, no live session for
/// this link is 401, a player not on the link or an item not on the player is 404.
/// </summary>
public sealed class ViewerMediaEndpointTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    private const string BotToken = "123456:viewer-media-test-token";

    /// <summary>An admin, who creates the links.</summary>
    private const long AdminTelegramId = 803;

    /// <summary>The recipient's email.</summary>
    private const string RecipientEmail = "marco@lube.it";

    /// <summary>The edge signing key (32 bytes, 0x20 to 0x3F), shared with the Worker as <c>HLS_SIGNING_KEY</c>.</summary>
    private const string SigningKey = "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=";

    /// <summary>
    /// A small DataVolley file: Club Aurora (home) beat Volley Nord 3-1 on 12 March 2026. Arash Tehrani is home #7 with
    /// two aces, one serve error, three kills and one blocked attack; an away #7 also attacks, and must not count for him.
    /// </summary>
    private const string ScoutFile = """
        [3DATAVOLLEYSCOUT]
        FILEFORMAT: 2.0
        [3MATCH]
        12/03/2026;20.00;2025/2026;Super League;Regular season;;;;;1;Z;0;
        [3TEAMS]
        AUR;Club Aurora;3;Marco Bianchi;Luca Neri;;
        NRD;Volley Nord;1;Jan Kowalski;Piotr Nowak;;
        [3SET]
        True; 8- 6;16-12;21-18;25-21;28;
        True; 6- 8;12-16;18-21;22-25;30;
        True; 8- 5;16-11;21-17;25-19;26;
        True; 8- 7;16-14;21-20;26-24;31;
        False;;;;;;
        [3PLAYERS-H]
        0;7;1;1;1;1;1;;AUR-007;Tehrani;Arash;;;;;False;
        [3PLAYERS-V]
        1;7;13;1;1;1;1;;NRD-007;Nowicki;Adam;;;;;False;
        [3SCOUT]
        *07SH#;;;;;;;20.01.10;1;1;1;1;1000;;
        *07SH#;;;;;;;20.02.10;1;1;1;1;1010;;
        *07SH=;;;;;;;20.03.10;1;1;1;1;1020;;
        *07AH#;;;;;;;20.04.10;1;1;1;1;1030;;
        *07AH#;;;;;;;20.05.10;1;1;1;1;1040;;
        *07AM#;;;;;;;20.06.10;1;1;1;1;1050;;
        *07AQ/;;;;;;;20.07.10;1;1;1;1;1060;;
        a07AH#;;;;;;;20.08.10;1;1;1;1;1070;;
        *p25:21;;;;;;;20.09.10;1;1;1;1;1080;;
        """;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Where photos and scout originals are kept.</summary>
    private readonly InMemoryObjectStorage _storage = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own name and Volleybox id.</summary>
    private int _players;

    /// <summary>Counts created assets so each gets its own checksum and Drive id.</summary>
    private int _assets;

    /// <summary>Starts PostgreSQL, boots the API with an edge signing key and stores an admin.</summary>
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
                ["Telegram:WebhookSecret"] = "viewer-media-secret",
                ["Telegram:Mode"] = "Webhook",
                ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["Edge:HlsSigningKey"] = SigningKey,
                ["RateLimiting:LinkPermitsPerMinute"] = "1000",
                ["RateLimiting:ViewerPermitsPerMinute"] = "1000",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(_storage));
                services.UseInlineBotNotices();
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
            .Add(User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), DateTimeOffset.UtcNow));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ---------------------------------------------------------------- V-5b: signed video stream

    /// <summary>
    /// A ready, visible video gets the master playlist URL on the edge: <c>/hls/{token}/{folder}/master.m3u8</c>, where
    /// the folder is <c>players/{player}/videos/{video}/v{version}/hls</c> and the token expires 30 minutes from now. The
    /// answer is never cached.
    /// </summary>
    [Fact]
    public async Task A_video_stream_is_a_signed_edge_url_for_its_folder()
    {
        var player = await AddPlayerAsync();
        var video = await AddVideoAsync(player, "Highlights");
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);
        var before = DateTimeOffset.UtcNow;

        using var response = await GetAsync($"/links/{code}/players/{player}/videos/{video}/stream", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var body = await ReadJsonAsync(response);
        var url = body.RootElement.GetProperty("playlistUrl").GetString()!;
        var folder = $"players/{player}/videos/{video}/v1/hls";
        url.Should().StartWith("/hls/").And.EndWith($"/{folder}/master.m3u8");
        var expiresAt = body.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        expiresAt.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// The token in the URL is the edge format (decisions §6): signed with the shared key, for the video's folder, bound
    /// to this viewer's session cookie, with the same expiry the answer reports. A copy in another browser fails at the
    /// edge because <c>s</c> is the hash of this session.
    /// </summary>
    [Fact]
    public async Task The_stream_token_is_bound_to_the_session_and_the_folder()
    {
        var player = await AddPlayerAsync();
        var video = await AddVideoAsync(player, "Highlights");
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/videos/{video}/stream", session);
        using var body = await ReadJsonAsync(response);
        var url = body.RootElement.GetProperty("playlistUrl").GetString()!;
        var token = url.Split('/')[2];

        var payload = VerifyToken(token);
        payload.GetProperty("v").GetInt32().Should().Be(1);
        payload.GetProperty("p").GetString().Should().Be($"players/{player}/videos/{video}/v1/hls");
        payload.GetProperty("s").GetString().Should().Be(SessionHash(session));
        payload.GetProperty("e").GetInt64().Should().Be(body.RootElement.GetProperty("expiresAt").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    /// <summary>Two viewers of the same video get different tokens: each is bound to its own session.</summary>
    [Fact]
    public async Task Each_session_gets_its_own_token()
    {
        var player = await AddPlayerAsync();
        var video = await AddVideoAsync(player, "Highlights");
        var code = await CreateLinkAsync([player]);
        var first = await StartSessionAsync(code);
        var second = await StartSessionAsync(code);

        var a = await StreamUrlAsync(code, player, video, first);
        var b = await StreamUrlAsync(code, player, video, second);

        a.Should().NotBe(b);
    }

    /// <summary>A replaced video (version 2) is streamed from its new folder, so an old token never reaches new files.</summary>
    [Fact]
    public async Task A_replaced_video_streams_from_its_new_version_folder()
    {
        var player = await AddPlayerAsync();
        var video = await AddVideoAsync(player, "Highlights", version: 2);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        var url = await StreamUrlAsync(code, player, video, session);

        url.Should().EndWith($"/players/{player}/videos/{video}/v2/hls/master.m3u8");
    }

    /// <summary>The usual checks, in order: closed link 404, no session 401, another link's session 401.</summary>
    [Fact]
    public async Task A_stream_needs_an_open_link_and_its_own_session()
    {
        var player = await AddPlayerAsync();
        var video = await AddVideoAsync(player, "Highlights");
        var code = await CreateLinkAsync([player]);
        var other = await CreateLinkAsync([player]);
        var otherSession = await StartSessionAsync(other);
        var path = $"/links/{code}/players/{player}/videos/{video}/stream";

        using (var none = await GetAsync(path, null))
        {
            none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ProblemCodeAsync(none)).Should().Be("viewer.verification_required");
        }

        using (var wrong = await GetAsync(path, otherSession))
        {
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var unknownLink = await GetAsync($"/links/Zz9Zz9/players/{player}/videos/{video}/stream", otherSession))
        {
            unknownLink.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ProblemCodeAsync(unknownLink)).Should().Be("share_link.unavailable");
        }
    }

    /// <summary>
    /// Only a ready, visible video of this player on this link is streamed. A hidden or unfinished video, a document, a
    /// video of a player not on the link, and an unknown id are all 404.
    /// </summary>
    [Fact]
    public async Task Only_a_watchable_video_on_the_link_is_streamed()
    {
        var player = await AddPlayerAsync();
        var outsider = await AddPlayerAsync();
        var hidden = await AddVideoAsync(player, "Hidden", hidden: true);
        var processing = await AddVideoAsync(player, "Processing", ready: false);
        var document = await AddDocumentAsync(player, "Stats", 2);
        var outsiderVideo = await AddVideoAsync(outsider, "Theirs");
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        foreach (var path in new[]
        {
            $"/links/{code}/players/{player}/videos/{hidden}/stream",
            $"/links/{code}/players/{player}/videos/{processing}/stream",
            $"/links/{code}/players/{player}/videos/{document}/stream",
            $"/links/{code}/players/{player}/videos/{outsiderVideo}/stream",
            $"/links/{code}/players/{outsider}/videos/{outsiderVideo}/stream",
            $"/links/{code}/players/{player}/videos/{Guid.NewGuid()}/stream",
        })
        {
            using var response = await GetAsync(path, session);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        }
    }

    /// <summary>Without an edge signing key the API cannot sign streams: 503 <c>media.streaming_off</c>, not a 500.</summary>
    [Fact]
    public async Task Without_a_signing_key_streaming_is_off()
    {
        var player = await AddPlayerAsync();
        var video = await AddVideoAsync(player, "Highlights");
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);
        await using var unsigned = _api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Edge:HlsSigningKey"] = string.Empty })));

        using var client = unsigned.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/links/{code}/players/{player}/videos/{video}/stream", UriKind.Relative));
        request.Headers.Add("X-Viewer-Session", session);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemCodeAsync(response)).Should().Be("media.streaming_off");
    }

    // ---------------------------------------------------------------- V-5b: signed document pages

    /// <summary>
    /// A document's pages are one signed edge URL each, in order: <c>/pages/{token}/{folder}/{n}.webp</c> with the folder
    /// <c>players/{player}/documents/{document}/v{version}/pages</c>. One token covers the whole document and is bound to
    /// the session like a stream token.
    /// </summary>
    [Fact]
    public async Task Document_pages_are_signed_edge_urls_in_order()
    {
        var player = await AddPlayerAsync();
        var document = await AddDocumentAsync(player, "Season stats", 3);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/documents/{document}/pages", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var body = await ReadJsonAsync(response);
        var pages = body.RootElement.GetProperty("pages").EnumerateArray().Select(page => page.GetString()!).ToList();
        var folder = $"players/{player}/documents/{document}/v1/pages";
        pages.Should().HaveCount(3);
        for (var index = 0; index < pages.Count; index++)
        {
            pages[index].Should().StartWith("/pages/").And.EndWith($"/{folder}/{index + 1}.webp");
        }

        var token = pages[0].Split('/')[2];
        pages.Should().AllSatisfy(page => page.Split('/')[2].Should().Be(token));
        var payload = VerifyToken(token);
        payload.GetProperty("p").GetString().Should().Be(folder);
        payload.GetProperty("s").GetString().Should().Be(SessionHash(session));
        body.RootElement.GetProperty("expiresAt").GetDateTimeOffset().ToUnixTimeSeconds().Should().Be(payload.GetProperty("e").GetInt64());
    }

    /// <summary>A document whose page count is not known yet has no pages to show.</summary>
    [Fact]
    public async Task A_document_without_a_page_count_has_no_pages()
    {
        var player = await AddPlayerAsync();
        var document = await AddDocumentAsync(player, "Player CV", null);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/documents/{document}/pages", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("pages").GetArrayLength().Should().Be(0);
    }

    /// <summary>Only a ready, visible document of this player is signed; a video id, a hidden document or no session is refused.</summary>
    [Fact]
    public async Task Only_a_readable_document_on_the_link_is_signed()
    {
        var player = await AddPlayerAsync();
        var hidden = await AddDocumentAsync(player, "Hidden", 2, hidden: true);
        var video = await AddVideoAsync(player, "Highlights");
        var visible = await AddDocumentAsync(player, "Stats", 2);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        foreach (var id in new[] { hidden, video, Guid.NewGuid() })
        {
            using var response = await GetAsync($"/links/{code}/players/{player}/documents/{id}/pages", session);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, id.ToString());
        }

        using var none = await GetAsync($"/links/{code}/players/{player}/documents/{visible}/pages", null);
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- V-8: DataVolley scout files

    /// <summary>The player's scout files: ready and visible <c>.dvw</c> files, in page order, paged; no storage key is sent.</summary>
    [Fact]
    public async Task Scout_files_are_listed_in_page_order()
    {
        var player = await AddPlayerAsync();
        var first = await AddScoutAsync(player, "Match vs Volley Nord");
        var second = await AddScoutAsync(player, "Match vs Paykan");
        await AddScoutAsync(player, "Hidden match", hidden: true);
        await AddDocumentAsync(player, "Stats", 1);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/scouts?pageSize=1", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Should().NotContain("assets/").And.NotContain(".dvw");
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var item = body.RootElement.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetGuid().Should().Be(first);
        item.GetProperty("title").GetString().Should().Be("Match vs Volley Nord");

        using var next = await GetAsync($"/links/{code}/players/{player}/scouts?page=2&pageSize=1", session);
        using var nextBody = await ReadJsonAsync(next);
        nextBody.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(second);
    }

    /// <summary>
    /// One scout file's report: the match header (date, teams, sets won, each set's score) and this player's stats per
    /// skill, found by name in the file. The away #7's attack does not count for the home #7.
    /// </summary>
    [Fact]
    public async Task A_scout_report_shows_the_match_and_this_players_stats()
    {
        var player = await AddPlayerAsync();
        var scout = await AddScoutAsync(player, "Match vs Volley Nord");
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/scouts/{scout}", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(scout);
        root.GetProperty("title").GetString().Should().Be("Match vs Volley Nord");

        var match = root.GetProperty("match");
        match.GetProperty("date").GetString().Should().Be("2026-03-12");
        match.GetProperty("homeTeam").GetString().Should().Be("Club Aurora");
        match.GetProperty("awayTeam").GetString().Should().Be("Volley Nord");
        match.GetProperty("homeSets").GetInt32().Should().Be(3);
        match.GetProperty("awaySets").GetInt32().Should().Be(1);
        match.GetProperty("sets").EnumerateArray().Select(set => $"{set.GetProperty("home").GetInt32()}-{set.GetProperty("away").GetInt32()}")
            .Should().Equal("25-21", "22-25", "25-19", "26-24");

        var me = root.GetProperty("player");
        me.GetProperty("number").GetInt32().Should().Be(7);
        me.GetProperty("team").GetString().Should().Be("Club Aurora");

        var skills = root.GetProperty("skills").EnumerateArray().ToDictionary(skill => skill.GetProperty("skill").GetString()!);
        skills.Keys.Should().Equal("Serve", "Reception", "Attack", "Block", "Dig", "Set");
        skills["Serve"].GetProperty("total").GetInt32().Should().Be(3);
        skills["Serve"].GetProperty("perfect").GetInt32().Should().Be(2);
        skills["Serve"].GetProperty("error").GetInt32().Should().Be(1);
        skills["Attack"].GetProperty("total").GetInt32().Should().Be(4);
        skills["Attack"].GetProperty("perfect").GetInt32().Should().Be(3);
        skills["Attack"].GetProperty("poor").GetInt32().Should().Be(1);
        skills["Attack"].GetProperty("successRate").GetInt32().Should().Be(75);
        skills["Attack"].GetProperty("efficiency").GetInt32().Should().Be(50);
        skills["Reception"].GetProperty("total").GetInt32().Should().Be(0);
        skills["Reception"].GetProperty("successRate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>A file the player is not in still shows the match, with no player and no skills.</summary>
    [Fact]
    public async Task A_scout_file_without_the_player_shows_only_the_match()
    {
        var player = await AddPlayerAsync();
        var other = await AddPlayerAsync();
        var scout = await AddScoutAsync(other, "Match vs Volley Nord");
        var code = await CreateLinkAsync([player, other]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{other}/scouts/{scout}", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("match").GetProperty("homeTeam").GetString().Should().Be("Club Aurora");
        body.RootElement.GetProperty("player").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("skills").GetArrayLength().Should().Be(0);
    }

    /// <summary>A file that is not DataVolley answers 422 <c>scout.unreadable</c>; its text is never echoed back.</summary>
    [Fact]
    public async Task An_unreadable_scout_file_is_refused()
    {
        var player = await AddPlayerAsync();
        var scout = await AddScoutAsync(player, "Broken", content: "this is not a scout file");
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/scouts/{scout}", session);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Should().Contain("scout.unreadable").And.NotContain("this is not a scout file");
    }

    /// <summary>A hidden scout file, a scout file of a player not on the link, a document id or no session is refused.</summary>
    [Fact]
    public async Task Only_a_visible_scout_file_on_the_link_is_read()
    {
        var player = await AddPlayerAsync();
        var outsider = await AddPlayerAsync();
        var hidden = await AddScoutAsync(player, "Hidden", hidden: true);
        var theirs = await AddScoutAsync(outsider, "Theirs");
        var document = await AddDocumentAsync(player, "Stats", 1);
        var mine = await AddScoutAsync(player, "Mine");
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        foreach (var path in new[]
        {
            $"/links/{code}/players/{player}/scouts/{hidden}",
            $"/links/{code}/players/{player}/scouts/{theirs}",
            $"/links/{code}/players/{outsider}/scouts/{theirs}",
            $"/links/{code}/players/{player}/scouts/{document}",
        })
        {
            using var response = await GetAsync(path, session);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        }

        using var none = await GetAsync($"/links/{code}/players/{player}/scouts/{mine}", null);
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The player page counts scout files in its stats once they can be read (V-8).</summary>
    [Fact]
    public async Task The_player_stats_count_scout_files()
    {
        var player = await AddPlayerAsync();
        await AddScoutAsync(player, "Match one");
        await AddScoutAsync(player, "Match two");
        await AddScoutAsync(player, "Hidden", hidden: true);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}", session);

        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("stats").GetProperty("matches").GetInt32().Should().Be(2);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Checks a token's signature with the shared key and returns its payload.</summary>
    /// <param name="token">The token from the URL.</param>
    /// <returns>The payload <c>{v, p, s, e}</c>.</returns>
    private static JsonElement VerifyToken(string token)
    {
        var parts = token.Split('.');
        parts.Should().HaveCount(2);
        var expected = HMACSHA256.HashData(Convert.FromBase64String(SigningKey), Encoding.ASCII.GetBytes(parts[0]));
        Base64UrlDecode(parts[1]).Should().Equal(expected, "the token is signed with Edge__HlsSigningKey");
        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[0]));
        return payload.RootElement.Clone();
    }

    /// <summary>The session hash a token carries: base64url of the first 16 bytes of SHA-256 of the cookie value.</summary>
    /// <param name="session">The session token.</param>
    /// <returns>The hash.</returns>
    private static string SessionHash(string session) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(session))[..16]);

    /// <summary>Decodes base64url without padding.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    /// <summary>Encodes base64url without padding.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Reads the stream URL for a video.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="player">The player id.</param>
    /// <param name="video">The video id.</param>
    /// <param name="session">The session token.</param>
    /// <returns>The playlist URL.</returns>
    private async Task<string> StreamUrlAsync(string code, Guid player, Guid video, string session)
    {
        using var response = await GetAsync($"/links/{code}/players/{player}/videos/{video}/stream", session);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("playlistUrl").GetString()!;
    }

    /// <summary>Stores a published player from Iran; the first is Arash Tehrani, the others get their own names and Volleybox ids.</summary>
    /// <returns>The player id.</returns>
    private async Task<Guid> AddPlayerAsync()
    {
        var n = Interlocked.Increment(ref _players);
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        var name = FullName.Create(n == 1 ? "Arash Tehrani" : $"Reza Moradi {(char)('A' + n)}{(char)('a' + n)}");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Opposite, Position.OutsideHitter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/arash-tehrani-p{82000 + n}"));
        player.Publish(DateTimeOffset.UtcNow);
        await SaveAsync<Player>(player);
        return player.Id;
    }

    /// <summary>Stores a video for a player, ready with all renditions unless told otherwise.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="title">The title.</param>
    /// <param name="hidden">Whether an admin hid it.</param>
    /// <param name="ready">Whether processing finished.</param>
    /// <param name="version">The content version: 2 means the Drive file was replaced once.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddVideoAsync(Guid player, string title, bool hidden = false, bool ready = true, int version = 1)
    {
        var n = Interlocked.Increment(ref _assets);
        var asset = NewAsset(player, $"{title}.mp4", title, AssetTags.Highlight, n);
        for (var replaced = 1; replaced < version; replaced++)
        {
            asset.ReplaceContent(AssetFile.Create($"{title}.mp4", (n + (1000 * replaced)).ToString("x32", System.Globalization.CultureInfo.InvariantCulture), 2048 + n));
        }

        if (ready)
        {
            asset.StartCopying();
            asset.AttachOriginal(StorageKey.Create($"assets/{asset.Id}/original.mp4"));
            asset.RecordVideoInfo(1080, 120);
            foreach (var quality in new[] { RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080 })
            {
                asset.MarkRenditionReady(quality, StorageKey.Create($"players/{player}/videos/{asset.Id}/v{version}/hls/{(int)quality}p/index.m3u8"));
            }
        }

        if (hidden)
        {
            asset.Hide();
        }

        await SaveAsync<Asset>(asset);
        return asset.Id;
    }

    /// <summary>Stores a ready document for a player.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="title">The title.</param>
    /// <param name="pages">The page count, or <see langword="null"/> when unknown.</param>
    /// <param name="hidden">Whether an admin hid it.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddDocumentAsync(Guid player, string title, int? pages, bool hidden = false)
    {
        var asset = NewAsset(player, $"{title}.pdf", title, AssetTags.Stats, Interlocked.Increment(ref _assets));
        asset.AttachOriginal(StorageKey.Create($"assets/{asset.Id}/original.pdf"));
        asset.CompleteDocument(pages);
        if (hidden)
        {
            asset.Hide();
        }

        await SaveAsync<Asset>(asset);
        return asset.Id;
    }

    /// <summary>Stores a ready DataVolley scout file for a player, with its original in storage.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="title">The title.</param>
    /// <param name="hidden">Whether an admin hid it.</param>
    /// <param name="content">The file's text; the default is <see cref="ScoutFile"/>.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddScoutAsync(Guid player, string title, bool hidden = false, string content = ScoutFile)
    {
        var asset = NewAsset(player, $"&{title}.dvw", title, AssetTags.Stats, Interlocked.Increment(ref _assets));
        var key = $"assets/{asset.Id}/original.dvw";
        await _storage.PutAsync(key, new MemoryStream(Encoding.UTF8.GetBytes(content)), "text/plain", TestContext.Current.CancellationToken);
        asset.AttachOriginal(StorageKey.Create(key));
        asset.CompleteDocument(null);
        if (hidden)
        {
            asset.Hide();
        }

        await SaveAsync<Asset>(asset);
        return asset.Id;
    }

    /// <summary>Builds a new asset in the next page position.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="title">The title.</param>
    /// <param name="tags">The tags.</param>
    /// <param name="n">The asset's sequence number.</param>
    /// <returns>The asset.</returns>
    private static Asset NewAsset(Guid player, string fileName, string title, AssetTags tags, int n) =>
        Asset.Add(
            player,
            null,
            AssetSource.Drive,
            $"drive-{n}",
            AssetFile.Create(fileName, n.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), 1024 + n),
            title,
            tags,
            n,
            DateTimeOffset.UtcNow);

    /// <summary>Stores an aggregate.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="item">The aggregate.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task SaveAsync<T>(T item)
        where T : AggregateRoot<Guid>
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<T, Guid>>().Add(item);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a link to the players, in that order, for the recipient, and returns its code.</summary>
    /// <param name="players">The player ids.</param>
    /// <returns>The code.</returns>
    private async Task<string> CreateLinkAsync(IReadOnlyList<Guid> players)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/admin/links", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                recipient = new { name = "Marco Rossi", organisation = "Lube Volley", emails = new[] { RecipientEmail } },
                playerIds = players,
                expiresInDays = 30,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, AdminTelegramId, DateTimeOffset.UtcNow));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Starts a viewer session on a link directly, as the gate would after the right code.</summary>
    /// <param name="code">The link code.</param>
    /// <returns>The session token.</returns>
    private async Task<string> StartSessionAsync(string code)
    {
        using var scope = _api.Services.CreateScope();
        var link = await scope.ServiceProvider.GetRequiredService<IRepository<ShareLink, Guid>>()
            .FirstOrDefaultAsync(new ShareLinkByCodeSpec(ShareLinkCode.Create(code)), TestContext.Current.CancellationToken);
        var secrets = scope.ServiceProvider.GetRequiredService<IViewerSecrets>();
        var token = secrets.NewToken();
        scope.ServiceProvider.GetRequiredService<IRepository<ViewerSession, Guid>>()
            .Add(ViewerSession.Start(secrets.HashToken(token), link!.Id, EmailAddress.Create(RecipientEmail), false, null, DateTimeOffset.UtcNow));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return token;
    }

    /// <summary>Reads a viewer endpoint, with the session header when given.</summary>
    /// <param name="path">The path.</param>
    /// <param name="sessionToken">The session token, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> GetAsync(string path, string? sessionToken)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (sessionToken is not null)
        {
            request.Headers.Add("X-Viewer-Session", sessionToken);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Reads the <c>code</c> of a ProblemDetails answer.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The code, or <see langword="null"/>.</returns>
    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = await ReadJsonAsync(response);
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Parses a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON document.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
