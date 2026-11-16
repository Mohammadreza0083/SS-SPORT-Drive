using Ardalis.Specification;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Ingest;

/// <summary>Queues a folder sync for every linked folder (I-6); the recurring job runs it every 10 minutes.</summary>
public sealed record ScheduleFolderSyncsCommand : ICommand<int>;

/// <summary>One page of linked folders after a given id, oldest first, read untracked.</summary>
public sealed class FoldersAfterIdSpec : Specification<DriveFolder>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="afterId">The id of the last folder of the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="take">How many folders to read.</param>
    public FoldersAfterIdSpec(Guid? afterId, int take)
    {
        if (afterId is { } after)
        {
            Query.Where(folder => folder.Id.CompareTo(after) > 0);
        }

        Query.OrderBy(folder => folder.Id).Take(take).AsNoTracking();
    }
}

/// <summary>The files of a player that wait for their copy, read untracked.</summary>
public sealed class QueuedAssetsOfPlayerSpec : Specification<Asset>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="playerId">The player.</param>
    public QueuedAssetsOfPlayerSpec(Guid playerId) =>
        Query.Where(asset => asset.PlayerId == playerId && asset.State == ProcessingState.Queued).OrderBy(asset => asset.SortOrder).AsNoTracking();
}

/// <summary>Handles <see cref="ScheduleFolderSyncsCommand"/>: reads the folders a page at a time and queues each one.</summary>
/// <param name="folders">The linked folders.</param>
/// <param name="scheduler">Queues the syncs.</param>
public sealed class ScheduleFolderSyncsCommandHandler(IRepository<DriveFolder, Guid> folders, IIngestScheduler scheduler)
    : ICommandHandler<ScheduleFolderSyncsCommand, int>
{
    /// <summary>How many folders one read returns (decisions §2b).</summary>
    public const int PageSize = 100;

    /// <summary>Queues a sync for every linked folder, including those whose access was lost, so access can come back.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the round.</param>
    /// <returns>How many syncs were queued.</returns>
    public async Task<int> Handle(ScheduleFolderSyncsCommand request, CancellationToken cancellationToken)
    {
        var queued = 0;
        Guid? after = null;
        while (true)
        {
            var page = await folders.ListAsync(new FoldersAfterIdSpec(after, PageSize), cancellationToken);
            foreach (var folder in page)
            {
                await scheduler.EnqueueAsync(IngestStep.SyncFolder, folder.Id, cancellationToken);
                queued++;
            }

            if (page.Count < PageSize)
            {
                return queued;
            }

            after = page[^1].Id;
        }
    }
}

/// <summary>Syncs a folder as soon as it is linked, instead of waiting for the next 10-minute round.</summary>
/// <param name="scheduler">Queues the sync.</param>
public sealed class SyncLinkedFolder(IIngestScheduler scheduler) : IBackgroundEventHandler<DriveFolderLinked>
{
    /// <summary>Queues the sync.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who linked the folder.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the sync is queued.</returns>
    public Task HandleAsync(DriveFolderLinked domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return scheduler.EnqueueAsync(IngestStep.SyncFolder, domainEvent.FolderId, cancellationToken);
    }
}

/// <summary>
/// Copies a new or replaced file of a published player right away. Files of a player on the waiting list wait for
/// Publish (decisions §5), which queues them all (<see cref="IngestPublishedPlayer"/>).
/// </summary>
/// <param name="players">The players.</param>
/// <param name="scheduler">Queues the copy.</param>
public sealed class QueueAssetIngest(IRepository<Player, Guid> players, IIngestScheduler scheduler)
    : IBackgroundEventHandler<AssetAdded>, IBackgroundEventHandler<AssetReplaced>
{
    /// <summary>Queues the copy of a new file of a published player.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the copy is queued, or nothing needed queueing.</returns>
    public Task HandleAsync(AssetAdded domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return QueueAsync(domainEvent.PlayerId, domainEvent.AssetId, cancellationToken);
    }

    /// <summary>Queues the copy of a replaced file of a published player.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the copy is queued, or nothing needed queueing.</returns>
    public Task HandleAsync(AssetReplaced domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return QueueAsync(domainEvent.PlayerId, domainEvent.AssetId, cancellationToken);
    }

    /// <summary>Queues the copy when the player is published.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the copy is queued, or nothing needed queueing.</returns>
    private async Task QueueAsync(Guid playerId, Guid assetId, CancellationToken cancellationToken)
    {
        if (await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(playerId), cancellationToken) is { IsPublished: true } player)
        {
            await scheduler.EnqueueAssetAsync(IngestStep.CopyOriginal, assetId, IngestLanes.For(player), cancellationToken);
        }
    }
}

/// <summary>
/// Publishing starts ingest: it queues the copy of every waiting file on the player's lane (M-4), the QR code and the card.
/// </summary>
/// <param name="players">The players, for the lane.</param>
/// <param name="assets">The assets.</param>
/// <param name="scheduler">Queues the steps.</param>
public sealed class IngestPublishedPlayer(IRepository<Player, Guid> players, IRepository<Asset, Guid> assets, IIngestScheduler scheduler) : IBackgroundEventHandler<PlayerPublished>
{
    /// <summary>Queues the player's ingest.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who published.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when everything is queued.</returns>
    public async Task HandleAsync(PlayerPublished domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(domainEvent.PlayerId), cancellationToken);
        var lane = player is null ? IngestLane.Live : IngestLanes.For(player);
        foreach (var asset in await assets.ListAsync(new QueuedAssetsOfPlayerSpec(domainEvent.PlayerId), cancellationToken))
        {
            await scheduler.EnqueueAssetAsync(IngestStep.CopyOriginal, asset.Id, lane, cancellationToken);
        }

        await scheduler.EnqueueAsync(IngestStep.RenderQrCode, domainEvent.PlayerId, cancellationToken);
        await scheduler.EnqueueAsync(IngestStep.RenderCard, domainEvent.PlayerId, cancellationToken);
    }
}

/// <summary>A published player's changed details (for example a new name) remake the QR code and the card.</summary>
/// <param name="players">The players.</param>
/// <param name="scheduler">Queues the steps.</param>
public sealed class RefreshPlayerImages(IRepository<Player, Guid> players, IIngestScheduler scheduler) : IBackgroundEventHandler<PlayerDetailsChanged>
{
    /// <summary>Queues the QR code and the card when the player is published.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who changed the player.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the steps are queued, or nothing needed queueing.</returns>
    public async Task HandleAsync(PlayerDetailsChanged domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(domainEvent.PlayerId), cancellationToken) is not { IsPublished: true })
        {
            return;
        }

        await scheduler.EnqueueAsync(IngestStep.RenderQrCode, domainEvent.PlayerId, cancellationToken);
        await scheduler.EnqueueAsync(IngestStep.RenderCard, domainEvent.PlayerId, cancellationToken);
    }
}

/// <summary>Every new photo is cut out, so the cut-out is ready when the player goes live.</summary>
/// <param name="scheduler">Queues the cut-out.</param>
public sealed class CutOutNewPhoto(IIngestScheduler scheduler) : IBackgroundEventHandler<PlayerPhotoChanged>
{
    /// <summary>Queues the cut-out.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who changed the photo.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the cut-out is queued.</returns>
    public Task HandleAsync(PlayerPhotoChanged domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return scheduler.EnqueueAsync(IngestStep.CutOutPhoto, domainEvent.PlayerId, cancellationToken);
    }
}
