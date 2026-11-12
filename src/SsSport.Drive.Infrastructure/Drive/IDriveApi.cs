namespace SsSport.Drive.Infrastructure.Drive;

/// <summary>
/// The two Drive API calls the folder check needs, behind a seam so the walking and counting logic can be tested
/// without Google. <see cref="GoogleDriveApi"/> is the real one.
/// </summary>
public interface IDriveApi
{
    /// <summary>Reads one file or folder.</summary>
    /// <param name="id">The Drive id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The item, or <see langword="null"/> when Drive says it does not exist for the service account.</returns>
    Task<DriveItem?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>Lists one page of a folder's direct, non-trashed children.</summary>
    /// <param name="folderId">The folder id.</param>
    /// <param name="pageToken">The token of the page to read, or <see langword="null"/> for the first.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page.</returns>
    Task<DrivePage> ListChildrenAsync(string folderId, string? pageToken, CancellationToken cancellationToken);
}

/// <summary>A Drive file or folder.</summary>
/// <param name="Id">The Drive id.</param>
/// <param name="Name">The name.</param>
/// <param name="MimeType">The MIME type; folders have <see cref="FolderMimeType"/>.</param>
/// <param name="Size">The size in bytes, or <see langword="null"/> for folders and Google Docs.</param>
/// <param name="DriveId">The Shared Drive it lives in, or <see langword="null"/> for My Drive.</param>
/// <param name="Trashed">Whether it is in the trash.</param>
/// <param name="Md5Checksum">The md5 checksum of a binary file, or <see langword="null"/> for folders and Google Docs.</param>
/// <param name="ModifiedTime">When the item last changed, if Drive says.</param>
public sealed record DriveItem(
    string Id,
    string Name,
    string MimeType,
    long? Size,
    string? DriveId,
    bool Trashed = false,
    string? Md5Checksum = null,
    DateTimeOffset? ModifiedTime = null)
{
    /// <summary>Drive's MIME type for folders.</summary>
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>Gets a value indicating whether the item is a folder.</summary>
    public bool IsFolder => MimeType == FolderMimeType;
}

/// <summary>One page of a folder listing.</summary>
/// <param name="Items">The items on the page.</param>
/// <param name="NextPageToken">The token of the next page, or <see langword="null"/> on the last one.</param>
public sealed record DrivePage(IReadOnlyList<DriveItem> Items, string? NextPageToken);
