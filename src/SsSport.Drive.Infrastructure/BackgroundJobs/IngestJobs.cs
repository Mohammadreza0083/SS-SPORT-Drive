using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Archive;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Infrastructure.Media;

namespace SsSport.Drive.Infrastructure.BackgroundJobs;

/// <summary>
/// The ingest queues and recurring job (Epic 3). Light Drive work and heavy media work have their own queues, and archive
/// players' files have their own throttled pair (M-4).
/// </summary>
public static class IngestJobs
{
    /// <summary>The queue of folder syncs, archive scans and copies from Drive; I/O bound, several workers.</summary>
    public const string DriveQueue = "ingest";

    /// <summary>The queue of ffmpeg, PDF and image work; CPU bound, one or two workers.</summary>
    public const string MediaQueue = "media";

    /// <summary>The queue of archive players' copies (M-4): one worker, so the archive never crowds out syncs and team copies.</summary>
    public const string DriveArchiveQueue = "ingest-archive";

    /// <summary>The queue of archive players' processing (M-4): <c>Media__ArchiveWorkers</c> workers that wait while team files do.</summary>
    public const string MediaArchiveQueue = "media-archive";

    /// <summary>How long an archive media job waits before it looks again when team files are waiting.</summary>
    public static readonly TimeSpan ArchiveYieldDelay = TimeSpan.FromMinutes(1);

    /// <summary>The recurring round that queues a sync of every linked folder.</summary>
    public const string FolderSyncRecurringJobId = "ingest-folder-sync";

    /// <summary>Gets the queue a step runs on.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The queue's name.</returns>
    public static string QueueFor(IngestStep step) => step is IngestStep.SyncFolder or IngestStep.CopyOriginal or IngestStep.ScanArchive ? DriveQueue : MediaQueue;
}

/// <summary>Queues each ingest step as a Hangfire job on its queue.</summary>
/// <param name="jobs">Hangfire's client.</param>
internal sealed class HangfireIngestScheduler(IBackgroundJobClient jobs) : IIngestScheduler
{
    /// <summary>Stores a job for the step; a worker of its queue runs it.</summary>
    /// <param name="step">The step.</param>
    /// <param name="targetId">The folder, asset or player.</param>
    /// <param name="cancellationToken">Unused: Hangfire's client is synchronous.</param>
    /// <returns>A completed task once the job is stored.</returns>
    public Task EnqueueAsync(IngestStep step, Guid targetId, CancellationToken cancellationToken)
    {
        var queue = new EnqueuedState(IngestJobs.QueueFor(step));
        var job = IngestJobs.QueueFor(step) == IngestJobs.DriveQueue
            ? Job.FromExpression<IngestJob>(worker => worker.RunDriveStepAsync(step, targetId, CancellationToken.None))
            : Job.FromExpression<IngestJob>(worker => worker.RunMediaStepAsync(step, targetId, CancellationToken.None));
        jobs.Create(job, queue);
        return Task.CompletedTask;
    }

    /// <summary>Stores an asset step on its lane: the team's lane is the normal queue, the archive's its own (M-4).</summary>
    /// <param name="step">The step.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="cancellationToken">Unused: Hangfire's client is synchronous.</param>
    /// <returns>A completed task once the job is stored.</returns>
    public Task EnqueueAssetAsync(IngestStep step, Guid assetId, IngestLane lane, CancellationToken cancellationToken)
    {
        if (lane != IngestLane.Archive || step is not (IngestStep.CopyOriginal or IngestStep.ProcessAsset))
        {
            return EnqueueAsync(step, assetId, cancellationToken);
        }

        if (step == IngestStep.CopyOriginal)
        {
            jobs.Create(Job.FromExpression<IngestJob>(worker => worker.RunArchiveDriveStepAsync(step, assetId, CancellationToken.None)), new EnqueuedState(IngestJobs.DriveArchiveQueue));
        }
        else
        {
            jobs.Create(Job.FromExpression<IngestJob>(worker => worker.RunArchiveMediaStepAsync(step, assetId, CancellationToken.None)), new EnqueuedState(IngestJobs.MediaArchiveQueue));
        }

        return Task.CompletedTask;
    }
}

/// <summary>The Hangfire jobs of ingest. Each runs one MediatR command in its own scope and logs how it went.</summary>
/// <param name="sender">Sends the commands.</param>
/// <param name="jobs">Puts a yielding archive job back on the queue.</param>
/// <param name="storage">Hangfire's storage, to see whether team files wait.</param>
/// <param name="logger">The logger: Hangfire's own logging is not wired to ours, so failures are logged here.</param>
public sealed partial class IngestJob(ISender sender, IBackgroundJobClient jobs, JobStorage storage, ILogger<IngestJob> logger)
{
    /// <summary>
    /// Gets how long a media step (and an archive scan) waits so the change that queued it is saved first: steps queued from
    /// inside a handler run before its transaction commits. Tests set it to zero.
    /// </summary>
    public TimeSpan CommitGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Runs the recurring round that queues a sync of every linked folder. A failure is not retried: the next round runs in 10 minutes.</summary>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when every sync is queued.</returns>
    [Queue(IngestJobs.DriveQueue)]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ScheduleFolderSyncsAsync(CancellationToken cancellationToken)
    {
        var queued = await sender.Send(new ScheduleFolderSyncsCommand(), cancellationToken);
        LogRound(logger, queued);
    }

