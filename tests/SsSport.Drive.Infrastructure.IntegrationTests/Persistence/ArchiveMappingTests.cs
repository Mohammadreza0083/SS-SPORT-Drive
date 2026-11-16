using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Epic 8's tables survive a save and a reload: a scan with its totals (<c>archive_scans</c>), a found folder with its counts
/// and suggestions (<c>archive_folders</c>), and a player's origin (<c>players.origin</c>). A migration adds them.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ArchiveMappingTests(AppDbContextFixture fixture) : IClassFixture<AppDbContextFixture>
{
    /// <summary>A fixed time, in UTC as PostgreSQL timestamptz requires.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A completed scan keeps its totals.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_scan_round_trips()
    {
        var scan = ArchiveScan.Start(Guid.CreateVersion7(_now), _now);
        var totals = new ArchiveScanTotals(40, 210, 107_374_182_400, 190, 20, 6);
        scan.Complete(totals, _now.AddMinutes(4));
        await SaveAsync(scan);

        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<ArchiveScan>().SingleAsync(s => s.Id == scan.Id, TestContext.Current.CancellationToken);

        loaded.Status.Should().Be(ArchiveScanStatus.Completed);
        loaded.Totals.Should().Be(totals);
        loaded.FinishedAt.Should().Be(_now.AddMinutes(4));
        loaded.StartedBy.Should().Be(scan.StartedBy);
    }

    /// <summary>A found folder keeps its path, counts, suggestions and state.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_found_folder_round_trips()
    {
        var scan = ArchiveScan.Start(Guid.CreateVersion7(_now), _now);
        await SaveAsync(scan);
        var counts = new ArchiveFolderCounts(8, 2, 1, 4_000_000_000, 10, 1, 2, true);
        var folder = ArchiveFolder.Record(scan.Id, "1AbCfolder", "Men Volleyball/SS-SPORT Ivan Valeev／OH-OPP", "SS-SPORT Ivan Valeev／OH-OPP", PlayerCategory.Men, counts, null, _now);
        var player = Guid.CreateVersion7(_now);
        folder.MarkImported(player, _now.AddMinutes(1));
        await SaveAsync(folder);

        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<ArchiveFolder>().SingleAsync(f => f.Id == folder.Id, TestContext.Current.CancellationToken);

        loaded.Path.Should().Be("Men Volleyball/SS-SPORT Ivan Valeev／OH-OPP");
        loaded.SuggestedName.Should().Be("Ivan Valeev");
        loaded.SuggestedPositions!.Items.Should().Equal(Position.OutsideHitter, Position.Opposite);
        loaded.Counts.Should().Be(counts);
        loaded.State.Should().Be(ArchiveFolderState.Imported);
        loaded.PlayerId.Should().Be(player);
        loaded.ImportedAt.Should().Be(_now.AddMinutes(1));
    }

    /// <summary>A player's origin round-trips, and team players stay team players.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_player_origin_round_trips()
    {
        var archive = Player.CreateFromArchive(PlayerCategory.Men, _now);
        var team = Player.Create(PlayerCategory.Women, _now);
        await SaveAsync(archive);
        await SaveAsync(team);

        await using var reader = fixture.CreateContext();
        (await reader.Set<Player>().SingleAsync(p => p.Id == archive.Id, TestContext.Current.CancellationToken)).Origin.Should().Be(PlayerOrigin.Archive);
        (await reader.Set<Player>().SingleAsync(p => p.Id == team.Id, TestContext.Current.CancellationToken)).Origin.Should().Be(PlayerOrigin.Team);
    }

    /// <summary>Adds an aggregate and saves it in its own context.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="aggregate">The aggregate.</param>
    /// <returns>A task that completes when it is saved.</returns>
    private async Task SaveAsync<T>(T aggregate)
        where T : class
    {
        await using AppDbContext context = fixture.CreateContext();
        context.Set<T>().Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
