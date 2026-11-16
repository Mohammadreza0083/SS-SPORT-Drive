using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Archive;

/// <summary>A scan of the Shared Drive and its totals (M-1).</summary>
public sealed record ArchiveScanDto
{
    /// <summary>Gets the scan id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets <c>Running</c>, <c>Completed</c> or <c>Failed</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets when it started (UTC).</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets when it finished (UTC), or <see langword="null"/> while it runs.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Gets why it failed, or <see langword="null"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Gets the player folders found.</summary>
    public int Folders { get; init; }

    /// <summary>Gets the usable files found.</summary>
    public int Files { get; init; }

    /// <summary>Gets their size in bytes.</summary>
    public long Bytes { get; init; }

    /// <summary>Gets how many of them the R2 backup holds.</summary>
    public int InBackup { get; init; }

    /// <summary>Gets how many the backup does not hold.</summary>
    public int MissingFromBackup { get; init; }

    /// <summary>Gets the files left out (unsupported, Google Docs, empty, loose).</summary>
    public int Skipped { get; init; }
}

/// <summary>The console's archive page header (M-1, M-2).</summary>
public sealed record ArchiveSummaryDto
{
    /// <summary>Gets the newest scan, whatever its status, or <see langword="null"/> before the first.</summary>
    public ArchiveScanDto? LatestScan { get; init; }

    /// <summary>Gets the newest completed scan, whose folders the list shows, or <see langword="null"/>.</summary>
    public ArchiveScanDto? Report { get; init; }

    /// <summary>Gets the report's folders not in the app yet.</summary>
    public int New { get; init; }

    /// <summary>Gets the report's folders already linked to a player before the scan.</summary>
    public int Linked { get; init; }

    /// <summary>Gets the report's folders imported since.</summary>
    public int Imported { get; init; }
}

/// <summary>One player folder of the report, with its suggestions.</summary>
public sealed record ArchiveFolderRowDto
{
    /// <summary>Gets the row id (used to import it).</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the category folder and the folder name.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets the folder name.</summary>
    public string FolderName { get; init; } = string.Empty;

    /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Gets the suggested name, or <see langword="null"/>.</summary>
    public string? SuggestedName { get; init; }

    /// <summary>Gets the suggested position codes, primary first.</summary>
    public IReadOnlyList<string> SuggestedPositions { get; init; } = [];

    /// <summary>Gets the videos.</summary>
    public int Videos { get; init; }

    /// <summary>Gets the documents.</summary>
    public int Documents { get; init; }

    /// <summary>Gets the scout files.</summary>
    public int Scouts { get; init; }

    /// <summary>Gets the usable files.</summary>
    public int Files { get; init; }

    /// <summary>Gets their size in bytes.</summary>
    public long Bytes { get; init; }

    /// <summary>Gets how many the backup holds.</summary>
    public int InBackup { get; init; }

    /// <summary>Gets how many the backup does not hold.</summary>
    public int MissingFromBackup { get; init; }

    /// <summary>Gets the files left out.</summary>
    public int Skipped { get; init; }

    /// <summary>Gets whether the listing hit its limit, so the counts are a lower bound.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Gets <c>New</c>, <c>Linked</c> or <c>Imported</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Gets the player it belongs to, or <see langword="null"/> while new.</summary>
    public Guid? PlayerId { get; init; }
}

/// <summary>The archive rules shared by the console's commands and queries.</summary>
public static class ArchiveRules
{
    /// <summary>The audit action of a started scan.</summary>
    public const string ScanStartedAction = "archive.scan_started";

    /// <summary>The audit action of an imported folder.</summary>
    public const string ImportedAction = "archive.imported";

    /// <summary>The audit action of a CSV import.</summary>
    public const string CsvImportedAction = "archive.csv_imported";

    /// <summary>The audit target of a scan.</summary>
    public const string ScanTarget = "ArchiveScan";

    /// <summary>Gets the folder states the list filter accepts.</summary>
    public static IReadOnlyList<string> States { get; } = ["new", "linked", "imported"];

    /// <summary>Maps a scan.</summary>
    /// <param name="scan">The scan.</param>
    /// <returns>The DTO.</returns>
    public static ArchiveScanDto ToDto(this ArchiveScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return new ArchiveScanDto
        {
            Id = scan.Id,
            Status = scan.Status.ToString(),
            StartedAt = scan.StartedAt,
            FinishedAt = scan.FinishedAt,
            FailureReason = scan.FailureReason,
            Folders = scan.Totals.Folders,
            Files = scan.Totals.Files,
            Bytes = scan.Totals.Bytes,
            InBackup = scan.Totals.InBackup,
            MissingFromBackup = scan.Totals.MissingFromBackup,
            Skipped = scan.Totals.Skipped,
        };
    }