    /// <summary>Runs a Drive step: a folder sync or a copy of an original. A Drive outage fails the job, and Hangfire retries it later.</summary>
    /// <param name="step">The step.</param>
    /// <param name="targetId">The folder or asset.</param>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when the step is done.</returns>
    [Queue(IngestJobs.DriveQueue)]
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = [60, 300, 900, 1800, 3600])]
    public async Task RunDriveStepAsync(IngestStep step, Guid targetId, CancellationToken cancellationToken)
    {
        if (step == IngestStep.ScanArchive)
        {
            await Task.Delay(CommitGrace, cancellationToken);
        }

        await RunAsync(step, targetId, IngestLane.Live, cancellationToken);
    }

    /// <summary>Runs an archive player's copy on its own queue (M-4); the processing it queues stays on the archive lane.</summary>
    /// <param name="step">The step (a copy).</param>
    /// <param name="targetId">The asset.</param>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when the step is done.</returns>
    [Queue(IngestJobs.DriveArchiveQueue)]
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = [60, 300, 900, 1800, 3600])]
    public Task RunArchiveDriveStepAsync(IngestStep step, Guid targetId, CancellationToken cancellationToken) => RunAsync(step, targetId, IngestLane.Archive, cancellationToken);

    /// <summary>
    /// Runs an archive player's processing on its own queue (M-4). New uploads first: while team files wait on the media
    /// queue, the job does not start; it puts itself back <see cref="IngestJobs.ArchiveYieldDelay"/> later.
    /// </summary>
    /// <param name="step">The step (a processing).</param>
    /// <param name="targetId">The asset.</param>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when the step is done or put back.</returns>
    [Queue(IngestJobs.MediaArchiveQueue)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [120, 600])]
    public async Task RunArchiveMediaStepAsync(IngestStep step, Guid targetId, CancellationToken cancellationToken)
    {
        await Task.Delay(CommitGrace, cancellationToken);
        var waiting = storage.GetMonitoringApi().EnqueuedCount(IngestJobs.MediaQueue);
        if (waiting > 0)
        {
            jobs.Schedule<IngestJob>(worker => worker.RunArchiveMediaStepAsync(step, targetId, CancellationToken.None), IngestJobs.ArchiveYieldDelay);
            LogYielded(logger, targetId, waiting);
            return;
        }

        await RunAsync(step, targetId, IngestLane.Archive, cancellationToken);
    }

    /// <summary>Runs a media step. Media errors fail the asset itself, so a retry only covers a crash.</summary>
    /// <param name="step">The step.</param>
    /// <param name="targetId">The asset or player.</param>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when the step is done.</returns>
    [Queue(IngestJobs.MediaQueue)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [120, 600])]
    public async Task RunMediaStepAsync(IngestStep step, Guid targetId, CancellationToken cancellationToken)
    {
        await Task.Delay(CommitGrace, cancellationToken);
        await RunAsync(step, targetId, IngestLane.Live, cancellationToken);
    }

    /// <summary>Sends the command of a step.</summary>
    /// <param name="step">The step.</param>
    /// <param name="targetId">The folder, asset, player or scan.</param>
    /// <param name="lane">The lane the job runs on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the command is done.</returns>
    private async Task RunAsync(IngestStep step, Guid targetId, IngestLane lane, CancellationToken cancellationToken)
    {
        try
        {
            switch (step)
            {
                case IngestStep.SyncFolder:
                    var result = await sender.Send(new SyncDriveFolderCommand(targetId), cancellationToken);
                    LogSynced(logger, targetId, result.Outcome, result.Added, result.Renamed, result.Replaced, result.Removed, result.Returned, result.Skipped);
                    break;
                case IngestStep.CopyOriginal:
                    await sender.Send(new CopyOriginalCommand(targetId, lane), cancellationToken);
                    break;
                case IngestStep.ProcessAsset:
                    await sender.Send(new ProcessAssetCommand(targetId), cancellationToken);
                    break;
                case IngestStep.RenderQrCode:
                    await sender.Send(new RenderPlayerQrCodeCommand(targetId), cancellationToken);
                    break;
                case IngestStep.CutOutPhoto:
                    await sender.Send(new CutOutPlayerPhotoCommand(targetId), cancellationToken);
                    break;
                case IngestStep.RenderCard:
                    await sender.Send(new RenderPlayerCardCommand(targetId), cancellationToken);
                    break;
                case IngestStep.ScanArchive:
                    await sender.Send(new RunArchiveScanCommand(targetId), cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown ingest step.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, step, targetId);
            throw;
        }
    }

    /// <summary>Logs a finished recurring round.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Queued">How many syncs were queued.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Folder sync round queued {Queued} folders")]
    private static partial void LogRound(ILogger logger, int Queued);

    /// <summary>Logs what a folder sync did.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="FolderId">The folder.</param>
    /// <param name="Outcome">What happened.</param>
    /// <param name="Added">Files added.</param>
    /// <param name="Renamed">Files renamed.</param>
    /// <param name="Replaced">Files replaced.</param>
    /// <param name="Removed">Files removed.</param>
    /// <param name="Returned">Files returned.</param>
    /// <param name="Skipped">Files skipped.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Folder {FolderId} synced: {Outcome}, {Added} added, {Renamed} renamed, {Replaced} replaced, {Removed} removed, {Returned} returned, {Skipped} skipped")]
    private static partial void LogSynced(ILogger logger, Guid FolderId, FolderSyncOutcome Outcome, int Added, int Renamed, int Replaced, int Removed, int Returned, int Skipped);

    /// <summary>Logs an archive job that waited for team files (M-4).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="AssetId">The archive asset.</param>
    /// <param name="Waiting">How many team media jobs were waiting.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Archive asset {AssetId} waits: {Waiting} team media jobs go first")]
    private static partial void LogYielded(ILogger logger, Guid AssetId, long Waiting);

    /// <summary>Logs a step that failed; Hangfire retries it.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="Step">The step.</param>
    /// <param name="TargetId">The folder, asset or player.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingest step {Step} for {TargetId} failed; Hangfire may retry it")]
    private static partial void LogFailed(ILogger logger, Exception exception, IngestStep Step, Guid TargetId);
}

