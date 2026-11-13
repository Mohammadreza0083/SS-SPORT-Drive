using Ardalis.Specification;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Ingest;

/// <summary>What one sync of a Drive folder did.</summary>
public enum FolderSyncOutcome
{
    /// <summary>The folder was read and the player's files follow it.</summary>
    Synced,

    /// <summary>Drive no longer lets the service account read the folder; nothing else changed.</summary>
    AccessLost,

    /// <summary>The folder is no longer linked (it was unlinked after the sync was queued).</summary>
    NotLinked,
}

/// <summary>The counts of one folder sync.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Added">New files added.</param>
/// <param name="Renamed">Files renamed in Drive.</param>
/// <param name="Replaced">Files with new content.</param>
/// <param name="Removed">Files gone from Drive, now hidden.</param>
/// <param name="Returned">Removed files that are back in Drive.</param>
/// <param name="Skipped">Files left out: unsupported types, empty files and files without an md5.</param>
public sealed record FolderSyncResult(FolderSyncOutcome Outcome, int Added, int Renamed, int Replaced, int Removed, int Returned, int Skipped)
{
    /// <summary>Builds the result of a sync that read nothing.</summary>
    /// <param name="outcome">Why nothing was read.</param>
    /// <returns>The result, with all counts at zero.</returns>
    public static FolderSyncResult Nothing(FolderSyncOutcome outcome) => new(outcome, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Syncs one linked Drive folder (I-6): new, renamed, replaced, removed and returned files, by the D-6 rules. Run by a
/// background job, every 10 minutes and when a folder is linked.
/// </summary>
/// <param name="FolderId">The linked folder.</param>
[InternalRequest("Sent only by the folder-sync job, with the id of a linked folder.")]
public sealed record SyncDriveFolderCommand(Guid FolderId) : ICommand<FolderSyncResult>;

/// <summary>A player's assets, tracked, for a folder sync.</summary>
public sealed class PlayerAssetsForSyncSpec : Specification<Asset>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="playerId">The player.</param>
    public PlayerAssetsForSyncSpec(Guid playerId) => Query.Where(asset => asset.PlayerId == playerId).Include(asset => asset.Renditions);
}

/// <summary>Handles <see cref="SyncDriveFolderCommand"/>.</summary>
/// <param name="folders">The linked folders.</param>
/// <param name="assets">The assets.</param>
/// <param name="lister">Lists the folder in Drive.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class SyncDriveFolderCommandHandler(
    IRepository<DriveFolder, Guid> folders,
    IRepository<Asset, Guid> assets,
    IDriveFolderLister lister,
    TimeProvider timeProvider) : ICommandHandler<SyncDriveFolderCommand, FolderSyncResult>
{
    /// <summary>Reads the folder in Drive and brings the player's files in line with it.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the sync.</param>
    /// <returns>What changed.</returns>
    /// <exception cref="Exceptions.ServiceUnavailableException"><c>drive.unavailable</c>: the job fails and is retried; nothing changes.</exception>
    public async Task<FolderSyncResult> Handle(SyncDriveFolderCommand request, CancellationToken cancellationToken)
    {
        if (await folders.GetByIdAsync(request.FolderId, cancellationToken) is not { } folder)
        {
            return FolderSyncResult.Nothing(FolderSyncOutcome.NotLinked);
        }

        var now = timeProvider.GetUtcNow();
        var listing = await lister.ListAsync(folder.DriveFolderId, cancellationToken);
        if (listing.Status != DriveFolderStatus.Found)
        {
            folder.MarkAccessLost(now);
            return FolderSyncResult.Nothing(FolderSyncOutcome.AccessLost);
        }

        var known = await assets.ListAsync(new PlayerAssetsForSyncSpec(folder.PlayerId), cancellationToken);
        var sync = new Sync(folder, known, assets, now);
        foreach (var entry in listing.Files)
        {
            sync.Apply(entry);
        }

        // A listing cut off at the file limit may miss files, so it never proves that a file was removed.
        if (!listing.IsTruncated)
        {
            sync.HideMissing();
        }

        if (listing.Name is { Length: > 0 } name && name != folder.Name)
        {
            folder.Rename(name);
        }

        folder.MarkSynced(now);
        return sync.Result(listing.SkippedCount);
    }

    /// <summary>The state of one run: which assets the listing touched and the counts so far.</summary>
    private sealed class Sync
    {
        /// <summary>The folder.</summary>
        private readonly DriveFolder _folder;

        /// <summary>The player's assets.</summary>
        private readonly List<Asset> _known;

        /// <summary>The assets repository.</summary>
        private readonly IRepository<Asset, Guid> _assets;

        /// <summary>The time of the sync.</summary>
        private readonly DateTimeOffset _now;

        /// <summary>The assets the listing mentioned.</summary>
        private readonly HashSet<Guid> _seen = [];

        /// <summary>The next position on the player's page.</summary>
        private int _nextSortOrder;

        /// <summary>Files added.</summary>
        private int _added;

        /// <summary>Files renamed.</summary>
        private int _renamed;

        /// <summary>Files replaced.</summary>
        private int _replaced;

        /// <summary>Files removed.</summary>
        private int _removed;

        /// <summary>Files returned.</summary>
        private int _returned;

        /// <summary>Files skipped.</summary>
        private int _skipped;

        /// <summary>Initializes the run.</summary>
        /// <param name="folder">The folder.</param>
        /// <param name="known">The player's assets.</param>
        /// <param name="assets">The assets repository.</param>
        /// <param name="now">The time of the sync.</param>
        public Sync(DriveFolder folder, IReadOnlyList<Asset> known, IRepository<Asset, Guid> assets, DateTimeOffset now)
        {
            _folder = folder;
            _known = [.. known];
            _assets = assets;
            _now = now;
            _nextSortOrder = known.Count == 0 ? 0 : known.Max(asset => asset.SortOrder) + 1;
        }

        /// <summary>Applies one listed file: adds it, or follows its rename, replacement or return.</summary>
        /// <param name="entry">The listed file.</param>
        public void Apply(DriveFileEntry entry)
        {
            if (entry.Md5 is null)
            {
                _skipped++;
                return;
            }

            AssetFile file;
            try
            {
                file = AssetFile.Create(entry.Name, entry.Md5, entry.SizeBytes);
            }
            catch (DomainException)
            {
                _skipped++;
                return;
            }

            var asset = Match(entry.FileId);
            if (asset is null)
            {
                Add(entry, file);
                return;
            }

            _seen.Add(asset.Id);
            if (asset.RemovedFromSourceAt is not null)
            {
                asset.ReturnToSource();
                _returned++;
            }

            if (file.Md5 != asset.File.Md5)
            {
                Replace(asset, entry, file);
            }
            else if (file.OriginalName != asset.File.OriginalName)
            {
                asset.RenameInSource(file.OriginalName, AssetTagger.Classify(entry.Subfolders, entry.Name).Title);
                _renamed++;
            }
        }

        /// <summary>Hides the files of this folder that Drive no longer lists. Nothing is ever deleted.</summary>
        public void HideMissing()
        {
            foreach (var asset in _known.Where(a => a.FolderId == _folder.Id && a.Source == AssetSource.Drive && a.RemovedFromSourceAt is null && !_seen.Contains(a.Id)))
            {
                asset.MarkRemovedFromSource(_now);
                _removed++;
            }
        }

        /// <summary>Builds the result.</summary>
        /// <param name="listingSkipped">How many files the Drive listing itself left out.</param>
        /// <returns>The result.</returns>
        public FolderSyncResult Result(int listingSkipped) =>
            new(FolderSyncOutcome.Synced, _added, _renamed, _replaced, _removed, _returned, _skipped + listingSkipped);

        /// <summary>Finds the asset of a Drive file in this player's Drive files: the live one, else the newest.</summary>
        /// <param name="driveFileId">The Drive file id.</param>
        /// <returns>The asset, or <see langword="null"/>.</returns>
        private Asset? Match(string driveFileId) =>
            _known
                .Where(asset => asset.Source == AssetSource.Drive && asset.DriveFileId == driveFileId)
                .OrderBy(asset => asset.RemovedFromSourceAt is null ? 0 : 1)
                .ThenByDescending(asset => asset.CreatedAt)
                .FirstOrDefault();

        /// <summary>Adds a new file after the player's last one.</summary>
        /// <param name="entry">The listed file.</param>
        /// <param name="file">The validated file.</param>
        private void Add(DriveFileEntry entry, AssetFile file)
        {
            var classification = AssetTagger.Classify(entry.Subfolders, entry.Name);
            var asset = Asset.Add(_folder.PlayerId, _folder.Id, AssetSource.Drive, entry.FileId, file, classification.Title, classification.Tags, _nextSortOrder++, _now);
            _assets.Add(asset);
            _known.Add(asset);
            _seen.Add(asset.Id);
            _added++;
        }

        /// <summary>Follows a file whose content changed; a file that became another kind is a new asset.</summary>
        /// <param name="asset">The asset.</param>
        /// <param name="entry">The listed file.</param>
        /// <param name="file">The validated new file.</param>
        private void Replace(Asset asset, DriveFileEntry entry, AssetFile file)
        {
            try
            {
                asset.ReplaceContent(file);
                _replaced++;
            }
            catch (DomainException exception) when (exception.Code == "asset.kind_changed")
            {
                asset.MarkRemovedFromSource(_now);
                Add(entry, file);
            }
        }
    }
}
