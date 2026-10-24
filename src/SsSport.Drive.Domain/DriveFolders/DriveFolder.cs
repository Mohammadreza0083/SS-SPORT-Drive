using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.DriveFolders;

/// <summary>Whether the service account can still read a linked folder.</summary>
public enum FolderAccess
{
    /// <summary>The folder can be read.</summary>
    Ok,

    /// <summary>The folder was unshared, moved or deleted; sync is paused until access returns.</summary>
    Lost,
}

/// <summary>Raised when a Drive folder is linked to a player.</summary>
/// <param name="FolderId">The linked folder.</param>
/// <param name="PlayerId">The player it belongs to.</param>
public sealed record DriveFolderLinked(Guid FolderId, Guid PlayerId) : IDomainEvent;

/// <summary>Raised when the service account loses access to a linked folder; admins are alerted.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="PlayerId">The player it belongs to.</param>
public sealed record FolderAccessLost(Guid FolderId, Guid PlayerId) : IDomainEvent;

/// <summary>Raised when a folder that had lost access syncs again.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="PlayerId">The player it belongs to.</param>
public sealed record FolderAccessRestored(Guid FolderId, Guid PlayerId) : IDomainEvent;

/// <summary>
/// A Google Drive folder linked to a player. A player can have several; a new folder is added, never swapped in.
/// The sync poller walks each folder every 10 minutes.
/// </summary>
public sealed class DriveFolder : AggregateRoot<Guid>
{
    /// <summary>Initializes a folder; use <see cref="Link"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="playerId">The owning player.</param>
    /// <param name="driveFolderId">The Google Drive folder id.</param>
    /// <param name="name">The folder name in Drive.</param>
    /// <param name="linkedAt">When it was linked (UTC).</param>
    private DriveFolder(Guid id, Guid playerId, string driveFolderId, string name, DateTimeOffset linkedAt)
        : base(id)
    {
        PlayerId = playerId;
        DriveFolderId = driveFolderId;
        Name = name;
        LinkedAt = linkedAt;
    }

    /// <summary>Gets the owning player.</summary>
    public Guid PlayerId { get; }

    /// <summary>Gets the Google Drive folder id.</summary>
    public string DriveFolderId { get; }

    /// <summary>Gets the folder name as it is in Drive.</summary>
    public string Name { get; private set; }

    /// <summary>Gets when the folder was linked (UTC).</summary>
    public DateTimeOffset LinkedAt { get; }

    /// <summary>Gets when the folder last synced successfully (UTC), or <see langword="null"/> before the first sync.</summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Gets whether the folder can still be read.</summary>
    public FolderAccess Access { get; private set; } = FolderAccess.Ok;

    /// <summary>Gets when access was lost (UTC), or <see langword="null"/> while the folder can be read.</summary>
    public DateTimeOffset? AccessLostAt { get; private set; }

    /// <summary>Links a Drive folder to a player and raises <see cref="DriveFolderLinked"/>.</summary>
    /// <param name="playerId">The owning player.</param>
    /// <param name="driveFolderId">The Google Drive folder id.</param>
    /// <param name="name">The folder name in Drive.</param>
    /// <param name="linkedAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="DomainException"><c>drive_folder.invalid</c> when the id or the name is blank.</exception>
    public static DriveFolder Link(Guid playerId, string driveFolderId, string name, DateTimeOffset linkedAt)
    {
        var folder = new DriveFolder(Guid.CreateVersion7(linkedAt), playerId, Required(driveFolderId), Required(name), linkedAt);
        folder.RaiseDomainEvent(new DriveFolderLinked(folder.Id, playerId));
        return folder;
    }

    /// <summary>Takes the folder's new name after it was renamed in Drive.</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name) => Name = Required(name);

    /// <summary>Records a successful sync; restores access (and raises <see cref="FolderAccessRestored"/>) if it was lost.</summary>
    /// <param name="syncedAt">The current time (UTC).</param>
    public void MarkSynced(DateTimeOffset syncedAt)
    {
        LastSyncedAt = syncedAt;
        if (Access == FolderAccess.Lost)
        {
            Access = FolderAccess.Ok;
            AccessLostAt = null;
            RaiseDomainEvent(new FolderAccessRestored(Id, PlayerId));
        }
    }

    /// <summary>Records that the folder can no longer be read and raises <see cref="FolderAccessLost"/> once.</summary>
    /// <param name="detectedAt">When the loss was detected (UTC).</param>
    public void MarkAccessLost(DateTimeOffset detectedAt)
    {
        if (Access == FolderAccess.Lost)
        {
            return;
        }

        Access = FolderAccess.Lost;
        AccessLostAt = detectedAt;
        RaiseDomainEvent(new FolderAccessLost(Id, PlayerId));
    }

    /// <summary>Trims a required text value.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The trimmed value.</returns>
    private static string Required(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0
            ? text
            : throw new DomainException("drive_folder.invalid", "A Drive folder needs its Drive id and its name.");
    }
}
