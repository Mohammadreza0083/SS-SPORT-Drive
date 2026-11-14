using System.Net;
using System.Text.Json;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// The console dashboard (A-2, canvas <c>AdminDashboard</c>): KPIs for a period (7, 30 or 90 days), views per Tehran day,
/// what needs the admin, the top players and the live activity feed. Views are watches started in the period; unique
/// viewers are distinct emails; completion is the average of each watch's merged coverage.
/// </summary>
public sealed class AdminDashboardTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>The KPIs count live players, views, unique viewers, average completion and the two queues waiting on the admin.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_kpis_sum_up_the_period()
    {
        var player = await _host.AddPlayerAsync();
        var video = await _host.AddVideoAsync(player, seconds: 100);
        var code = await _host.CreateLinkAsync(player);
        var session = await _host.StartViewerSessionAsync(code);
        await _host.WatchAsync(code, player, video, session, (0, 50));
        await _host.WatchAsync(code, player, video, session, (0, 100));
        await _host.AddWaitingPlayerAsync("Ali Hoshmand", [PlayerDetail.Volleybox]);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/dashboard?period=7d");

        var root = body.RootElement;
        root.GetProperty("period").GetString().Should().Be("7d");
        var kpis = root.GetProperty("kpis");
        kpis.GetProperty("livePlayers").GetInt32().Should().Be(1);
        kpis.GetProperty("views").GetInt32().Should().Be(2);
        kpis.GetProperty("previousViews").GetInt32().Should().Be(0);
        kpis.GetProperty("uniqueViewers").GetInt32().Should().Be(1);
        kpis.GetProperty("averageCompletion").GetDouble().Should().BeApproximately(0.75, 0.001);
        kpis.GetProperty("pendingAccessRequests").GetInt32().Should().Be(0);
        kpis.GetProperty("waitingPlayers").GetInt32().Should().Be(1);
    }

    /// <summary>Views per day cover the whole period, oldest first, ending today in Tehran, with empty days as zero.</summary>
    /// <param name="period">The period.</param>
    /// <param name="days">How many days it covers.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    public async Task Views_per_day_cover_the_period(string period, int days)
    {
        var player = await _host.AddPlayerAsync();
        var video = await _host.AddVideoAsync(player);
        var code = await _host.CreateLinkAsync(player);
        await _host.WatchAsync(code, player, video, await _host.StartViewerSessionAsync(code), (0, 10));

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/dashboard?period={period}");

        var series = body.RootElement.GetProperty("viewsByDay").EnumerateArray().ToList();
        series.Should().HaveCount(days);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TehranZone()).DateTime);
        series[^1].GetProperty("date").GetString().Should().Be(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        series[^1].GetProperty("views").GetInt32().Should().Be(1);
        series.Take(days - 1).Should().OnlyContain(day => day.GetProperty("views").GetInt32() == 0);
    }

    /// <summary>Without a period the dashboard shows 30 days; an unknown period is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_period_defaults_to_30_days_and_is_validated()
    {
        using (var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/dashboard"))
        {
            body.RootElement.GetProperty("period").GetString().Should().Be("30d");
        }

        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/dashboard?period=365d", telegramId: BackendTestHost.AdminTelegramId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Needs attention counts failed files, failed and retrying emails, lost Drive folders and the two queues.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Needs_attention_counts_what_waits_on_an_admin()
    {
        var player = await _host.AddPlayerAsync();
        await _host.AddFileAsync(player, "Full_Match_02.mov", ProcessingState.Failed, 1);
        await _host.AddFolderAsync(player, "SS-SPORT Arash Tehrani", accessLost: true);
        await _host.AddWaitingPlayerAsync("Kasra Amini", [PlayerDetail.Photo]);
        await _host.AddWaitingPlayerAsync("Navid Javan", [PlayerDetail.Volleybox]);
        var failed = OutgoingEmail.Queue(EmailKind.General, "forward.kept", Domain.Primitives.EmailAddress.Create("scout@other.com"), null, DateTimeOffset.UtcNow);
        var retrying = OutgoingEmail.Queue(EmailKind.Code, "gate.code", Domain.Primitives.EmailAddress.Create("jan@aurora.nl"), null, DateTimeOffset.UtcNow);
        for (var attempt = 0; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            failed.RecordFailure("SmtpException", DateTimeOffset.UtcNow);
        }

        retrying.RecordFailure("HttpRequestException", DateTimeOffset.UtcNow);
        failed.ClearDomainEvents();
        retrying.ClearDomainEvents();
        await _host.SaveAsync(failed);
        await _host.SaveAsync(retrying);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/dashboard");

        var attention = body.RootElement.GetProperty("needsAttention");
        attention.GetProperty("accessRequests").GetInt32().Should().Be(0);
        attention.GetProperty("waitingPlayers").GetInt32().Should().Be(2);
        attention.GetProperty("failedFiles").GetInt32().Should().Be(1);
        attention.GetProperty("failedEmails").GetInt32().Should().Be(1);
        attention.GetProperty("retryingEmails").GetInt32().Should().Be(1);
        attention.GetProperty("lostFolders").GetInt32().Should().Be(1);
    }

    /// <summary>The top players are ranked by views in the period, at most five, with their average completion.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Top_players_are_ranked_by_views()
    {
        var first = await _host.AddPlayerAsync();
        var second = await _host.AddPlayerAsync();
        var firstVideo = await _host.AddVideoAsync(first, seconds: 100);
        var secondVideo = await _host.AddVideoAsync(second, seconds: 100);
        var code = await _host.CreateLinkAsync(first, second);
        var session = await _host.StartViewerSessionAsync(code);
        await _host.WatchAsync(code, second, secondVideo, session, (0, 100));
        await _host.WatchAsync(code, second, secondVideo, session, (0, 100));
        await _host.WatchAsync(code, first, firstVideo, session, (0, 50));

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/dashboard?period=7d");

        var top = body.RootElement.GetProperty("topPlayers").EnumerateArray().ToList();
        top.Select(row => row.GetProperty("playerId").GetGuid()).Should().Equal(second, first);
        top[0].GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        top[0].GetProperty("views").GetInt32().Should().Be(2);
        top[0].GetProperty("averageCompletion").GetDouble().Should().BeApproximately(1, 0.001);
        top[1].GetProperty("averageCompletion").GetDouble().Should().BeApproximately(0.5, 0.001);
        top.Count.Should().BeLessThanOrEqualTo(5);
    }

    /// <summary>
    /// The live activity feed is a cursor page of the latest watches, newest first, with the viewer, the player, the video
    /// and whether the watch is still live (a heartbeat in the last minute).
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_activity_feed_pages_the_latest_watches()
    {
        var player = await _host.AddPlayerAsync();
        var video = await _host.AddVideoAsync(player, seconds: 100);
        var code = await _host.CreateLinkAsync(player);
        var session = await _host.StartViewerSessionAsync(code);
        var older = await _host.WatchAsync(code, player, video, session, (0, 10));
        var newer = await _host.WatchAsync(code, player, video, session, (0, 60));

        using var first = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/activity?pageSize=1");

        var row = first.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("watchId").GetGuid().Should().Be(newer);
        row.GetProperty("viewerId").GetGuid().Should().NotBeEmpty();
        row.GetProperty("email").GetString().Should().Be(BackendTestHost.RecipientEmail);
        row.GetProperty("organisation").GetString().Should().Be("Lube Volley");
        row.GetProperty("playerId").GetGuid().Should().Be(player);
        row.GetProperty("playerName").GetString().Should().NotBeNullOrEmpty();
        row.GetProperty("videoTitle").GetString().Should().NotBeNullOrEmpty();
        row.GetProperty("completion").GetDouble().Should().BeApproximately(0.6, 0.001);
        row.GetProperty("isLive").GetBoolean().Should().BeTrue();
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();

        using var second = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/activity?pageSize=1&cursor={Uri.EscapeDataString(cursor!)}");
        second.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("watchId").GetGuid().Should().Be(older);
        second.RootElement.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Only admins see the dashboard and the feed: analysts are 403, anonymous callers 401.</summary>
    /// <param name="path">A dashboard path.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("/admin/dashboard")]
    [InlineData("/admin/activity")]
    public async Task Only_admins_see_the_dashboard(string path)
    {
        using var analyst = await _host.SendAsync(HttpMethod.Get, path, telegramId: BackendTestHost.AnalystTelegramId);
        analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = await _host.SendAsync(HttpMethod.Get, path);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The Tehran time zone, as the API resolves it.</summary>
    /// <returns>The zone.</returns>
    private static TimeZoneInfo TehranZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromHours(3.5), "Asia/Tehran", "Asia/Tehran");
        }
    }
}
