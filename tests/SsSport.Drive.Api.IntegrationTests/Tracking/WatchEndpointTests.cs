using System.Net;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Api.IntegrationTests.Tracking;

/// <summary>
/// Watch tracking behind a personal link (L-5) and its metrics for admins (L-6). The viewer's player starts a watch on
/// the first play, sends a heartbeat with the range played every 10 seconds, and reports play / pause / seek / ended.
/// Every call needs the viewer's live session for this link (<c>X-Viewer-Session</c>), like the content reads. Admins
/// read, per viewer email and video, plays, watch time, unique seconds and completion, paged in SQL.
/// </summary>
public sealed class WatchEndpointTests : IAsyncLifetime
{
    /// <summary>The viewer session header.</summary>
    private const string SessionHeader = "X-Viewer-Session";

    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Starting a watch counts one play, for the session's email and the video's length.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Starting_a_watch_counts_a_play()
    {
        var (code, player, video, session) = await SetUpAsync();

        using var response = await StartWatchAsync(code, player, video, session);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        var watchId = body.RootElement.GetProperty("watchId").GetGuid();
        body.RootElement.GetProperty("heartbeatSeconds").GetInt32().Should().Be(10);
        var watch = await _host.QueryAsync(db => db.Set<WatchSession>().AsNoTracking().SingleAsync(item => item.Id == watchId, TestContext.Current.CancellationToken));
        watch.Plays.Should().Be(1);
        watch.Email.Value.Should().Be(BackendTestHost.RecipientEmail);
        watch.DurationSeconds.Should().Be(100);
        watch.AssetId.Should().Be(video);
        watch.ShareLinkId.Should().Be(await _host.LinkIdAsync(code));
    }

    /// <summary>Tracking needs a live session for this link; a closed link stays the gate's 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tracking_needs_a_session_for_this_link()
    {
        var (code, player, video, _) = await SetUpAsync();
        var other = await _host.CreateLinkAsync(player);
        var otherSession = await _host.StartViewerSessionAsync(other);

        using var none = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/players/{player}/videos/{video}/watch", new { });
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BackendTestHost.ProblemCodeAsync(none)).Should().Be("viewer.verification_required");

