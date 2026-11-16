using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Archive;

/// <summary>Where a scan of the Shared Drive is.</summary>
public enum ArchiveScanStatus
{
    /// <summary>The job is walking the Drive.</summary>
    Running,

    /// <summary>The report is ready.</summary>
    Completed,

    /// <summary>The scan stopped; <see cref="ArchiveScan.FailureReason"/> says why.</summary>
    Failed,
}

/// <summary>What a whole scan found (M-1).</summary>
/// <param name="Folders">Player folders found under the category folders.</param>
/// <param name="Files">Usable files (videos, documents, scout files) in them.</param>
/// <param name="Bytes">The size of those files.</param>
/// <param name="InBackup">Files whose md5 the R2 backup holds.</param>
/// <param name="MissingFromBackup">Files the backup does not hold (ingest copies them from Drive).</param>
/// <param name="Skipped">Files left out: unsupported types, Google Docs, empty files, and loose files outside player folders.</param>
public sealed record ArchiveScanTotals(int Folders, int Files, long Bytes, int InBackup, int MissingFromBackup, int Skipped)
{
    /// <summary>Gets the totals of a scan that found nothing.</summary>
    public static ArchiveScanTotals Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// One read-only scan of the Shared Drive's archive (M-1): the job walks the category folders, lists every player folder
/// and matches each file's md5 against the rclone backup. Only one runs at a time.
/// </summary>
public sealed class ArchiveScan : AggregateRoot<Guid>
{
    /// <summary>How long a scan may run before it is taken as crashed.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    /// <summary>Initializes a scan; use <see cref="Start"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="startedBy">The admin who started it.</param>
    /// <param name="startedAt">When it started (UTC).</param>
    private ArchiveScan(Guid id, Guid startedBy, DateTimeOffset startedAt)
        : base(id)
    {
        StartedBy = startedBy;
        StartedAt = startedAt;
    }

    /// <summary>Gets the admin who started the scan.</summary>
    public Guid StartedBy { get; }

    /// <summary>Gets when the scan started (UTC).</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets when the scan finished (UTC), or <see langword="null"/> while it runs.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Gets where the scan is.</summary>
    public ArchiveScanStatus Status { get; private set; } = ArchiveScanStatus.Running;

    /// <summary>Gets why the scan failed, or <see langword="null"/>.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Gets what the scan found; empty until it completes.</summary>
    public ArchiveScanTotals Totals { get; private set; } = ArchiveScanTotals.Empty;

    /// <summary>Starts a scan.</summary>
    /// <param name="startedBy">The admin.</param>
    /// <param name="startedAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The running scan.</returns>
    public static ArchiveScan Start(Guid startedBy, DateTimeOffset startedAt) => new(Guid.CreateVersion7(startedAt), startedBy, startedAt);

    /// <summary>Gets whether the scan still counts as running: it runs and is not stale.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="true"/> while another scan must wait.</returns>
    public bool IsActive(DateTimeOffset now) => Status == ArchiveScanStatus.Running && now - StartedAt <= StaleAfter;

    /// <summary>Records the totals of a finished scan.</summary>
    /// <param name="totals">What it found.</param>
    /// <param name="finishedAt">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>archive.scan_finished</c> when the scan already finished.</exception>
    public void Complete(ArchiveScanTotals totals, DateTimeOffset finishedAt)
    {
        ArgumentNullException.ThrowIfNull(totals);
        EnsureRunning();
        Totals = totals;
        Status = ArchiveScanStatus.Completed;
        FinishedAt = finishedAt;
    }

    /// <summary>Records why the scan stopped.</summary>
    /// <param name="reason">The reason, for the console.</param>
    /// <param name="finishedAt">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>archive.scan_finished</c> when the scan already finished.</exception>
    public void Fail(string reason, DateTimeOffset finishedAt)
    {
        EnsureRunning();
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "The scan stopped." : reason.Trim();
        Status = ArchiveScanStatus.Failed;
        FinishedAt = finishedAt;
    }

    /// <summary>Refuses to finish a scan twice.</summary>
    private void EnsureRunning()
    {
        if (Status != ArchiveScanStatus.Running)
        {
            throw new DomainException("archive.scan_finished", "This scan has already finished.");
        }
    }
}
