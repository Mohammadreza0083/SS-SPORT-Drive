using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Infrastructure.Drive;

/// <summary>
/// Reads player folders in the agency's Shared Drive. The live folder check (T-4c) counts files by kind; the ingest
/// listing (I-1) returns every usable file with its subfolder path, size, md5 and change time. Both check the folder
/// the same way (a non-trashed folder inside the Shared Drive) and share one breadth-first walk, which stops at
/// <see cref="MaxItems"/> files or <see cref="MaxDepth"/> levels and says so.
/// </summary>
public sealed partial class GoogleDriveFolderReader : IDriveFolderReader, IDriveFolderLister, IDriveArchiveBrowser
{
    /// <summary>The most children one folder listing reads for the archive scan (M-1); the archive has about a hundred player folders.</summary>
    public const int MaxChildren = 5000;

    /// <summary>The most files read before the walk stops (a player folder holds a few dozen).</summary>
    public const int MaxItems = 2000;

    /// <summary>The deepest subfolder level walked (archive folders nest two levels at most).</summary>
    public const int MaxDepth = 6;

    /// <summary>The Drive API.</summary>
    private readonly IDriveApi _api;

    /// <summary>The settings.</summary>
    private readonly GoogleDriveOptions _options;

    /// <summary>The logger.</summary>
    private readonly ILogger<GoogleDriveFolderReader> _logger;

    /// <summary>Initializes the reader.</summary>
    /// <param name="api">The Drive API.</param>
    /// <param name="options">The Drive settings.</param>
    /// <param name="logger">The logger; tests may leave it out.</param>
    public GoogleDriveFolderReader(IDriveApi api, IOptions<GoogleDriveOptions> options, ILogger<GoogleDriveFolderReader>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _api = api;
        _options = options.Value;
        _logger = logger ?? NullLogger<GoogleDriveFolderReader>.Instance;
    }

    /// <inheritdoc/>
    public Task<DriveFolderSummary> ReadAsync(string folderId, CancellationToken cancellationToken = default) =>
        WithFolderAsync(folderId, DriveFolderSummary.Missing, CountAsync, cancellationToken);

    /// <inheritdoc/>
    public Task<DriveFolderListing> ListAsync(string folderId, CancellationToken cancellationToken = default) =>
        WithFolderAsync(folderId, DriveFolderListing.Missing, CollectAsync, cancellationToken);

    /// <inheritdoc/>
    public Task<DriveChildren> ListRootAsync(CancellationToken cancellationToken = default) => ChildrenAsync(null, cancellationToken);

