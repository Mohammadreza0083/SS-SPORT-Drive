using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Archive;

/// <summary>
/// M-1 and M-2: a scan of the Shared Drive runs once at a time and records its totals; each player folder it found is
/// kept with its counts and suggestions, and is imported at most once.
/// </summary>
public sealed class ArchiveScanTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An admin.</summary>
    private static readonly Guid _admin = Guid.NewGuid();

    /// <summary>A new scan runs, started by an admin.</summary>
    [Fact]
    public void A_scan_starts_running()
    {
        var scan = ArchiveScan.Start(_admin, _now);

        scan.Status.Should().Be(ArchiveScanStatus.Running);
        scan.StartedBy.Should().Be(_admin);
        scan.StartedAt.Should().Be(_now);
        scan.FinishedAt.Should().BeNull();
    }

    /// <summary>A finished scan keeps its totals.</summary>
    [Fact]
    public void A_scan_completes_with_its_totals()
    {
        var scan = ArchiveScan.Start(_admin, _now);
        var totals = new ArchiveScanTotals(Folders: 40, Files: 210, Bytes: 107_374_182_400, InBackup: 190, MissingFromBackup: 20, Skipped: 6);

        scan.Complete(totals, _now.AddMinutes(4));

        scan.Status.Should().Be(ArchiveScanStatus.Completed);
        scan.FinishedAt.Should().Be(_now.AddMinutes(4));
        scan.Totals.Should().Be(totals);
        scan.FailureReason.Should().BeNull();
    }

    /// <summary>A failed scan says why and keeps no totals.</summary>
    [Fact]
    public void A_scan_fails_with_a_reason()
    {
        var scan = ArchiveScan.Start(_admin, _now);

        scan.Fail("Google Drive cannot be reached.", _now.AddMinutes(1));

        scan.Status.Should().Be(ArchiveScanStatus.Failed);
        scan.FailureReason.Should().Be("Google Drive cannot be reached.");
        scan.Totals.Should().Be(ArchiveScanTotals.Empty);
    }

    /// <summary>A scan finishes once.</summary>
    [Fact]
    public void A_finished_scan_cannot_finish_again()
    {
        var scan = ArchiveScan.Start(_admin, _now);
        scan.Complete(ArchiveScanTotals.Empty, _now);

        var again = () => scan.Fail("late", _now);

        again.Should().Throw<DomainException>().Which.Code.Should().Be("archive.scan_finished");
    }

    /// <summary>A scan that runs for more than two hours is stale (the worker crashed), so a new one may start.</summary>
    [Fact]
    public void A_scan_running_for_over_two_hours_is_stale()
    {
        var scan = ArchiveScan.Start(_admin, _now);

        scan.IsActive(_now.AddHours(1)).Should().BeTrue();
        scan.IsActive(_now.AddHours(2).AddMinutes(1)).Should().BeFalse();

        scan.Complete(ArchiveScanTotals.Empty, _now.AddMinutes(1));
        scan.IsActive(_now.AddMinutes(2)).Should().BeFalse();
    }

    /// <summary>A found folder keeps its counts and the suggestions parsed from its name.</summary>
    [Fact]
    public void A_found_folder_keeps_its_counts_and_suggestions()
    {
        var scanId = Guid.NewGuid();
        var counts = new ArchiveFolderCounts(Videos: 8, Documents: 2, Scouts: 1, Bytes: 4_000_000_000, InBackup: 10, MissingFromBackup: 1, Skipped: 2, IsTruncated: false);

        var folder = ArchiveFolder.Record(scanId, "1AbC", "Men Volleyball/SS-SPORT Ivan Valeev／OH-OPP", "SS-SPORT Ivan Valeev／OH-OPP", PlayerCategory.Men, counts, linkedPlayerId: null, _now);

        folder.ScanId.Should().Be(scanId);
        folder.DriveFolderId.Should().Be("1AbC");
        folder.Path.Should().Be("Men Volleyball/SS-SPORT Ivan Valeev／OH-OPP");
        folder.FolderName.Should().Be("SS-SPORT Ivan Valeev／OH-OPP");
        folder.Category.Should().Be(PlayerCategory.Men);
        folder.SuggestedName.Should().Be("Ivan Valeev");
        folder.SuggestedPositions!.Items.Should().Equal(Position.OutsideHitter, Position.Opposite);
        folder.Counts.Should().Be(counts);
        folder.Counts.Files.Should().Be(11);
        folder.State.Should().Be(ArchiveFolderState.New);
        folder.PlayerId.Should().BeNull();
    }

    /// <summary>A folder without a position suggests none.</summary>
    [Fact]
    public void A_folder_without_a_position_suggests_none()
    {
        var folder = ArchiveFolder.Record(Guid.NewGuid(), "1AbC", "Men Volleyball/SS-SPORT Aref Moradi", "SS-SPORT Aref Moradi", PlayerCategory.Men, ArchiveFolderCounts.Empty, null, _now);

        folder.SuggestedPositions.Should().BeNull();
    }

    /// <summary>A folder already linked to a player in the app is recorded as linked and cannot be imported.</summary>
    [Fact]
    public void A_folder_linked_to_a_player_cannot_be_imported()
    {
        var player = Guid.NewGuid();
        var folder = ArchiveFolder.Record(Guid.NewGuid(), "1AbC", "Men Volleyball/SS-SPORT Aref Moradi", "SS-SPORT Aref Moradi", PlayerCategory.Men, ArchiveFolderCounts.Empty, player, _now);

        folder.State.Should().Be(ArchiveFolderState.Linked);
        folder.PlayerId.Should().Be(player);

        var import = () => folder.MarkImported(Guid.NewGuid(), _now);
        import.Should().Throw<DomainException>().Which.Code.Should().Be("archive.folder_not_new");
    }

    /// <summary>Importing records the new player once; a second import is refused.</summary>
    [Fact]
    public void A_folder_is_imported_once()
    {
        var folder = ArchiveFolder.Record(Guid.NewGuid(), "1AbC", "Men Volleyball/SS-SPORT Aref Moradi", "SS-SPORT Aref Moradi", PlayerCategory.Men, ArchiveFolderCounts.Empty, null, _now);
        var player = Guid.NewGuid();

        folder.MarkImported(player, _now.AddMinutes(5));

        folder.State.Should().Be(ArchiveFolderState.Imported);
        folder.PlayerId.Should().Be(player);
        folder.ImportedAt.Should().Be(_now.AddMinutes(5));
        var again = () => folder.MarkImported(Guid.NewGuid(), _now);
        again.Should().Throw<DomainException>().Which.Code.Should().Be("archive.folder_not_new");
    }

    /// <summary>A player imported from the archive starts on the waiting list without an analyst, marked as archive.</summary>
    [Fact]
    public void An_archive_player_starts_waiting_without_an_analyst()
    {
        var player = Player.CreateFromArchive(PlayerCategory.Women, _now);

        player.Origin.Should().Be(PlayerOrigin.Archive);
        player.Status.Should().Be(PlayerStatus.Waiting);
        player.AnalystId.Should().BeNull();
        player.Category.Should().Be(PlayerCategory.Women);
        player.DomainEvents.Should().ContainSingle(domainEvent => domainEvent is PlayerCreated);
    }

    /// <summary>Players the team adds come from the team.</summary>
    [Fact]
    public void A_team_player_comes_from_the_team()
    {
        Player.Create(PlayerCategory.Men, _now).Origin.Should().Be(PlayerOrigin.Team);
    }
}
