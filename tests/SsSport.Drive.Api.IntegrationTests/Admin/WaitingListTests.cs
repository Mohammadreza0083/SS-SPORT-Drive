using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// The waiting list (A-5, canvas <c>Waiting</c>, decisions §5): players with files but missing details, oldest first,
/// each with what is missing in the drawer's order. An admin completes the details in the drawer; Publish is refused by
/// the API until every detail is set (the button is disabled in the console too). Publishing starts ingest, is audited
/// and tells the responsible analyst in the bot.
/// </summary>
public sealed class WaitingListTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync(new Dictionary<string, string?>
    {
        // No quiet hours, so the analyst's notice goes out at once whatever the time of the run.
        ["Notifications:QuietHoursStart"] = "00:00",
        ["Notifications:QuietHoursEnd"] = "00:00",
    });

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Rows list the folder, the files, what is missing and since when, oldest first; published players never show.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Rows_show_what_is_missing_oldest_first()
    {
        await _host.AddPlayerAsync();
        var newer = await _host.AddWaitingPlayerAsync("Kasra Amini", [PlayerDetail.Nationality, PlayerDetail.Photo], createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        var older = await _host.AddWaitingPlayerAsync("Ali Hoshmand", [PlayerDetail.Volleybox, PlayerDetail.Positions], createdAt: DateTimeOffset.UtcNow.AddDays(-5));
        await _host.AddFolderAsync(older, "SS-SPORT Ali Hoshmand");
        await _host.AddFileAsync(older, "Highlights.mov", ProcessingState.Queued, 1);
        await _host.AddFileAsync(older, "Stats.pdf", ProcessingState.Queued, 2);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/waiting?page=1&pageSize=20");

        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var rows = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        rows.Select(row => row.GetProperty("id").GetGuid()).Should().Equal(older, newer);
        rows[0].GetProperty("name").GetString().Should().Be("Ali Hoshmand");
        rows[0].GetProperty("folderName").GetString().Should().Be("SS-SPORT Ali Hoshmand");
        rows[0].GetProperty("videoCount").GetInt32().Should().Be(1);
        rows[0].GetProperty("documentCount").GetInt32().Should().Be(1);
        rows[0].GetProperty("missing").EnumerateArray().Select(item => item.GetString()).Should().Equal("Positions", "Volleybox");
        rows[0].GetProperty("waitingSince").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(-5), TimeSpan.FromMinutes(1));
        rows[1].GetProperty("missing").EnumerateArray().Select(item => item.GetString()).Should().Equal("Nationality", "Photo");
        rows[1].GetProperty("folderName").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    /// <summary>The missing filter keeps the players that lack that detail; an unknown detail is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_missing_filter_narrows_the_list()
    {
        var noVolleybox = await _host.AddWaitingPlayerAsync("Ali Hoshmand", [PlayerDetail.Volleybox]);
        var noPhoto = await _host.AddWaitingPlayerAsync("Kasra Amini", [PlayerDetail.Photo]);

        using (var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/waiting?missing=volleybox"))
        {
            body.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Equal(noVolleybox);
        }

        using (var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/waiting?missing=photo"))
        {
            body.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Equal(noPhoto);
        }

        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/waiting?missing=card", telegramId: BackendTestHost.AdminTelegramId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The summary counts the waiting players, those without Volleybox or photo, and those published today.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_summary_counts_the_gaps()
    {
        await _host.AddWaitingPlayerAsync("Ali Hoshmand", [PlayerDetail.Volleybox]);
        await _host.AddWaitingPlayerAsync("Kasra Amini", [PlayerDetail.Volleybox, PlayerDetail.Photo]);
        await _host.AddPlayerAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/waiting/summary");

        body.RootElement.GetProperty("waiting").GetInt32().Should().Be(2);
        body.RootElement.GetProperty("noVolleybox").GetInt32().Should().Be(2);
        body.RootElement.GetProperty("noPhoto").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("publishedToday").GetInt32().Should().Be(1);
    }

    /// <summary>The drawer saves details through the console; the answer says what is still missing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_drawer_saves_details()
    {
        var player = await _host.AddWaitingPlayerAsync("Ali Hoshmand", [PlayerDetail.Volleybox, PlayerDetail.Positions]);

        using var body = await _host.AdminJsonAsync(HttpMethod.Patch, $"/admin/players/{player}", new
        {
            positions = new[] { "OH", "OPP" },
            volleyboxUrl = "https://volleybox.net/ali-hoshmand-p60214",
        });

        body.RootElement.GetProperty("positions").EnumerateArray().Select(item => item.GetString()).Should().Equal("OH", "OPP");
        body.RootElement.GetProperty("volleyboxId").GetInt32().Should().Be(60214);
        body.RootElement.GetProperty("missing").GetArrayLength().Should().Be(0);
        body.RootElement.GetProperty("status").GetString().Should().Be("Waiting");
    }

    /// <summary>Publishing an incomplete player is a 422 <c>player.incomplete</c> and changes nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Publish_is_refused_until_every_detail_is_set()
    {
        var player = await _host.AddWaitingPlayerAsync("Ali Hoshmand", [PlayerDetail.Photo]);

        using var response = await _host.SendAsync(HttpMethod.Post, $"/admin/players/{player}/publish", new { }, telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BackendTestHost.ProblemCodeAsync(response)).Should().Be("player.incomplete");
        var stored = await _host.QueryAsync(db => Task.FromResult(db.Set<Player>().Single(item => item.Id == player)));
        stored.IsPublished.Should().BeFalse();
    }

    /// <summary>
    /// Publishing a complete player answers the published page, leaves the waiting list, is audited as
    /// <c>player.published</c> and tells the responsible analyst in the bot.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Publishing_a_complete_player_goes_live()
    {
        var player = await _host.AddWaitingPlayerAsync("Ali Hoshmand", analystId: _host.AnalystId);
        var messenger = (RecordingMessenger)_host.Api.Services.GetRequiredService<ITelegramMessenger>();

        using (var body = await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/players/{player}/publish", new { }))
        {
            body.RootElement.GetProperty("status").GetString().Should().NotBe("Waiting");
            body.RootElement.GetProperty("publishedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        }

        using (var list = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/waiting"))
        {
            list.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        }

        var entry = await _host.QueryAsync(db => Task.FromResult(db.Set<AuditEntry>().Single(item => item.Action == "player.published")));
        entry.ActorUserId.Should().Be(_host.AdminId);
        entry.TargetType.Should().Be("Player");
        entry.TargetId.Should().Be(player.ToString());
        await messenger.WaitForAsync(call => call.ChatId == BackendTestHost.AnalystTelegramId && call.Text != null && call.Text.Contains("Ali Hoshmand", StringComparison.Ordinal), TimeSpan.FromSeconds(10));
    }

    /// <summary>Publishing twice is harmless: the second call answers the same published player.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Publishing_twice_is_harmless()
    {
        var player = await _host.AddWaitingPlayerAsync("Ali Hoshmand");

        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/players/{player}/publish", new { }))
        {
        }

        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/players/{player}/publish", new { }))
        {
        }

        var entries = await _host.QueryAsync(db => Task.FromResult(db.Set<AuditEntry>().Count(item => item.Action == "player.published")));
        entries.Should().Be(1);
    }

    /// <summary>Only admins complete and publish waiting players (decisions §5).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_publish()
    {
        var player = await _host.AddWaitingPlayerAsync("Ali Hoshmand", analystId: _host.AnalystId);

        using var list = await _host.SendAsync(HttpMethod.Get, "/admin/waiting", telegramId: BackendTestHost.AnalystTelegramId);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var publish = await _host.SendAsync(HttpMethod.Post, $"/admin/players/{player}/publish", new { }, telegramId: BackendTestHost.AnalystTelegramId);
        publish.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
