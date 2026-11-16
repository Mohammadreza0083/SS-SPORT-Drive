namespace SsSport.Drive.Application.Abstractions.Drive;

/// <summary>
/// Browses the Shared Drive's folder tree for the archive scan (M-1): the root's children, then each category folder's
/// children. Player folders themselves are listed with <see cref="IDriveFolderLister"/>.
/// </summary>
public interface IDriveArchiveBrowser
{
    /// <summary>Lists the direct children of the Shared Drive's root.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The child folders and how many loose files sit next to them.</returns>
    /// <exception cref="Exceptions.ServiceUnavailableException"><c>drive.unavailable</c> when Drive cannot be reached or is not configured.</exception>
    Task<DriveChildren> ListRootAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the direct children of a folder.</summary>
    /// <param name="folderId">The Drive folder id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The child folders and how many loose files sit next to them.</returns>
    /// <exception cref="Exceptions.ServiceUnavailableException"><c>drive.unavailable</c> when Drive cannot be reached or is not configured.</exception>
    Task<DriveChildren> ListFolderAsync(string folderId, CancellationToken cancellationToken = default);
}

/// <summary>A folder's direct children.</summary>
/// <param name="Folders">The child folders.</param>
/// <param name="FileCount">How many files sit directly in the folder (not in its subfolders).</param>
public sealed record DriveChildren(IReadOnlyList<DriveSubfolder> Folders, int FileCount);

/// <summary>A child folder.</summary>
/// <param name="Id">The Drive folder id.</param>
/// <param name="Name">The folder name.</param>
public sealed record DriveSubfolder(string Id, string Name);
