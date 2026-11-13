using System.Security.Cryptography;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Ingest;

/// <summary>
/// Stores an asset's original in the media bucket (I-2) and queues its processing. An original already stored is reused,
/// one the rclone backup holds is copied inside storage, and anything else is downloaded from Drive and checked against
/// the md5 Drive listed.
/// </summary>
/// <param name="AssetId">The asset.</param>
public sealed record CopyOriginalCommand(Guid AssetId) : ICommand;

/// <summary>Handles <see cref="CopyOriginalCommand"/>.</summary>
/// <param name="assets">The assets.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="backup">The read-only rclone backup.</param>
/// <param name="drive">Downloads from Google Drive.</param>
/// <param name="workspace">Hands out the work folder of the download.</param>
/// <param name="scheduler">Queues the processing.</param>
public sealed class CopyOriginalCommandHandler(
    IRepository<Asset, Guid> assets,
    IObjectStorage storage,
    IBackupArchive backup,
    IDriveFileDownloader drive,
    IWorkspace workspace,
    IIngestScheduler scheduler) : ICommandHandler<CopyOriginalCommand>
{
    /// <summary>Stores the original, or fails the asset with the reason it cannot be stored.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>A task that completes when the asset is processing or failed.</returns>
    /// <exception cref="Exceptions.ServiceUnavailableException"><c>drive.unavailable</c>: the job fails and is retried; nothing changes.</exception>
    public async Task Handle(CopyOriginalCommand request, CancellationToken cancellationToken)
    {
        // A job may run twice: only an asset that still waits for its copy is worked on.
        if (await assets.GetByIdAsync(request.AssetId, cancellationToken) is not { State: ProcessingState.Queued } asset)
        {
            return;
        }

        var key = OriginalKeys.For(asset.File);
        if (await storage.ExistsAsync(key, cancellationToken))
        {
            await AttachAsync(asset, key, cancellationToken);
        }
        else if (await backup.FindByMd5Async(asset.File.Md5, cancellationToken) is { } backupKey)
        {
            await backup.CopyToMediaAsync(backupKey, key, cancellationToken);
            await AttachAsync(asset, key, cancellationToken);
        }
        else if (asset.DriveFileId is null)
        {
            asset.Fail("The original was not found in storage or the backup.");
        }
        else
        {
            asset.StartCopying();
            await DownloadAsync(asset, key, cancellationToken);
        }
    }

    /// <summary>Downloads the file from Drive, checks its md5 and stores it.</summary>
    /// <param name="asset">The asset, copying.</param>
    /// <param name="key">The key of the original.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when the asset is processing or failed.</returns>
    private async Task DownloadAsync(Asset asset, string key, CancellationToken cancellationToken)
    {
        using var folder = workspace.Create();
        var path = Path.Combine(folder.Path, "original");
        string md5;
        await using (var content = await drive.OpenAsync(asset.DriveFileId!, cancellationToken))
        {
            if (content is null)
            {
                asset.Fail("The file is no longer in Google Drive.");
                return;
            }

            md5 = await SaveAsync(content, path, cancellationToken);
        }

        if (md5 != asset.File.Md5)
        {
            asset.Fail("The file Drive returned does not match the md5 it listed, so it was not stored.");
            return;
        }

        await storage.UploadFileAsync(path, key, OriginalKeys.ContentType(asset.File.OriginalName), cancellationToken);
        await AttachAsync(asset, key, cancellationToken);
    }

    /// <summary>Writes a stream to a file and computes its md5 on the way.</summary>
    /// <param name="content">The stream.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The lower-case md5 of the content.</returns>
    private static async Task<string> SaveAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Records the stored original and queues the processing.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="key">The key of the original.</param>
    /// <param name="cancellationToken">Cancels the enqueueing.</param>
    /// <returns>A task that completes when the processing is queued.</returns>
    private async Task AttachAsync(Asset asset, string key, CancellationToken cancellationToken)
    {
        asset.AttachOriginal(StorageKey.Create(key));
        await scheduler.EnqueueAsync(IngestStep.ProcessAsset, asset.Id, cancellationToken);
    }
}
