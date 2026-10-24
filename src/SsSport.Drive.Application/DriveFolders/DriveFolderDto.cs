namespace SsSport.Drive.Application.DriveFolders;

/// <summary>A Drive folder linked to a player, with its sync and access state.</summary>
public sealed record DriveFolderDto
{
    /// <summary>Gets the folder id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the owning player's id.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the Google Drive folder id.</summary>
    public string DriveFolderId { get; init; } = string.Empty;

    /// <summary>Gets the folder name in Drive.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets <c>Ok</c> or <c>Lost</c>.</summary>
    public string Access { get; init; } = string.Empty;

    /// <summary>Gets when the folder was linked (UTC).</summary>
    public DateTimeOffset LinkedAt { get; init; }

    /// <summary>Gets when the folder last synced (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSyncedAt { get; init; }

    /// <summary>Gets when access was lost (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? AccessLostAt { get; init; }
}
