using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Assets;

/// <summary>An HLS quality level; the value is the frame height in pixels.</summary>
public enum RenditionQuality
{
    /// <summary>360p.</summary>
    P360 = 360,

    /// <summary>720p.</summary>
    P720 = 720,

    /// <summary>1080p.</summary>
    P1080 = 1080,
}

/// <summary>Whether a rendition has been encoded yet.</summary>
public enum RenditionStatus
{
    /// <summary>Planned, not encoded yet.</summary>
    Pending,

    /// <summary>Encoded; its playlist is in storage.</summary>
    Ready,
}

/// <summary>One HLS quality level of a video asset. Owned by <see cref="Asset"/> and changed only through it.</summary>
public sealed class Rendition : Entity<Guid>
{
    /// <summary>Initializes a planned rendition.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="quality">The quality level.</param>
    internal Rendition(Guid id, RenditionQuality quality)
        : base(id)
    {
        Quality = quality;
    }

    /// <summary>Gets the quality level.</summary>
    public RenditionQuality Quality { get; }

    /// <summary>Gets the key of the rendition's <c>index.m3u8</c>, or <see langword="null"/> while pending.</summary>
    public StorageKey? PlaylistKey { get; private set; }

    /// <summary>Gets whether the rendition is encoded.</summary>
    public RenditionStatus Status { get; private set; } = RenditionStatus.Pending;

    /// <summary>Marks the rendition encoded.</summary>
    /// <param name="playlistKey">The key of its playlist.</param>
    internal void MarkReady(StorageKey playlistKey)
    {
        PlaylistKey = playlistKey;
        Status = RenditionStatus.Ready;
    }
}