    /// <inheritdoc/>
    public Task<DriveChildren> ListFolderAsync(string folderId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);
        return ChildrenAsync(folderId, cancellationToken);
    }

    /// <summary>
    /// Lists a folder's direct children, every page, up to <see cref="MaxChildren"/>. The Shared Drive's id is also the id of
    /// its root folder. Trashed items are left out; any outage is <c>drive.unavailable</c>.
    /// </summary>
    /// <param name="folderId">The folder, or <see langword="null"/> for the Shared Drive's root.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The child folders and the number of loose files.</returns>
    private async Task<DriveChildren> ChildrenAsync(string? folderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SharedDriveId))
        {
            LogNotConfigured(_logger);
            throw Unavailable();
        }

        try
        {
            var folders = new List<DriveSubfolder>();
            var files = 0;
            string? pageToken = null;
            do
            {
                var page = await _api.ListChildrenAsync(folderId ?? _options.SharedDriveId, pageToken, cancellationToken);
                foreach (var item in page.Items.Where(item => !item.Trashed))
                {
                    if (item.IsFolder)
                    {
                        folders.Add(new DriveSubfolder(item.Id, item.Name));
                    }
                    else
                    {
                        files++;
                    }
                }

                pageToken = folders.Count + files >= MaxChildren ? null : page.NextPageToken;
            }
            while (pageToken is not null);

            return new DriveChildren(folders, files);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            LogUnreachable(_logger, exception, exception.GetType().Name);
            throw Unavailable();
        }
    }

    /// <summary>
    /// Checks that the id is a non-trashed folder in the agency's Shared Drive and, if so, reads it. Any Drive outage on
    /// the way becomes <c>drive.unavailable</c>, so a partial read is never returned as a whole one.
    /// </summary>
    /// <typeparam name="TResult">What is read.</typeparam>
    /// <param name="folderId">The Drive folder id.</param>
    /// <param name="missing">Builds the result for a folder that cannot be used.</param>
    /// <param name="read">Reads a usable folder.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The result.</returns>
    private async Task<TResult> WithFolderAsync<TResult>(
        string folderId,
        Func<DriveFolderStatus, TResult> missing,
        Func<DriveItem, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SharedDriveId))
        {
            LogNotConfigured(_logger);
            throw Unavailable();
        }

        try
        {
            var folder = await _api.GetAsync(folderId, cancellationToken);
            if (folder is null || folder.Trashed)
            {
                return missing(DriveFolderStatus.NotFound);
            }

            if (!folder.IsFolder)
            {
                return missing(DriveFolderStatus.NotAFolder);
            }

            if (!string.Equals(folder.DriveId, _options.SharedDriveId, StringComparison.Ordinal))
            {
                return missing(DriveFolderStatus.OutsideSharedDrive);
            }

            return await read(folder, cancellationToken);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            LogUnreachable(_logger, exception, exception.GetType().Name);
            throw Unavailable();
        }
    }

    /// <summary>Counts the files of a folder tree by kind (T-4c).</summary>
    /// <param name="root">The player folder.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The summary.</returns>
    private async Task<DriveFolderSummary> CountAsync(DriveItem root, CancellationToken cancellationToken)
    {
        int videos = 0, documents = 0, scouts = 0, others = 0;
        long bytes = 0;
        var truncated = await WalkAsync(
            root,
            (item, _) =>
            {
                bytes += item.Size ?? 0;
                switch (AssetFile.TryDetectKind(item.Name, out var kind) ? kind : (AssetKind?)null)
                {
                    case AssetKind.Video: videos++; break;
                    case AssetKind.Document: documents++; break;
                    case AssetKind.Scout: scouts++; break;
                    default: others++; break;
                }
            },
            cancellationToken);

        return new DriveFolderSummary(DriveFolderStatus.Found, root.Name, videos, documents, scouts, others, bytes, truncated);
    }

    /// <summary>Lists the ingestible files of a folder tree with their subfolder path (I-1).</summary>
    /// <param name="root">The player folder.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The listing.</returns>
    private async Task<DriveFolderListing> CollectAsync(DriveItem root, CancellationToken cancellationToken)
    {
        var files = new List<DriveFileEntry>();
        var skipped = 0;
        var truncated = await WalkAsync(
            root,
            (item, subfolders) =>
            {
                if (item.Size is not > 0 || !AssetFile.TryDetectKind(item.Name, out var kind))
                {
                    skipped++;
                    return;
                }

                files.Add(new DriveFileEntry(
                    item.Id,
                    item.Name,
                    kind,
                    item.Size.Value,
                    string.IsNullOrWhiteSpace(item.Md5Checksum) ? null : item.Md5Checksum.Trim().ToLowerInvariant(),
                    item.ModifiedTime,
                    subfolders));
            },
            cancellationToken);

        if (truncated)
        {
            LogListingTruncated(_logger, root.Id, MaxItems);
        }

        return new DriveFolderListing(DriveFolderStatus.Found, root.Name, files, skipped, truncated);
    }

    /// <summary>
    /// Walks a folder tree breadth-first, every page of every subfolder, and hands each file to
    /// <paramref name="onFile"/> with the subfolder names from the root down to it. Stops after
    /// <see cref="MaxItems"/> files and does not go below <see cref="MaxDepth"/> levels.
    /// </summary>
    /// <param name="root">The folder to walk.</param>
    /// <param name="onFile">Called for each file with its subfolder path.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns><see langword="true"/> when the walk stopped at the file limit.</returns>
    private async Task<bool> WalkAsync(DriveItem root, Action<DriveItem, IReadOnlyList<string>> onFile, CancellationToken cancellationToken)
    {
        var seen = 0;
        var pending = new Queue<(string Id, IReadOnlyList<string> Path)>();
        pending.Enqueue((root.Id, []));
        while (pending.Count > 0)
        {
            var (folderId, path) = pending.Dequeue();
            string? pageToken = null;
            do
            {
                var page = await _api.ListChildrenAsync(folderId, pageToken, cancellationToken);
                foreach (var item in page.Items)
                {
                    if (item.IsFolder)
                    {
                        if (path.Count + 1 < MaxDepth)
                        {
                            pending.Enqueue((item.Id, [.. path, item.Name]));
                        }

                        continue;
                    }

                    if (seen >= MaxItems)
                    {
                        return true;
                    }

                    seen++;
                    onFile(item, path);
                }

                pageToken = page.NextPageToken;
            }
            while (pageToken is not null);
        }

        return false;
    }

    /// <summary>Tells a Drive outage (network, timeout, Google error, bad credentials) from a bug or a cancellation.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns><see langword="true"/> when Drive could not be used.</returns>
    private static bool IsOutage(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        HttpRequestException or TimeoutException or IOException or GoogleApiException or TokenResponseException or DriveConfigurationException => true,
        _ => false,
    };

    /// <summary>Builds the error the API turns into 503.</summary>
    /// <returns>The exception.</returns>
    private static ServiceUnavailableException Unavailable() =>
        new("drive.unavailable", "Google Drive cannot be reached right now. Try again in a moment.");

    /// <summary>Logs a missing Shared Drive id.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Google Drive is not configured: set GoogleDrive__SharedDriveId and GoogleDrive__ServiceAccountKeyPath")]
    private static partial void LogNotConfigured(ILogger logger);

    /// <summary>Logs a listing that stopped at the file limit, so files may be missing from ingest.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="FolderId">The Drive folder id.</param>
    /// <param name="Limit">The file limit.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Drive folder {FolderId} holds more than {Limit} files; the listing is incomplete")]
    private static partial void LogListingTruncated(ILogger logger, string FolderId, int Limit);

    /// <summary>Logs an outage (the kind of failure only; no ids, names or credentials).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="FailureType">The exception type.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Google Drive could not be reached ({FailureType})")]
    private static partial void LogUnreachable(ILogger logger, Exception exception, string FailureType);
}
