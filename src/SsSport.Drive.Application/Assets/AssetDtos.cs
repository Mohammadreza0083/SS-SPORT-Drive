namespace SsSport.Drive.Application.Assets;

/// <summary>One file of a player with its processing state and renditions: plain values only.</summary>
public sealed record AssetDto
{
    /// <summary>Gets the asset id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the owning player's id.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the linked Drive folder id, or <see langword="null"/>.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>Gets <c>Video</c>, <c>Document</c> or <c>Scout</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Gets <c>Drive</c> or <c>Archive</c>.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets the title shown to viewers.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the title was edited by hand.</summary>
    public bool TitleIsCustom { get; init; }

    /// <summary>Gets the tag names (e.g. <c>Highlight</c>, <c>FullMatch</c>); empty when untagged.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets the position on the player page.</summary>
    public int SortOrder { get; init; }

    /// <summary>Gets <c>Visible</c> or <c>Hidden</c>.</summary>
    public string Visibility { get; init; } = string.Empty;

    /// <summary>Gets the processing state (<c>Queued</c>, <c>Copying</c>, <c>Processing</c>, <c>Ready</c>, <c>Failed</c>).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Gets why processing failed, or <see langword="null"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Gets the content version.</summary>
    public int Version { get; init; }

    /// <summary>Gets the file name in Drive.</summary>
    public string OriginalName { get; init; } = string.Empty;

    /// <summary>Gets the size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets a video's duration in seconds, or <see langword="null"/>.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Gets a document's page count, or <see langword="null"/>.</summary>
    public int? PageCount { get; init; }

    /// <summary>Gets when the file was removed from Drive (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? RemovedFromSourceAt { get; init; }

    /// <summary>Gets when the asset was added (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the renditions, lowest quality first; empty for documents.</summary>
    public IReadOnlyList<RenditionDto> Renditions { get; init; } = [];
}

/// <summary>One HLS quality level of a video.</summary>
public sealed record RenditionDto
{
    /// <summary>Gets the frame height (360, 720 or 1080).</summary>
    public int Quality { get; init; }

    /// <summary>Gets <c>Pending</c> or <c>Ready</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the playlist's storage key, or <see langword="null"/> while pending.</summary>
    public string? PlaylistKey { get; init; }
}
