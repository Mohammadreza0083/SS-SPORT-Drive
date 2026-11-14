using System.Net;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// The console's players list (A-3, canvas <c>AdminPlayers</c>): every player, paged in SQL, searchable by name or
/// Volleybox id, filtered by position and by console status, and exported as CSV with the same filters. Console status:
/// <c>Waiting</c> = not published; <c>Attention</c> = published with a failed file or a lost Drive folder;
/// <c>Processing</c> = published with files still in progress; <c>Live</c> = every other published player.
/// </summary>
public sealed class AdminPlayersTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Each row carries what the table shows: positions as codes, files, viewers, last viewed, analyst and status.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Rows_carry_the_console_columns()
    {
        var live = await _host.AddPlayerAsync();
        var video = await _host.AddVideoAsync(live, seconds: 100);
        await _host.AddFileAsync(live, "Stats_Oct_2026.pdf", ProcessingState.Ready, 2);
        var code = await _host.CreateLinkAsync(live);
        await _host.WatchAsync(code, live, video, await _host.StartViewerSessionAsync(code), (0, 10));
        var waiting = await _host.AddWaitingPlayerAsync("Ali Hoshmand", [PlayerDetail.Volleybox], analystId: _host.AnalystId);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/players?page=1&pageSize=20");

        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var rows = body.RootElement.GetProperty("items").EnumerateArray().ToDictionary(row => row.GetProperty("id").GetGuid());
        var liveRow = rows[live];
        liveRow.GetProperty("name").GetString().Should().Be("Arash Tehrani");
        liveRow.GetProperty("positions").EnumerateArray().Select(item => item.GetString()).Should().Equal("OPP", "OH");
        liveRow.GetProperty("nationalityName").GetString().Should().Be("Iran");
        liveRow.GetProperty("videoCount").GetInt32().Should().Be(1);
        liveRow.GetProperty("documentCount").GetInt32().Should().Be(1);
        liveRow.GetProperty("viewers").GetInt32().Should().Be(1);
        liveRow.GetProperty("lastViewedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        liveRow.GetProperty("status").GetString().Should().Be("Live");
        liveRow.GetProperty("hasPhoto").GetBoolean().Should().BeTrue();
        var waitingRow = rows[waiting];
        waitingRow.GetProperty("status").GetString().Should().Be("Waiting");
        waitingRow.GetProperty("analystName").GetString().Should().Be("Analyst");
        waitingRow.GetProperty("viewers").GetInt32().Should().Be(0);
        waitingRow.GetProperty("lastViewedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    /// <summary>The search matches part of the name or the exact Volleybox id.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Search_matches_name_or_volleybox_id()
    {
        var arash = await _host.AddPlayerAsync();
        await _host.AddWaitingPlayerAsync("Kasra Amini");

        (await IdsAsync("/admin/players?q=tehr")).Should().Equal(arash);
        (await IdsAsync("/admin/players?q=81001")).Should().Equal(arash);
        (await IdsAsync("/admin/players?q=nobody")).Should().BeEmpty();
    }

    /// <summary>The position filter takes a canvas code; an unknown code is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Position_filter_takes_a_code()
    {
        await _host.AddPlayerAsync();
        var blocker = await _host.AddWaitingPlayerAsync("Kasra Amini");

        (await IdsAsync("/admin/players?position=MB")).Should().Equal(blocker);

        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/players?position=XX", telegramId: BackendTestHost.AdminTelegramId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The status filter picks each console status; an unknown status is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Status_filter_picks_each_console_status()
    {
        var live = await _host.AddPlayerAsync();
        var processing = await _host.AddPlayerAsync();
        await _host.AddFileAsync(processing, "Reza_vs_Shahrdari.mov", ProcessingState.Processing, 1);
        var attention = await _host.AddPlayerAsync();
        await _host.AddFileAsync(attention, "Full_Match_02.mov", ProcessingState.Failed, 1);
        var waiting = await _host.AddWaitingPlayerAsync("Navid Javan", [PlayerDetail.Photo]);

        (await IdsAsync("/admin/players?status=live")).Should().Equal(live);
        (await IdsAsync("/admin/players?status=processing")).Should().Equal(processing);
        (await IdsAsync("/admin/players?status=attention")).Should().Equal(attention);
        (await IdsAsync("/admin/players?status=waiting")).Should().Equal(waiting);

        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/players?status=hidden", telegramId: BackendTestHost.AdminTelegramId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The list is paged in SQL and capped at 100 rows a page like every list (decisions §2b).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_list_is_paged()
    {
        await _host.AddPlayerAsync();
        await _host.AddPlayerAsync();
        await _host.AddPlayerAsync();

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/players?page=2&pageSize=2");
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(3);
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);

        using var tooBig = await _host.SendAsync(HttpMethod.Get, "/admin/players?pageSize=101", telegramId: BackendTestHost.AdminTelegramId);
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The CSV export has the same filters as the list, a header row, one line per player, an attachment name with the
    /// date and no caching. Cells that a spreadsheet would run as a formula start with an apostrophe (CSV injection).
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Csv_export_follows_the_filters_and_is_safe()
    {
        var formulaAnalyst = await _host.AddMemberAsync("=HYPERLINK(evil)", Role.Analyst);
        await _host.AddWaitingPlayerAsync("Navid Javan", [PlayerDetail.Photo], analystId: formulaAnalyst);
        await _host.AddPlayerAsync();

        using var response = await _host.SendAsync(HttpMethod.Get, "/admin/players.csv?status=waiting", telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().MatchRegex(@"^players-\d{4}-\d{2}-\d{2}\.csv$");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var lines = (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Name,Positions,Nationality,Videos,Documents,Viewers,Last viewed,Analyst,Status,Volleybox");
        lines.Should().HaveCount(2);
        lines[1].Should().StartWith("Navid Javan,MB,Iran,0,0,0,,");
        lines[1].Should().Contain(",'=HYPERLINK(evil),Waiting,");
    }

    /// <summary>Only admins see the list and the export.</summary>
    /// <param name="path">A list path.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("/admin/players")]
    [InlineData("/admin/players.csv")]
    public async Task Only_admins_see_the_players(string path)
    {
        using var analyst = await _host.SendAsync(HttpMethod.Get, path, telegramId: BackendTestHost.AnalystTelegramId);
        analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = await _host.SendAsync(HttpMethod.Get, path);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Reads the ids of the first page of a list call.</summary>
    /// <param name="path">The path with its query.</param>
    /// <returns>The ids, in list order.</returns>
    private async Task<List<Guid>> IdsAsync(string path)
    {
        using var body = await _host.AdminJsonAsync(HttpMethod.Get, path);
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())];
    }
}