        using var wrong = await StartWatchAsync(code, player, video, otherSession);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var unknown = await StartWatchAsync("Zz9Yy8", player, video, otherSession);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BackendTestHost.ProblemCodeAsync(unknown)).Should().Be("share_link.unavailable");
    }

    /// <summary>Only a ready, visible video of a player on the link can be watched.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_the_links_videos_can_be_watched()
    {
        var (code, player, _, session) = await SetUpAsync();
        var hidden = await _host.AddVideoAsync(player, hidden: true);
        var processing = await _host.AddVideoAsync(player, ready: false);
        var stranger = await _host.AddPlayerAsync();
        var strangerVideo = await _host.AddVideoAsync(stranger);

        foreach (var (playerId, videoId) in new[] { (player, hidden), (player, processing), (stranger, strangerVideo), (player, strangerVideo) })
        {
            using var response = await StartWatchAsync(code, playerId, videoId, session);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    /// <summary>Heartbeats add the played ranges; contiguous ones join.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Heartbeats_add_ranges()
    {
        var (code, player, video, session) = await SetUpAsync();
        var watchId = await WatchIdAsync(code, player, video, session);

        foreach (var (from, to) in new[] { (0.0, 10.0), (10.0, 20.0), (5.0, 15.0) })
        {
            using var beat = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/watch/{watchId}/heartbeat", new { from, to }, headers: Header(session));
            beat.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var watch = await _host.QueryAsync(db => db.Set<WatchSession>().AsNoTracking().SingleAsync(item => item.Id == watchId, TestContext.Current.CancellationToken));
        watch.Ranges.Should().Equal(new WatchRange(0, 20), new WatchRange(5, 15));
        watch.UniqueSeconds.Should().Be(20);
        watch.WatchSeconds.Should().Be(30);
    }

    /// <summary>An impossible heartbeat is a 422 and changes nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_impossible_heartbeat_is_refused()
    {
        var (code, player, video, session) = await SetUpAsync();
        var watchId = await WatchIdAsync(code, player, video, session);

        using var response = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/watch/{watchId}/heartbeat", new { from = 0, to = 60 }, headers: Header(session));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BackendTestHost.ProblemCodeAsync(response)).Should().Be("tracking.invalid_range");
    }

    /// <summary>A watch belongs to the session that started it; another session's call is a 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_watch_belongs_to_its_session()
    {
        var (code, player, video, session) = await SetUpAsync();
        var watchId = await WatchIdAsync(code, player, video, session);
        var otherSession = await _host.StartViewerSessionAsync(code);

        using var beat = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/watch/{watchId}/heartbeat", new { from = 0, to = 10 }, headers: Header(otherSession));
        beat.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BackendTestHost.ProblemCodeAsync(beat)).Should().Be("tracking.unknown_watch");

        using var unknown = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/watch/{Guid.CreateVersion7()}/heartbeat", new { from = 0, to = 10 }, headers: Header(session));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Player events are logged; playing again after the end is a second play.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Events_are_logged_and_replays_count()
    {
        var (code, player, video, session) = await SetUpAsync();
        var watchId = await WatchIdAsync(code, player, video, session);

        foreach (var (type, position) in new[] { ("Pause", 30.0), ("Play", 30.0), ("Ended", 100.0), ("Play", 0.0) })
        {
            using var response = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/watch/{watchId}/events", new { type, position }, headers: Header(session));
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var watch = await _host.QueryAsync(db => db.Set<WatchSession>().AsNoTracking().SingleAsync(item => item.Id == watchId, TestContext.Current.CancellationToken));
        watch.Plays.Should().Be(2);
        var events = await _host.QueryAsync(db => db.Set<TrackingEvent>().AsNoTracking().Where(item => item.WatchSessionId == watchId).OrderBy(item => item.Id).ToListAsync(TestContext.Current.CancellationToken));
        events.Select(item => item.Type).Should().Equal(PlaybackEventType.Pause, PlaybackEventType.Play, PlaybackEventType.Ended, PlaybackEventType.Play);
        events[2].PositionSeconds.Should().Be(100);
    }

    /// <summary>An unknown event type is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_event_is_refused()
    {
        var (code, player, video, session) = await SetUpAsync();
        var watchId = await WatchIdAsync(code, player, video, session);

        using var response = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/watch/{watchId}/events", new { type = "Download", position = 1 }, headers: Header(session));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Admins read the link's viewing per viewer and video: plays and watch time summed over watches, unique seconds and
    /// completion from the merged ranges of all of them.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Admins_read_the_viewing_metrics()
    {
        var (code, player, video, session) = await SetUpAsync();
        var first = await WatchIdAsync(code, player, video, session);
        var second = await WatchIdAsync(code, player, video, session);
        await BeatAsync(code, first, session, 0, 10);
        await BeatAsync(code, second, session, 5, 20);

        using var response = await _host.SendAsync(HttpMethod.Get, $"/admin/links/{await _host.LinkIdAsync(code)}/viewing?page=1&pageSize=20", telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var row = body.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("email").GetString().Should().Be(BackendTestHost.RecipientEmail);
        row.GetProperty("playerId").GetGuid().Should().Be(player);
        row.GetProperty("assetId").GetGuid().Should().Be(video);
        row.GetProperty("plays").GetInt32().Should().Be(2);
        row.GetProperty("watchSeconds").GetDouble().Should().Be(25);
        row.GetProperty("uniqueSeconds").GetDouble().Should().Be(20);
        row.GetProperty("completion").GetDouble().Should().Be(0.2);
        row.GetProperty("isComplete").GetBoolean().Should().BeFalse();
        row.TryGetProperty("lastSeenAt", out _).Should().BeTrue();
    }

    /// <summary>Only admins read viewing metrics; analysts never see viewer identities (decisions §3a, T-7a).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_read_viewing()
    {
        var (code, _, _, _) = await SetUpAsync();
        var linkId = await _host.LinkIdAsync(code);

        using var analyst = await _host.SendAsync(HttpMethod.Get, $"/admin/links/{linkId}/viewing", telegramId: BackendTestHost.AnalystTelegramId);
        analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = await _host.SendAsync(HttpMethod.Get, $"/admin/links/{linkId}/viewing");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    /// <summary>Starts a watch.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="player">The player id.</param>
    /// <param name="video">The video id.</param>
    /// <param name="session">The session token.</param>
    /// <returns>The response.</returns>
    private Task<HttpResponseMessage> StartWatchAsync(string code, Guid player, Guid video, string session) =>
        _host.SendAsync(HttpMethod.Post, $"/links/{code}/players/{player}/videos/{video}/watch", new { }, headers: Header(session));

    /// <summary>Starts a watch and returns its id.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="player">The player id.</param>
    /// <param name="video">The video id.</param>
    /// <param name="session">The session token.</param>
    /// <returns>The watch id.</returns>
    private async Task<Guid> WatchIdAsync(string code, Guid player, Guid video, string session)
    {
        using var response = await StartWatchAsync(code, player, video, session);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        return body.RootElement.GetProperty("watchId").GetGuid();
    }

    /// <summary>Sends one heartbeat.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="watchId">The watch.</param>
    /// <param name="session">The session token.</param>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <returns>A task that completes when it is accepted.</returns>
    private async Task BeatAsync(string code, Guid watchId, string session, double from, double to)
    {
        using var response = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/watch/{watchId}/heartbeat", new { from, to }, headers: Header(session));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>The session header.</summary>
    /// <param name="session">The token.</param>
    /// <returns>The headers.</returns>
    private static Dictionary<string, string> Header(string session) => new() { [SessionHeader] = session };
}
