using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>
/// A player's photo as the key of its object in storage (R2), e.g. <c>players/0192f1c2/photo.webp</c>.
/// Uploading and background removal (backlog I-7) happen elsewhere; the aggregate only keeps the reference.
/// </summary>
public sealed record PlayerPhoto
{
    /// <summary>The longest accepted key.</summary>
    private const int MaxLength = 512;

    /// <summary>Initializes the photo with a validated key.</summary>
    /// <param name="storageKey">The object key.</param>
    private PlayerPhoto(string storageKey) => StorageKey = storageKey;

    /// <summary>Gets the relative object key in the media bucket.</summary>
    public string StorageKey { get; }

    /// <summary>Validates an object key.</summary>
    /// <param name="raw">The key; surrounding whitespace is trimmed.</param>
    /// <returns>The photo.</returns>
    /// <exception cref="DomainException"><c>player.photo_key_invalid</c> for empty, absolute, traversing, whitespace-containing or overlong keys.</exception>
    public static PlayerPhoto Create(string? raw)
    {
        var key = (raw ?? string.Empty).Trim();
        if (!Primitives.StorageKey.IsValid(key, MaxLength) || key.Any(char.IsWhiteSpace))
        {
            throw new DomainException("player.photo_key_invalid", "The photo must be a relative storage key, e.g. players/{id}/photo.webp.");
        }

        return new PlayerPhoto(key);
    }

    /// <summary>Returns the key.</summary>
    /// <returns>The storage key.</returns>
    public override string ToString() => StorageKey;
}