    /// <summary>Maps a found folder.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The DTO.</returns>
    public static ArchiveFolderRowDto ToRowDto(this ArchiveFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new ArchiveFolderRowDto
        {
            Id = folder.Id,
            Path = folder.Path,
            FolderName = folder.FolderName,
            Category = folder.Category.ToString(),
            SuggestedName = folder.SuggestedName,
            SuggestedPositions = folder.SuggestedPositions?.Items.Select(position => position.Code()).ToList() ?? [],
            Videos = folder.Counts.Videos,
            Documents = folder.Counts.Documents,
            Scouts = folder.Counts.Scouts,
            Files = folder.Counts.Files,
            Bytes = folder.Counts.Bytes,
            InBackup = folder.Counts.InBackup,
            MissingFromBackup = folder.Counts.MissingFromBackup,
            Skipped = folder.Counts.Skipped,
            IsTruncated = folder.Counts.IsTruncated,
            State = folder.State.ToString(),
            PlayerId = folder.PlayerId,
        };
    }
}

/// <summary>The newest scan, optionally only a completed one, read untracked.</summary>
public sealed class LatestArchiveScanSpec : Specification<ArchiveScan>, ISingleResultSpecification<ArchiveScan>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="completedOnly">Whether only completed scans count.</param>
    public LatestArchiveScanSpec(bool completedOnly = false)
    {
        if (completedOnly)
        {
            Query.Where(scan => scan.Status == ArchiveScanStatus.Completed);
        }

        Query.OrderByDescending(scan => scan.StartedAt).ThenByDescending(scan => scan.Id).Take(1).AsNoTracking();
    }
}

/// <summary>The folders of a scan in path order, optionally in one state (paged by <see cref="IPagedReader"/>).</summary>
public sealed class ArchiveFoldersOfScanSpec : Specification<ArchiveFolder>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="scanId">The scan.</param>
    /// <param name="state">A state, or <see langword="null"/> for all.</param>
    public ArchiveFoldersOfScanSpec(Guid scanId, ArchiveFolderState? state = null)
    {
        Query.Where(folder => folder.ScanId == scanId);
        if (state is { } only)
        {
            Query.Where(folder => folder.State == only);
        }

        // The paged reader adds the id as the tie-breaker and reads untracked.
        Query.OrderBy(folder => folder.Path);
    }
}

/// <summary>Starts a scan of the Shared Drive (<c>POST /admin/archive/scans</c>), for admins only.</summary>
public sealed record StartArchiveScanCommand : ICommand<ArchiveScanDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="StartArchiveScanCommand"/>; it carries no input.</summary>
public sealed class StartArchiveScanCommandValidator : AbstractValidator<StartArchiveScanCommand>;

/// <summary>Handles <see cref="StartArchiveScanCommand"/>: one scan at a time, audited, run by a background job.</summary>
/// <param name="scans">The scans.</param>
/// <param name="scheduler">Queues the job.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The admin.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class StartArchiveScanCommandHandler(
    IRepository<ArchiveScan, Guid> scans,
    IIngestScheduler scheduler,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<StartArchiveScanCommand, ArchiveScanDto>
{
    /// <summary>Starts the scan.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The running scan.</returns>
    /// <exception cref="ConflictException"><c>archive.scan_running</c> while another scan runs.</exception>
    public async Task<ArchiveScanDto> Handle(StartArchiveScanCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await scans.FirstOrDefaultAsync(new LatestArchiveScanSpec(), cancellationToken) is { } latest && latest.IsActive(now))
        {
            throw new ConflictException("archive.scan_running", "A scan of the archive is already running.");
        }

        var actor = ShareLinkRules.ActorId(currentUser);
        var scan = ArchiveScan.Start(actor ?? Guid.Empty, now);
        scans.Add(scan);
        audit.Add(AuditEntry.Record(actor, ArchiveRules.ScanStartedAction, ArchiveRules.ScanTarget, scan.Id.ToString(), null, now));
        await scheduler.EnqueueAsync(IngestStep.ScanArchive, scan.Id, cancellationToken);
        return scan.ToDto();
    }
}

