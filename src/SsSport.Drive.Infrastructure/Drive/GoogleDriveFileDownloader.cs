using Google;
using Google.Apis.Auth.OAuth2.Responses;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Infrastructure.Drive;

/// <summary>
/// <see cref="IDriveFileDownloader"/> over <see cref="IDriveApi"/>. A file Drive no longer has is <see langword="null"/>;
/// a network, Google or credentials failure is <c>drive.unavailable</c>, so the job is retried later (like the folder reads).
/// </summary>
/// <param name="api">The Drive API.</param>
public sealed class GoogleDriveFileDownloader(IDriveApi api) : IDriveFileDownloader
{
    /// <inheritdoc/>
    public async Task<Stream?> OpenAsync(string fileId, CancellationToken cancellationToken)
    {
        try
        {
            return await api.DownloadAsync(fileId, cancellationToken);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            throw new ServiceUnavailableException("drive.unavailable", "Google Drive cannot be reached right now. Try again in a moment.");
        }
    }

    /// <summary>Tells a Drive outage from a bug or a cancellation.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns><see langword="true"/> when Drive could not be used.</returns>
    private static bool IsOutage(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        HttpRequestException or TimeoutException or IOException or GoogleApiException or TokenResponseException or DriveConfigurationException => true,
        _ => false,
    };
}
