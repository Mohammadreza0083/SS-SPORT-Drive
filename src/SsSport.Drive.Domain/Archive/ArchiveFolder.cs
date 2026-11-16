using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Archive;

/// <summary>Where a found archive folder is.</summary>
public enum ArchiveFolderState
{
    /// <summary>Not in the app yet; an admin can import it.</summary>
    New,

    /// <summary>Already linked to a player before the scan (an analyst added it).</summary>
    Linked,

    /// <summary>Imported by an admin as a waiting player.</summary>
    Imported,
}

/// <summary>What one player folder holds (M-1).</summary>
/// <param name="Videos">Videos.</param>
/// <param name="Documents">Documents (PDFs and stats screenshots).</param>
/// <param name="Scouts">DataVolley scout files.</param>
/// <param name="Bytes">The size of those files.</param>
/// <param name="InBackup">Files whose md5 the R2 backup holds.</param>
/// <param name="MissingFromBackup">Files the backup does not hold.</param>
/// <param name="Skipped">Files left out (unsupported, Google Docs, empty).</param>
/// <param name="IsTruncated">Whether the listing stopped at its limit, so the counts are a lower bound.</param>
public sealed record ArchiveFolderCounts(int Videos, int Documents, int Scouts, long Bytes, int InBackup, int MissingFromBackup, int Skipped, bool IsTruncated)
{
    /// <summary>Gets the counts of an empty folder.</summary>
    public static ArchiveFolderCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, false);

    /// <summary>Gets the number of usable files.</summary>
    public int Files => Videos + Documents + Scouts;
}

/// <summary>
/// A player folder a scan found in the Shared Drive (M-1), with what it holds and what its name suggests (M-2). An admin
/// imports it as a waiting player, once.
/// </summary>
public sealed class ArchiveFolder : AggregateRoot<Guid>
{
    /// <summary>Initializes a folder; use <see cref="Record"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="scanId">The scan that found it.</param>
    /// <param name="driveFolderId">The Drive folder id.</param>
    /// <param name="path">The category and folder name, e.g. <c>Men Volleyball/SS-SPORT Ali Hoshmand</c>.</param>
    /// <param name="folderName">The folder name in Drive.</param>
    /// <param name="category">Men or Women, from the category folder.</param>
    /// <param name="foundAt">When it was found (UTC).</param>
    private ArchiveFolder(Guid id, Guid scanId, string driveFolderId, string path, string folderName, PlayerCategory category, DateTimeOffset foundAt)
        : base(id)
    {
        ScanId = scanId;
        DriveFolderId = driveFolderId;
        Path = path;
        FolderName = folderName;
        Category = category;
        FoundAt = foundAt;
    }

    /// <summary>Gets the scan that found the folder.</summary>
    public Guid ScanId { get; }

    /// <summary>Gets the Drive folder id.</summary>
    public string DriveFolderId { get; }

    /// <summary>Gets the category folder and the folder name.</summary>
    public string Path { get; }

    /// <summary>Gets the folder name in Drive.</summary>
    public string FolderName { get; }

    /// <summary>Gets men's or women's volleyball.</summary>
    public PlayerCategory Category { get; }

    /// <summary>Gets when the scan found it (UTC).</summary>
    public DateTimeOffset FoundAt { get; }

    /// <summary>Gets the name its folder name suggests, or <see langword="null"/>.</summary>
    public string? SuggestedName { get; private set; }

    /// <summary>Gets the positions its folder name suggests, or <see langword="null"/> when there are none.</summary>
    public PlayerPositions? SuggestedPositions { get; private set; }

    /// <summary>Gets what the folder holds.</summary>
    public ArchiveFolderCounts Counts { get; private set; } = ArchiveFolderCounts.Empty;

    /// <summary>Gets where the folder is.</summary>
    public ArchiveFolderState State { get; private set; }

    /// <summary>Gets the player it is linked to or was imported as, or <see langword="null"/> while new.</summary>
    public Guid? PlayerId { get; private set; }

    /// <summary>Gets when it was imported (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? ImportedAt { get; private set; }

    /// <summary>Records a folder a scan found and what its name suggests.</summary>
    /// <param name="scanId">The scan.</param>
    /// <param name="driveFolderId">The Drive folder id.</param>
    /// <param name="path">The category folder and the folder name.</param>
    /// <param name="folderName">The folder name.</param>
    /// <param name="category">Men or Women.</param>
    /// <param name="counts">What it holds.</param>
    /// <param name="linkedPlayerId">The player it is already linked to, or <see langword="null"/>.</param>
    /// <param name="foundAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The folder.</returns>
    public static ArchiveFolder Record(
        Guid scanId,
        string driveFolderId,
        string path,
        string folderName,
        PlayerCategory category,
        ArchiveFolderCounts counts,
        Guid? linkedPlayerId,
        DateTimeOffset foundAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveFolderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(counts);
        var suggestion = ArchiveFolderName.Parse(folderName);
        return new ArchiveFolder(Guid.CreateVersion7(foundAt), scanId, driveFolderId, path, folderName, category, foundAt)
        {
            SuggestedName = suggestion.Name,
            SuggestedPositions = suggestion.Positions.Count == 0 ? null : PlayerPositions.Create(suggestion.Positions),
            Counts = counts,
            State = linkedPlayerId is null ? ArchiveFolderState.New : ArchiveFolderState.Linked,
            PlayerId = linkedPlayerId,
        };
    }

    /// <summary>Records the waiting player the folder was imported as.</summary>
    /// <param name="playerId">The new player.</param>
    /// <param name="importedAt">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>archive.folder_not_new</c> when it is linked or imported already.</exception>
    public void MarkImported(Guid playerId, DateTimeOffset importedAt)
    {
        if (State != ArchiveFolderState.New)
        {
            throw new DomainException("archive.folder_not_new", "This folder is already in the app.");
        }

        State = ArchiveFolderState.Imported;
        PlayerId = playerId;
        ImportedAt = importedAt;
    }
}
