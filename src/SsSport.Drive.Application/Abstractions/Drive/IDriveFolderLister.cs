using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.Abstractions.Drive;

/// <summary>
/// Lists every ingestible file in a player folder, subfolders included (I-1). Ingest (I-2/I-3) and the sync poller
/// (I-6) work from this listing; the folder checks are the same as <see cref="IDriveFolderReader"/>.
/// </summary>
public interface IDriveFolderLister
{
    /// <summary>Lists the files of a folder and its subfolders.</summary>
    /// <param name="folderId">The Drive folder id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The listing, or why the folder cannot be used.</returns>
    /// <exception cref="Exceptions.ServiceUnavailableException"><c>drive.unavailable</c> when Drive cannot be reached or is not configured.</exception>
    Task<DriveFolderListing> ListAsync(string folderId, CancellationToken cancellationToken = default);
}

/// <summary>One file in a player folder that ingest can use.</summary>
/// <param name="FileId">The Drive file id.</param>
/// <param name="Name">The file name as it is in Drive.</param>
/// <param name="Kind">The kind, from the extension (<see cref="AssetFile"/> rules).</param>
/// <param name="SizeBytes">The size in bytes (always above zero).</param>
/// <param name="Md5">The md5 checksum Drive reports, lower-case hex, or <see langword="null"/> when Drive gives none.</param>
/// <param name="ModifiedAt">When Drive last saw the file change, if known.</param>
/// <param name="Subfolders">The subfolder names from the player folder down to the file (empty for files at the top), for <see cref="AssetTagger"/>.</param>
public sealed record DriveFileEntry(
    string FileId,
    string Name,
    AssetKind Kind,
    long SizeBytes,
    string? Md5,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<string> Subfolders);

/// <summary>A player folder and the files in it.</summary>
/// <param name="Status">Whether the folder can be used.</param>
/// <param name="Name">The folder name, when found.</param>
/// <param name="Files">The ingestible files, breadth-first in Drive's order.</param>
/// <param name="SkippedCount">Files left out: unsupported types, Google Docs and empty files.</param>
/// <param name="IsTruncated">Whether the walk stopped at the limit, so files may be missing.</param>
public sealed record DriveFolderListing(
    DriveFolderStatus Status,
    string? Name,
    IReadOnlyList<DriveFileEntry> Files,
    int SkippedCount,
    bool IsTruncated)
{
    /// <summary>Builds the listing of a folder that cannot be used.</summary>
    /// <param name="status">Why.</param>
    /// <returns>The listing.</returns>
    public static DriveFolderListing Missing(DriveFolderStatus status) => new(status, null, [], 0, false);
}
