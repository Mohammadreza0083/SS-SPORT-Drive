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
/// The live Drive folder check (T-4c): the folder must be a non-trashed folder inside the agency's Shared Drive; its
/// tree is walked breadth-first and every file counted by kind with the same rules as ingest (<see cref="AssetFile"/>).
/// Large trees stop at <see cref="MaxItems"/> files or <see cref="MaxDepth"/> levels and say so.
/// </summary>
public sealed partial class GoogleDriveFolderReader : IDriveFolderReader
{
    /// <summary>The most files counted before the check stops (a player folder holds a few dozen).</summary>
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
    public async Task<DriveFolderSummary> ReadAsync(string folderId, CancellationToken cancellationToken = default)
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
                return DriveFolderSummary.Missing(DriveFolderStatus.NotFound);
            }

            if (!folder.IsFolder)
            {
                return DriveFolderSummary.Missing(DriveFolderStatus.NotAFolder);
            }

            if (!string.Equals(folder.DriveId, _options.SharedDriveId, StringComparison.Ordinal))
            {
                return DriveFolderSummary.Missing(DriveFolderStatus.OutsideSharedDrive);
            }

            return await CountAsync(folder, cancellationToken);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            LogUnreachable(_logger, exception, exception.GetType().Name);
            throw Unavailable();
        }
    }

    /// <summary>Walks the folder tree breadth-first and counts its files by kind.</summary>
    /// <param name="root">The player folder.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The summary.</returns>
    private async Task<DriveFolderSummary> CountAsync(DriveItem root, CancellationToken cancellationToken)
    {
        int videos = 0, documents = 0, scouts = 0, others = 0;
        long bytes = 0;
        var truncated = false;
        var pending = new Queue<(string Id, int Depth)>([(root.Id, 0)]);
        while (pending.Count > 0 && !truncated)
        {
            var (folderId, depth) = pending.Dequeue();
            string? pageToken = null;
            do
            {
                var page = await _api.ListChildrenAsync(folderId, pageToken, cancellationToken);
                foreach (var item in page.Items)
                {
                    if (item.IsFolder)
                    {
                        if (depth + 1 < MaxDepth)
                        {
                            pending.Enqueue((item.Id, depth + 1));
                        }

                        continue;
                    }

                    if (videos + documents + scouts + others >= MaxItems)
                    {
                        truncated = true;
                        break;
                    }

                    bytes += item.Size ?? 0;
                    switch (AssetFile.TryDetectKind(item.Name, out var kind) ? kind : (AssetKind?)null)
                    {
                        case AssetKind.Video: videos++; break;
                        case AssetKind.Document: documents++; break;
                        case AssetKind.Scout: scouts++; break;
                        default: others++; break;
                    }
                }

                pageToken = truncated ? null : page.NextPageToken;
            }
            while (pageToken is not null);
        }

        return new DriveFolderSummary(DriveFolderStatus.Found, root.Name, videos, documents, scouts, others, bytes, truncated);
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

    /// <summary>Logs an outage (the kind of failure only; no ids, names or credentials).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="FailureType">The exception type.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Google Drive could not be reached ({FailureType})")]
    private static partial void LogUnreachable(ILogger logger, Exception exception, string FailureType);
}
