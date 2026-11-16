using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// Epic 8 in the console: an admin starts a scan of the Shared Drive (M-1), reads the report, imports folders as waiting
/// archive players (M-2), and sends the waiting archive players out and back as CSV (M-3). Admins only.
/// </summary>
public sealed class AdminArchiveTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Before any scan the page is empty; starting one answers 202 with the running scan, and a second start is a 409.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_scan_starts_once_at_a_time()
    {
        using (var empty = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/archive"))
        {
            empty.RootElement.GetProperty("latestScan").ValueKind.Should().Be(JsonValueKind.Null);
            empty.RootElement.GetProperty("report").ValueKind.Should().Be(JsonValueKind.Null);
        }

        using (var started = await _host.AdminJsonAsync(HttpMethod.Post, "/admin/archive/scans", new { }, HttpStatusCode.Accepted))
        {
            started.RootElement.GetProperty("status").GetString().Should().Be("Running");
        }

        using var again = await _host.SendAsync(HttpMethod.Post, "/admin/archive/scans", new { }, telegramId: BackendTestHost.AdminTelegramId);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BackendTestHost.ProblemCodeAsync(again)).Should().Be("archive.scan_running");

        using var page = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/archive");
        page.RootElement.GetProperty("latestScan").GetProperty("status").GetString().Should().Be("Running");
    }

    /// <summary>The report lists the newest completed scan's folders with counts and suggestions; the state filter narrows it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_report_lists_the_folders()
    {
        var (scan, ivan, sara) = await SeedReportAsync();

        using (var summary = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/archive"))
        {
            summary.RootElement.GetProperty("report").GetProperty("id").GetGuid().Should().Be(scan);
            summary.RootElement.GetProperty("report").GetProperty("folders").GetInt32().Should().Be(2);
            summary.RootElement.GetProperty("new").GetInt32().Should().Be(1);
            summary.RootElement.GetProperty("linked").GetInt32().Should().Be(1);
        }

        using (var all = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/archive/folders?page=1&pageSize=20"))
        {
            var rows = all.RootElement.GetProperty("items").EnumerateArray().ToList();
            rows.Select(row => row.GetProperty("id").GetGuid()).Should().Equal(ivan, sara);
            rows[0].GetProperty("suggestedName").GetString().Should().Be("Ivan Valeev");
            rows[0].GetProperty("suggestedPositions").EnumerateArray().Select(item => item.GetString()).Should().Equal("OH", "OPP");
            rows[0].GetProperty("videos").GetInt32().Should().Be(8);
            rows[0].GetProperty("missingFromBackup").GetInt32().Should().Be(1);
            rows[0].GetProperty("state").GetString().Should().Be("New");
        }

        using (var fresh = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/archive/folders?state=new"))
        {
            fresh.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Equal(ivan);
        }

        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/archive/folders?state=gone", telegramId: BackendTestHost.AdminTelegramId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Importing makes a waiting archive player with the suggestions and the folder linked; a folder already in the app is
    /// refused with its code. The new player is on the waiting list, never published.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Importing_puts_players_on_the_waiting_list()
    {
        var (_, ivan, sara) = await SeedReportAsync();

        using var result = await _host.AdminJsonAsync(HttpMethod.Post, "/admin/archive/import", new { folderIds = new[] { ivan, sara } });

        var imported = result.RootElement.GetProperty("imported").EnumerateArray().Should().ContainSingle().Subject;
        imported.GetProperty("folderId").GetGuid().Should().Be(ivan);
        var player = imported.GetProperty("playerId").GetGuid();
        result.RootElement.GetProperty("refused").EnumerateArray().Single().GetProperty("code").GetString().Should().Be("archive.folder_not_new");

        var saved = await _host.QueryAsync(context => context.Set<Player>().AsNoTracking().SingleAsync(p => p.Id == player, TestContext.Current.CancellationToken));
        saved.Origin.Should().Be(PlayerOrigin.Archive);
        saved.Status.Should().Be(PlayerStatus.Waiting);
        saved.Name!.Value.Should().Be("Ivan Valeev");
        (await _host.QueryAsync(context => context.Set<DriveFolder>().AnyAsync(f => f.PlayerId == player && f.DriveFolderId == "drive-ivan", TestContext.Current.CancellationToken)))
            .Should().BeTrue();

        using var waiting = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/waiting");
        waiting.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Contain(player);

        using var empty = await _host.SendAsync(HttpMethod.Post, "/admin/archive/import", new { folderIds = Array.Empty<Guid>() }, telegramId: BackendTestHost.AdminTelegramId);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The CSV round trip: the export is an attachment with one row per waiting archive player; the edited file comes back
    /// as JSON text, good rows are saved and bad rows reported by row number.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_csv_goes_out_and_comes_back()
    {
        var (_, ivan, _) = await SeedReportAsync();
        using (await _host.AdminJsonAsync(HttpMethod.Post, "/admin/archive/import", new { folderIds = new[] { ivan } }))
        {
        }

        using var export = await _host.SendAsync(HttpMethod.Get, "/admin/archive/waiting.csv", telegramId: BackendTestHost.AdminTelegramId);
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        export.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().StartWith("archive-waiting-").And.EndWith(".csv");
        var csv = await export.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Id,Folder,Category,Name,Nationality,Positions,Volleybox");
        lines.Should().HaveCount(2);
        var id = lines[1].Split(',')[0];
        lines[1].Should().Contain("Ivan Valeev").And.Contain("OH/OPP");

        var edited = $"{lines[0]}\r\n{id},x,Men,Ivan Valeev,RU,OH/OPP,https://volleybox.net/ivan-valeev-p70001\r\n{Guid.NewGuid()},x,,,,,\r\n";
        using var result = await _host.AdminJsonAsync(HttpMethod.Post, "/admin/archive/waiting.csv", new { csv = edited });

        result.RootElement.GetProperty("updated").GetInt32().Should().Be(1);
        var error = result.RootElement.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("row").GetInt32().Should().Be(3);
        error.GetProperty("code").GetString().Should().Be("archive.csv_player_not_waiting");
        var saved = await _host.QueryAsync(context => context.Set<Player>().AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(id), TestContext.Current.CancellationToken));
        saved.Nationality!.Code.Should().Be("RU");
        saved.Volleybox!.PlayerId.Should().Be(70001);
        saved.Status.Should().Be(PlayerStatus.Waiting);
    }

    /// <summary>Seeds a completed scan with a new folder and one already linked to a player.</summary>
    /// <returns>The scan and the two rows.</returns>
    private async Task<(Guid Scan, Guid Ivan, Guid Sara)> SeedReportAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var scan = ArchiveScan.Start(Guid.CreateVersion7(now), now.AddMinutes(-5));
        scan.Complete(new ArchiveScanTotals(2, 20, 5_000_000_000, 18, 2, 1), now.AddMinutes(-1));
        await _host.SaveAsync(scan);
        var ivan = ArchiveFolder.Record(scan.Id, "drive-ivan", "Men Volleyball/SS-SPORT Ivan Valeev／OH-OPP", "SS-SPORT Ivan Valeev／OH-OPP", PlayerCategory.Men, new ArchiveFolderCounts(8, 2, 0, 4_000_000_000, 9, 1, 1, false), null, now);
        var linked = await _host.AddPlayerAsync();
        var sara = ArchiveFolder.Record(scan.Id, "drive-sara", "Women Volleyball/SS-SPORT Sara Ahmadi／L", "SS-SPORT Sara Ahmadi／L", PlayerCategory.Women, new ArchiveFolderCounts(9, 1, 0, 1_000_000_000, 9, 1, 0, false), linked, now);
        await _host.SaveAsync(ivan);
        await _host.SaveAsync(sara);
        return (scan.Id, ivan.Id, sara.Id);
    }
}
