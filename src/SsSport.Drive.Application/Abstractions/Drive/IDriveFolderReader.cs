namespace SsSport.Drive.Application.Abstractions.Drive;

/// <summary>
/// Reads a Google Drive folder with the service account (T-4c): is it a usable player folder in the agency's Shared
/// Drive, what is it called, and what is in it.
/// </summary>
public interface IDriveFolderReader
{
    /// <summary>Reads a folder and counts what is inside it, subfolders included.</summary>
    /// <param name="folderId">The Drive folder id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The folder's summary, or why it cannot be used.</returns>
    /// <exception cref="Exceptions.ServiceUnavailableException"><c>drive.unavailable</c> when Drive cannot be reached or is not configured.</exception>
    Task<DriveFolderSummary> ReadAsync(string folderId, CancellationToken cancellationToken = default);
}

/// <summary>What Drive says about a folder.</summary>
public enum DriveFolderStatus
{
    /// <summary>A readable folder in the agency's Shared Drive.</summary>
    Found,

    /// <summary>Missing, trashed, or not shared with the service account (Drive does not tell these apart).</summary>
    NotFound,

    /// <summary>A folder, but not in the agency's Shared Drive (owner, 2026-10-29: refused).</summary>
    OutsideSharedDrive,

    /// <summary>The link points to a file, not a folder.</summary>
    NotAFolder,
}

/// <summary>A Drive folder and what is in it.</summary>
/// <param name="Status">Whether the folder can be used.</param>
/// <param name="Name">The folder name, when found.</param>
/// <param name="VideoCount">Videos (mp4, mov, m4v).</param>
/// <param name="DocumentCount">Documents (pdf, png, jpg).</param>
/// <param name="ScoutCount">DataVolley scout files (dvw).</param>
/// <param name="OtherCount">Files the player page will not show.</param>
/// <param name="TotalBytes">The size of every counted file.</param>
/// <param name="IsTruncated">Whether counting stopped at the limit, so the counts are a lower bound.</param>
public sealed record DriveFolderSummary(
    DriveFolderStatus Status,
    string? Name,
    int VideoCount,
    int DocumentCount,
    int ScoutCount,
    int OtherCount,
    long TotalBytes,
    bool IsTruncated)
{
    /// <summary>Builds the summary of a folder that cannot be used.</summary>
    /// <param name="status">Why.</param>
    /// <returns>The summary.</returns>
    public static DriveFolderSummary Missing(DriveFolderStatus status) => new(status, null, 0, 0, 0, 0, 0, false);
}
