using NSubstitute;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Archive;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using static SsSport.Drive.Application.UnitTests.Archive.ArchiveFiles;

namespace SsSport.Drive.Application.UnitTests.Archive;

/// <summary>
/// M-1: an admin starts a read-only scan of the Shared Drive; a background job walks the <c>Men Volleyball</c> and
/// <c>Women Volleyball</c> folders, lists every player folder, matches each file's md5 against the R2 backup and keeps a
/// report row per folder. Only one scan runs at a time; a Drive outage fails the scan with a reason and keeps no rows.
/// </summary>
public sealed class ArchiveScanTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The admin.</summary>
    private static readonly Guid _admin = Guid.CreateVersion7(_now);

    /// <summary>The scans.</summary>
    private readonly InMemoryRepository<ArchiveScan> _scans = new();

    /// <summary>The found folders.</summary>
    private readonly InMemoryRepository<ArchiveFolder> _found = new();

    /// <summary>The linked Drive folders.</summary>
    private readonly InMemoryRepository<DriveFolder> _linked = new();

    /// <summary>The audit log.</summary>
    private readonly InMemoryRepository<AuditEntry> _audit = new();

    /// <summary>The scheduler.</summary>
    private readonly IIngestScheduler _scheduler = Substitute.For<IIngestScheduler>();

    /// <summary>The Drive.</summary>
    private readonly FakeArchiveDrive _drive = new();

    /// <summary>The clock.</summary>
    private readonly TimeProvider _clock = new FixedClock(_now);

    /// <summary>Starting a scan records it as running, audits it and queues the job.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Starting_a_scan_queues_the_job()
    {
        var dto = await StartHandler().Handle(new StartArchiveScanCommand(), TestContext.Current.CancellationToken);

        var scan = _scans.Added.Should().ContainSingle().Subject;
        scan.Status.Should().Be(ArchiveScanStatus.Running);
        scan.StartedBy.Should().Be(_admin);
        dto.Id.Should().Be(scan.Id);
        dto.Status.Should().Be("Running");
        _audit.Added.Should().ContainSingle(entry => entry.Action == "archive.scan_started" && entry.TargetId == scan.Id.ToString());
        await _scheduler.Received(1).EnqueueAsync(IngestStep.ScanArchive, scan.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>A second scan cannot start while one runs.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_one_scan_runs_at_a_time()
    {
        _scans.Seed(ArchiveScan.Start(_admin, _now.AddMinutes(-10)));

        var start = () => StartHandler().Handle(new StartArchiveScanCommand(), TestContext.Current.CancellationToken);

        (await start.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("archive.scan_running");
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A scan stuck for over two hours (a crashed worker) does not block a new one.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_stale_scan_does_not_block_a_new_one()
    {
        _scans.Seed(ArchiveScan.Start(_admin, _now.AddHours(-3)));

        await StartHandler().Handle(new StartArchiveScanCommand(), TestContext.Current.CancellationToken);

        _scans.Added.Should().ContainSingle();
    }

    /// <summary>
    /// The job walks both category folders, keeps one row per player folder with its counts, matches md5s against the
    /// backup and counts loose files and files the listing skipped as skipped. Other top-level folders are ignored.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_scan_reports_every_player_folder()
    {
        _drive.TopFolder("men", "Men Volleyball").TopFolder("women", "Women Volleyball").TopFolder("old", "Old stuff").LooseRootFiles(2)
            .LooseFiles("men", 1)
            .PlayerFolder("men", "p1", "SS-SPORT Ivan Valeev／OH-OPP", [File("full.mp4", "aaa", 3000), File("hl.mov", "bbb", 1000), File("stats.pdf", "ccc", 10)], skipped: 1)
            .PlayerFolder("women", "p2", "SS-SPRT Sara Ahmadi／Libero", [File("game.mp4", null, 500), File("game.dvw", "ddd", 5)], truncated: true);
        var scan = Running();

        await RunHandler(new FakeBackup("aaa", "ccc")).Handle(new RunArchiveScanCommand(scan.Id), TestContext.Current.CancellationToken);

        scan.Status.Should().Be(ArchiveScanStatus.Completed);
        scan.Totals.Should().Be(new ArchiveScanTotals(Folders: 2, Files: 5, Bytes: 4515, InBackup: 2, MissingFromBackup: 3, Skipped: 4));
        _found.Added.Should().HaveCount(2);

        var ivan = _found.Added.Single(folder => folder.DriveFolderId == "p1");
        ivan.Path.Should().Be("Men Volleyball/SS-SPORT Ivan Valeev／OH-OPP");
        ivan.Category.Should().Be(PlayerCategory.Men);
        ivan.SuggestedName.Should().Be("Ivan Valeev");
        ivan.Counts.Should().Be(new ArchiveFolderCounts(Videos: 2, Documents: 1, Scouts: 0, Bytes: 4010, InBackup: 2, MissingFromBackup: 1, Skipped: 1, IsTruncated: false));
        ivan.State.Should().Be(ArchiveFolderState.New);
        ivan.ScanId.Should().Be(scan.Id);

        var sara = _found.Added.Single(folder => folder.DriveFolderId == "p2");
        sara.Category.Should().Be(PlayerCategory.Women);
        sara.Counts.Should().Be(new ArchiveFolderCounts(Videos: 1, Documents: 0, Scouts: 1, Bytes: 505, InBackup: 0, MissingFromBackup: 2, Skipped: 0, IsTruncated: true));
        _drive.Listed.Should().BeEquivalentTo("p1", "p2");
    }

    /// <summary>A folder already linked to a player (an analyst added it) is reported as linked to that player.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_folder_already_in_the_app_is_reported_as_linked()
    {
        var player = Guid.CreateVersion7(_now);
        _linked.Seed(DriveFolder.Link(player, "p1", "SS-SPORT Ivan Valeev", _now.AddDays(-3)));
        _drive.TopFolder("men", "Men Volleyball").PlayerFolder("men", "p1", "SS-SPORT Ivan Valeev", [File("a.mp4", "aaa")]);
        var scan = Running();

        await RunHandler(new FakeBackup()).Handle(new RunArchiveScanCommand(scan.Id), TestContext.Current.CancellationToken);

        var folder = _found.Added.Should().ContainSingle().Subject;
        folder.State.Should().Be(ArchiveFolderState.Linked);
        folder.PlayerId.Should().Be(player);
    }

    /// <summary>A Drive outage fails the scan with a reason and keeps none of the rows found before it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_drive_outage_fails_the_scan_and_keeps_no_rows()
    {
        _drive.TopFolder("men", "Men Volleyball").PlayerFolder("men", "p1", "SS-SPORT Ivan Valeev", [File("a.mp4", "aaa")]);
        _drive.Down = true;
        var scan = Running();

        await RunHandler(new FakeBackup()).Handle(new RunArchiveScanCommand(scan.Id), TestContext.Current.CancellationToken);

        scan.Status.Should().Be(ArchiveScanStatus.Failed);
        scan.FailureReason.Should().Be("Google Drive cannot be reached right now.");
        _found.Added.Should().BeEmpty();
    }

    /// <summary>A scan that already finished (the job ran twice) is left alone.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_finished_scan_is_not_run_again()
    {
        _drive.TopFolder("men", "Men Volleyball").PlayerFolder("men", "p1", "SS-SPORT Ivan Valeev", [File("a.mp4", "aaa")]);
        var scan = Running();
        scan.Complete(ArchiveScanTotals.Empty, _now);

        await RunHandler(new FakeBackup()).Handle(new RunArchiveScanCommand(scan.Id), TestContext.Current.CancellationToken);

        _found.Added.Should().BeEmpty();
        _drive.Listed.Should().BeEmpty();
    }

    /// <summary>A scan that is not saved yet (the job ran before the start committed) fails the job, so it is retried.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_scan_fails_the_job_for_a_retry()
    {
        var run = () => RunHandler(new FakeBackup()).Handle(new RunArchiveScanCommand(Guid.CreateVersion7(_now)), TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<NotFoundException>();
    }

    /// <summary>Builds the start handler.</summary>
    /// <returns>The handler.</returns>
    private StartArchiveScanCommandHandler StartHandler() => new(_scans, _scheduler, _audit, new AdminUser(_admin), _clock);

    /// <summary>Builds the job's handler.</summary>
    /// <param name="backup">The backup.</param>
    /// <returns>The handler.</returns>
    private RunArchiveScanCommandHandler RunHandler(FakeBackup backup) => new(_scans, _found, _linked, _drive, _drive, backup, _clock);

    /// <summary>Seeds a running scan.</summary>
    /// <returns>The scan.</returns>
    private ArchiveScan Running()
    {
        var scan = ArchiveScan.Start(_admin, _now);
        _scans.Seed(scan);
        return scan;
    }
}

/// <summary>A clock that always says the same time.</summary>
/// <param name="now">The time.</param>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => now;
}
