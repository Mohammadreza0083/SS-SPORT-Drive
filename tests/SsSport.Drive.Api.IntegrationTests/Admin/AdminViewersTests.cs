using System.Net;
using System.Text.Json;
using SsSport.Drive.Api.IntegrationTests.Support;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// Viewers (A-7 profile and replay heatmap, A-11 access and watch log; canvas <c>AdminViewers</c> and <c>AdminViewer</c>).
/// A viewer is one email that can watch; every recipient email of a link is registered as a viewer when the link is
/// created, so a viewer who never opened it shows as "never watched". The console addresses viewers by id, never by
/// email in the URL. Plays, watch time, unique seconds and completion follow the L-6 metrics.
/// </summary>
public sealed class AdminViewersTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// The list has one row per viewer: organisation, players they can open, plays, watch time, last seen, access until
    /// and whether they have a viewer account. A recipient who never watched is listed too.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_list_has_one_row_per_viewer()
    {
        var (code, player, video, session) = await SetUpAsync();
        await _host.WatchAsync(code, player, video, session, (0, 30));
        await _host.WatchAsync(code, player, video, session, (10, 40));

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/viewers?page=1&pageSize=20");

        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var row = body.RootElement.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("id").GetGuid().Should().NotBeEmpty();
        row.GetProperty("email").GetString().Should().Be(BackendTestHost.RecipientEmail);
        row.GetProperty("organisation").GetString().Should().Be("Lube Volley");
        row.GetProperty("players").EnumerateArray().Select(item => item.GetString()).Should().Equal("Arash Tehrani");
        row.GetProperty("playerCount").GetInt32().Should().Be(1);
        row.GetProperty("plays").GetInt32().Should().Be(2);
        row.GetProperty("watchSeconds").GetDouble().Should().Be(60);
        row.GetProperty("lastSeenAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        row.GetProperty("accessUntil").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromHours(1));
        row.GetProperty("account").GetString().Should().Be("LinkOnly");
    }

    /// <summary>A recipient who never opened the link is listed with no plays and no last seen.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_recipient_who_never_watched_is_listed()
    {
        await _host.CreateLinkAsync(await _host.AddPlayerAsync());

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/viewers?activity=never");

        var row = body.RootElement.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("plays").GetInt32().Should().Be(0);
        row.GetProperty("lastSeenAt").ValueKind.Should().Be(JsonValueKind.Null);

        using var active = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/viewers?activity=active");
        active.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    /// <summary>The search matches part of the email or the organisation; an unknown activity filter is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Search_matches_email_or_organisation()
    {
        await _host.CreateLinkAsync(await _host.AddPlayerAsync());

        (await TotalAsync("/admin/viewers?q=lube")).Should().Be(1);
        (await TotalAsync("/admin/viewers?q=marco")).Should().Be(1);
        (await TotalAsync("/admin/viewers?q=aurora")).Should().Be(0);

        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/viewers?activity=sometimes", telegramId: BackendTestHost.AdminTelegramId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The player filter gives the "Who has access" tab of the player page: viewers of links to that player only.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_player_filter_lists_who_has_access()
    {
        var (_, player, _, _) = await SetUpAsync();
        var other = await _host.AddPlayerAsync();

        (await TotalAsync($"/admin/viewers?playerId={player}")).Should().Be(1);
        (await TotalAsync($"/admin/viewers?playerId={other}")).Should().Be(0);
    }

    /// <summary>The profile sums the viewer's watching and says how many players they can open.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_profile_sums_the_viewers_watching()
    {
        var (code, player, video, session) = await SetUpAsync();
        await _host.WatchAsync(code, player, video, session, (0, 100));
        await _host.WatchAsync(code, player, video, session, (0, 50));
        var viewer = await ViewerIdAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/viewers/{viewer}");

        var root = body.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(viewer);
        root.GetProperty("email").GetString().Should().Be(BackendTestHost.RecipientEmail);
        root.GetProperty("organisation").GetString().Should().Be("Lube Volley");
        root.GetProperty("firstSeenAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        root.GetProperty("plays").GetInt32().Should().Be(2);
        root.GetProperty("watchSeconds").GetDouble().Should().Be(150);
        root.GetProperty("playersAccessible").GetInt32().Should().Be(1);
        root.GetProperty("averageCompletion").GetDouble().Should().BeApproximately(0.75, 0.001);
        root.GetProperty("account").GetString().Should().Be("LinkOnly");
    }

    /// <summary>An unknown viewer id is a 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_viewer_is_not_found()
    {
        using var response = await _host.SendAsync(HttpMethod.Get, $"/admin/viewers/{Guid.NewGuid()}", telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The access table has one row per player and link, with the link's expiry and the plays on it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Access_lists_each_player_and_link()
    {
        var (code, player, video, session) = await SetUpAsync();
        await _host.WatchAsync(code, player, video, session, (0, 20));
        var viewer = await ViewerIdAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/viewers/{viewer}/access?page=1&pageSize=20");

        var row = body.RootElement.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("playerId").GetGuid().Should().Be(player);
        row.GetProperty("playerName").GetString().Should().Be("Arash Tehrani");
        row.GetProperty("linkId").GetGuid().Should().Be(await _host.LinkIdAsync(code));
        row.GetProperty("code").GetString().Should().Be(code);
        row.GetProperty("status").GetString().Should().Be("Active");
        row.GetProperty("grantedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        row.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromHours(1));
        row.GetProperty("plays").GetInt32().Should().Be(1);
        row.GetProperty("watchSeconds").GetDouble().Should().Be(20);
    }

    /// <summary>
    /// The watch log has one row per video: plays and watch time summed, unique seconds and completion from the merged
    /// ranges of every play, and the last time it was watched. It filters by player.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_watch_log_has_one_row_per_video()
    {
        var (code, player, video, session) = await SetUpAsync();
        var second = await _host.AddVideoAsync(player, seconds: 50);
        await _host.WatchAsync(code, player, video, session, (0, 10));
        await _host.WatchAsync(code, player, video, session, (5, 20));
        await _host.WatchAsync(code, player, second, session, (0, 50));
        var viewer = await ViewerIdAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/viewers/{viewer}/videos?playerId={player}&page=1&pageSize=20");

        var rows = body.RootElement.GetProperty("items").EnumerateArray().ToDictionary(row => row.GetProperty("assetId").GetGuid());
        rows.Should().HaveCount(2);
        var first = rows[video];
        first.GetProperty("playerName").GetString().Should().Be("Arash Tehrani");
        first.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        first.GetProperty("plays").GetInt32().Should().Be(2);
        first.GetProperty("watchSeconds").GetDouble().Should().Be(25);
        first.GetProperty("uniqueSeconds").GetDouble().Should().Be(20);
        first.GetProperty("completion").GetDouble().Should().BeApproximately(0.2, 0.001);
        first.GetProperty("lastWatchedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        rows[second].GetProperty("completion").GetDouble().Should().BeApproximately(1, 0.001);
    }

    /// <summary>
    /// The replay heatmap counts, per 5-second bucket, how many of the viewer's plays covered it, over every play of that
    /// video. A long video gets wider buckets so there are never more than 200.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_heatmap_counts_replays_per_bucket()
    {
        var (code, player, video, session) = await SetUpAsync();
        await _host.WatchAsync(code, player, video, session, (0, 10));
        await _host.WatchAsync(code, player, video, session, (5, 20));
        var viewer = await ViewerIdAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/viewers/{viewer}/videos/{video}/heatmap");

        var root = body.RootElement;
        root.GetProperty("assetId").GetGuid().Should().Be(video);
        root.GetProperty("durationSeconds").GetDouble().Should().Be(100);
        root.GetProperty("bucketSeconds").GetDouble().Should().Be(5);
        var buckets = root.GetProperty("buckets").EnumerateArray().Select(item => item.GetInt32()).ToList();
        buckets.Should().HaveCount(20);
        buckets.Take(5).Should().Equal(1, 2, 1, 1, 0);
        buckets.Skip(4).Should().OnlyContain(count => count == 0);
    }

    /// <summary>A 2-hour match is cut into at most 200 buckets.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_long_video_gets_at_most_200_buckets()
    {
        var player = await _host.AddPlayerAsync();
        var match = await _host.AddVideoAsync(player, seconds: 7200);
        var code = await _host.CreateLinkAsync(player);
        await _host.WatchAsync(code, player, match, await _host.StartViewerSessionAsync(code), (0, 40));
        var viewer = await ViewerIdAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/viewers/{viewer}/videos/{match}/heatmap");

        body.RootElement.GetProperty("buckets").GetArrayLength().Should().BeLessThanOrEqualTo(200);
        body.RootElement.GetProperty("bucketSeconds").GetDouble().Should().Be(36);
    }

    /// <summary>The heatmap of a video the viewer never played is a 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_heatmap_of_an_unwatched_video_is_not_found()
    {
        var (_, _, video, _) = await SetUpAsync();
        var viewer = await ViewerIdAsync();

        using var response = await _host.SendAsync(HttpMethod.Get, $"/admin/viewers/{viewer}/videos/{video}/heatmap", telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The sessions list is a cursor page of the viewer's plays, newest first.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Sessions_page_the_viewers_plays()
    {
        var (code, player, video, session) = await SetUpAsync();
        var older = await _host.WatchAsync(code, player, video, session, (0, 10));
        var newer = await _host.WatchAsync(code, player, video, session, (0, 20));
        var viewer = await ViewerIdAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/viewers/{viewer}/sessions?pageSize=5");

        var rows = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        rows.Select(row => row.GetProperty("watchId").GetGuid()).Should().Equal(newer, older);
        rows[0].GetProperty("playerName").GetString().Should().Be("Arash Tehrani");
        rows[0].GetProperty("watchSeconds").GetDouble().Should().Be(20);
        rows[0].TryGetProperty("startedAt", out _).Should().BeTrue();
        body.RootElement.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Viewer identities are for admins only (decisions §3a): analysts get 403 on every viewer endpoint.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_see_viewers()
    {
        var (_, _, video, _) = await SetUpAsync();
        var viewer = await ViewerIdAsync();

        foreach (var path in new[]
        {
            "/admin/viewers",
            $"/admin/viewers/{viewer}",
            $"/admin/viewers/{viewer}/access",
            $"/admin/viewers/{viewer}/videos",
            $"/admin/viewers/{viewer}/videos/{video}/heatmap",
            $"/admin/viewers/{viewer}/sessions",
        })
        {
            using var analyst = await _host.SendAsync(HttpMethod.Get, path, telegramId: BackendTestHost.AnalystTelegramId);
            analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    /// <summary>A player, a 100-second video, a link to the player and a viewer session on it.</summary>
    /// <returns>The link code, player, video and session token.</returns>
    private async Task<(string Code, Guid Player, Guid Video, string Session)> SetUpAsync()
    {
        var player = await _host.AddPlayerAsync();
        var video = await _host.AddVideoAsync(player, seconds: 100);
        var code = await _host.CreateLinkAsync(player);
        return (code, player, video, await _host.StartViewerSessionAsync(code));
    }

    /// <summary>Finds the recipient's viewer id through the list.</summary>
    /// <returns>The id.</returns>
    private async Task<Guid> ViewerIdAsync()
    {
        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/viewers?q={Uri.EscapeDataString("marco@")}");
        return body.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
    }

    /// <summary>Reads the total of a list call.</summary>
    /// <param name="path">The path with its query.</param>
    /// <returns>The total count.</returns>
    private async Task<int> TotalAsync(string path)
    {
        using var body = await _host.AdminJsonAsync(HttpMethod.Get, path);
        return body.RootElement.GetProperty("totalCount").GetInt32();
    }
}