/// <summary>Runs a scan (M-1): walks the archive, matches md5s to the backup and keeps a row per player folder.</summary>
/// <param name="ScanId">The scan.</param>
[InternalRequest("Sent only by the archive scan job, with the id of a scan an admin started.")]
public sealed record RunArchiveScanCommand(Guid ScanId) : ICommand;

/// <summary>Handles <see cref="RunArchiveScanCommand"/>.</summary>
/// <param name="scans">The scans.</param>
/// <param name="found">The found folders.</param>
/// <param name="linked">The Drive folders already linked to players.</param>
/// <param name="browser">Browses the Shared Drive's folders.</param>
/// <param name="lister">Lists a player folder's files.</param>
/// <param name="backup">The read-only rclone backup.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class RunArchiveScanCommandHandler(
    IRepository<ArchiveScan, Guid> scans,
    IRepository<ArchiveFolder, Guid> found,
    IRepository<DriveFolder, Guid> linked,
    IDriveArchiveBrowser browser,
    IDriveFolderLister lister,
    IBackupArchive backup,
    TimeProvider timeProvider) : ICommandHandler<RunArchiveScanCommand>
{
    /// <summary>Walks the archive. A Drive outage fails the scan with its reason; nothing found before it is kept.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>A task that completes when the scan has completed or failed.</returns>
    /// <exception cref="NotFoundException">The scan is not saved yet; the job is retried.</exception>
    public async Task Handle(RunArchiveScanCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scan = await scans.GetByIdAsync(request.ScanId, cancellationToken) ?? throw new NotFoundException("Archive scan");
        if (scan.Status != ArchiveScanStatus.Running)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var rows = new List<ArchiveFolder>();
        int skipped;
        try
        {
            skipped = await WalkAsync(scan.Id, rows, now, cancellationToken);
        }
        catch (ServiceUnavailableException exception)
        {
            scan.Fail(exception.Message, timeProvider.GetUtcNow());
            return;
        }

        rows.ForEach(found.Add);
        scan.Complete(
            new ArchiveScanTotals(
                Folders: rows.Count,
                Files: rows.Sum(row => row.Counts.Files),
                Bytes: rows.Sum(row => row.Counts.Bytes),
                InBackup: rows.Sum(row => row.Counts.InBackup),
                MissingFromBackup: rows.Sum(row => row.Counts.MissingFromBackup),
                Skipped: skipped + rows.Sum(row => row.Counts.Skipped)),
            timeProvider.GetUtcNow());
    }

    /// <summary>Walks the category folders and records every player folder.</summary>
    /// <param name="scanId">The scan.</param>
    /// <param name="rows">Collects the rows.</param>
    /// <param name="now">The time of the scan.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The loose files outside player folders.</returns>
    private async Task<int> WalkAsync(Guid scanId, List<ArchiveFolder> rows, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var root = await browser.ListRootAsync(cancellationToken);
        var loose = root.FileCount;
        foreach (var top in root.Folders)
        {
            if (!ArchiveFolderName.TryReadCategory(top.Name, out var category))
            {
                continue;
            }

            var players = await browser.ListFolderAsync(top.Id, cancellationToken);
            loose += players.FileCount;
            foreach (var folder in players.Folders)
            {
                var listing = await lister.ListAsync(folder.Id, cancellationToken);
                if (listing.Status != DriveFolderStatus.Found)
                {
                    continue;
                }

                var counts = await CountAsync(listing, cancellationToken);
                var link = await linked.FirstOrDefaultAsync(new DriveFolderByDriveIdSpec(folder.Id), cancellationToken);
                rows.Add(ArchiveFolder.Record(scanId, folder.Id, $"{top.Name}/{folder.Name}", folder.Name, category, counts, link?.PlayerId, now));
            }
        }

        return loose;
    }

    /// <summary>Counts a player folder's files by kind and by whether the backup holds them.</summary>
    /// <param name="listing">The folder's listing.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>The counts.</returns>
    private async Task<ArchiveFolderCounts> CountAsync(DriveFolderListing listing, CancellationToken cancellationToken)
    {
        int videos = 0, documents = 0, scouts = 0, inBackup = 0, missing = 0;
        long bytes = 0;
        foreach (var file in listing.Files)
        {
            bytes += file.SizeBytes;
            switch (file.Kind)
            {
                case AssetKind.Video: videos++; break;
                case AssetKind.Document: documents++; break;
                default: scouts++; break;
            }

            if (file.Md5 is { } md5 && await backup.FindByMd5Async(md5, cancellationToken) is not null)
            {
                inBackup++;
            }
            else
            {
                missing++;
            }
        }

        return new ArchiveFolderCounts(videos, documents, scouts, bytes, inBackup, missing, listing.SkippedCount, listing.IsTruncated);
    }
}