/// <summary>
/// The Hangfire servers of ingest, inside the Workers host: the <c>ingest</c> queue with two workers (Drive is I/O bound),
/// the <c>media</c> queue with <c>Media__Workers</c> workers (1 or 2; ffmpeg is CPU bound), and the archive lane (M-4): the
/// <c>ingest-archive</c> queue with one worker and the <c>media-archive</c> queue with <c>Media__ArchiveWorkers</c> workers (0
/// pauses the archive). Like the notices server it does not start without a database, or when
/// <c>BackgroundJobs__RunServer</c> is <c>false</c>.
/// </summary>
/// <param name="services">The root service provider.</param>
/// <param name="configuration">The configuration.</param>
/// <param name="options">The media settings.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class IngestServer(IServiceProvider services, IConfiguration configuration, IOptions<MediaOptions> options, ILogger<IngestServer> logger)
    : IHostedService, IAsyncDisposable
{
    /// <summary>The running servers.</summary>
    private readonly List<BackgroundJobServer> _servers = [];

    /// <summary>Starts the servers when a database is configured and they are not turned off.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue(BotNoticeServer.RunServerSetting, defaultValue: true) || string.IsNullOrWhiteSpace(configuration.GetConnectionString(DependencyInjection.ConnectionStringName)))
        {
            LogNotStarted(logger);
            return Task.CompletedTask;
        }

        Start(IngestJobs.DriveQueue, 2);
        Start(IngestJobs.MediaQueue, Math.Clamp(options.Value.Workers, 1, 2));
        if (options.Value.ArchiveWorkers > 0)
        {
            Start(IngestJobs.DriveArchiveQueue, 1);
            Start(IngestJobs.MediaArchiveQueue, Math.Clamp(options.Value.ArchiveWorkers, 1, 2));
        }

        return Task.CompletedTask;
    }

    /// <summary>Stops both servers and waits for the running jobs.</summary>
    /// <param name="cancellationToken">Gives up waiting when the host stops forcefully.</param>
    /// <returns>A task that completes when the servers have stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var running = _servers.ToList();
        _servers.Clear();
        foreach (var server in running)
        {
            server.SendStop();
        }

        foreach (var server in running)
        {
            await server.WaitForShutdownAsync(cancellationToken);
            server.Dispose();
        }
    }

    /// <summary>Disposes servers that were never stopped.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        _servers.Clear();
        return ValueTask.CompletedTask;
    }

    /// <summary>Starts one server.</summary>
    /// <param name="queue">The queue it serves.</param>
    /// <param name="workers">How many jobs run at once.</param>
    private void Start(string queue, int workers)
    {
        _servers.Add(new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                Queues = [queue],
                WorkerCount = workers,
                ServerName = $"workers-{queue}:{Environment.MachineName}",
                Activator = services.GetRequiredService<JobActivator>(),
            },
            services.GetRequiredService<JobStorage>()));
        LogStarted(logger, queue, workers);
    }

    /// <summary>Logs that a server runs.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Queue">The queue it serves.</param>
    /// <param name="Workers">How many jobs run at once.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Ingest server started on queue {Queue} with {Workers} workers")]
    private static partial void LogStarted(ILogger logger, string Queue, int Workers);

    /// <summary>Logs that the servers did not start.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Ingest servers not started: no database is configured or BackgroundJobs__RunServer is false")]
    private static partial void LogNotStarted(ILogger logger);
}