/// <summary>Reads the archive page's header (<c>GET /admin/archive</c>), for admins only.</summary>
public sealed record GetArchiveSummaryQuery : IQuery<ArchiveSummaryDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetArchiveSummaryQuery"/>; it carries no input.</summary>
public sealed class GetArchiveSummaryQueryValidator : AbstractValidator<GetArchiveSummaryQuery>;

/// <summary>Handles <see cref="GetArchiveSummaryQuery"/>.</summary>
/// <param name="scans">The scans.</param>
/// <param name="found">The found folders.</param>
public sealed class GetArchiveSummaryQueryHandler(IRepository<ArchiveScan, Guid> scans, IRepository<ArchiveFolder, Guid> found)
    : IQueryHandler<GetArchiveSummaryQuery, ArchiveSummaryDto>
{
    /// <summary>Reads the newest scan, the newest report and its counts.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The header.</returns>
    public async Task<ArchiveSummaryDto> Handle(GetArchiveSummaryQuery request, CancellationToken cancellationToken)
    {
        var latest = await scans.FirstOrDefaultAsync(new LatestArchiveScanSpec(), cancellationToken);
        var report = await scans.FirstOrDefaultAsync(new LatestArchiveScanSpec(completedOnly: true), cancellationToken);
        if (report is null)
        {
            return new ArchiveSummaryDto { LatestScan = latest?.ToDto() };
        }

        return new ArchiveSummaryDto
        {
            LatestScan = latest?.ToDto(),
            Report = report.ToDto(),
            New = await found.CountAsync(new ArchiveFoldersOfScanSpec(report.Id, ArchiveFolderState.New), cancellationToken),
            Linked = await found.CountAsync(new ArchiveFoldersOfScanSpec(report.Id, ArchiveFolderState.Linked), cancellationToken),
            Imported = await found.CountAsync(new ArchiveFoldersOfScanSpec(report.Id, ArchiveFolderState.Imported), cancellationToken),
        };
    }
}

/// <summary>Reads one page of the newest report's folders (<c>GET /admin/archive/folders</c>), for admins only.</summary>
/// <param name="Paging">The page.</param>
/// <param name="State">A state filter (<c>new</c>, <c>linked</c>, <c>imported</c>), or <see langword="null"/>.</param>
public sealed record GetArchiveFoldersQuery(PageRequest Paging, string? State) : IPagedQuery<ArchiveFolderRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetArchiveFoldersQuery"/> on the server; an unknown state is a 400.</summary>
public sealed class GetArchiveFoldersQueryValidator : AbstractValidator<GetArchiveFoldersQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetArchiveFoldersQueryValidator()
    {
        RuleFor(query => query.Paging).ValidPaging();
        RuleFor(query => query.State)
            .Must(state => ArchiveRules.States.Contains(state!, StringComparer.OrdinalIgnoreCase))
            .When(query => !string.IsNullOrEmpty(query.State))
            .WithMessage("Use new, linked or imported.")
            .OverridePropertyName("state");
    }
}

/// <summary>Handles <see cref="GetArchiveFoldersQuery"/>; before the first completed scan the list is empty.</summary>
/// <param name="scans">The scans.</param>
/// <param name="pages">The paged reader.</param>
public sealed class GetArchiveFoldersQueryHandler(IRepository<ArchiveScan, Guid> scans, IPagedReader pages)
    : IQueryHandler<GetArchiveFoldersQuery, PagedResult<ArchiveFolderRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<PagedResult<ArchiveFolderRowDto>> Handle(GetArchiveFoldersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await scans.FirstOrDefaultAsync(new LatestArchiveScanSpec(completedOnly: true), cancellationToken) is not { } report)
        {
            return PagedResult<ArchiveFolderRowDto>.Create([], request.Paging, 0);
        }

        ArchiveFolderState? state = string.IsNullOrEmpty(request.State) ? null : Enum.Parse<ArchiveFolderState>(request.State, ignoreCase: true);
        return await pages.PageAsync(new ArchiveFoldersOfScanSpec(report.Id, state), request.Paging, folder => folder.ToRowDto(), cancellationToken);
    }
}
