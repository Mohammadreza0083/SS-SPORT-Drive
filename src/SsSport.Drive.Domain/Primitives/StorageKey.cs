namespace SsSport.Drive.Domain.Primitives;

/// <summary>
/// The key of an object in the media store (R2, RustFS locally), e.g. <c>hls/{assetId}/720/index.m3u8</c>.
/// Keys are relative; spaces and Unicode are allowed because the Drive backup mirrors folder names.
/// </summary>
public sealed record StorageKey
{
    /// <summary>The longest key the S3 API accepts.</summary>
    private const int MaxLength = 1024;

    /// <summary>Initializes the key with a validated value.</summary>
    /// <param name="value">The key.</param>
    private StorageKey(string value) => Value = value;

    /// <summary>Gets the relative object key.</summary>
    public string Value { get; }

    /// <summary>Validates an object key.</summary>
    /// <param name="raw">The key; surrounding whitespace is trimmed.</param>
    /// <returns>The key.</returns>
    /// <exception cref="DomainException"><c>storage.key_invalid</c> for empty, absolute, traversing, URL, backslash or overlong keys.</exception>
    public static StorageKey Create(string? raw)
    {
        var key = (raw ?? string.Empty).Trim();
        if (!IsValid(key, MaxLength))
        {
            throw new DomainException("storage.key_invalid", "A storage key must be a relative object key, e.g. hls/{id}/720/index.m3u8.");
        }

        return new StorageKey(key);
    }

    /// <summary>Checks the rules every object key follows: relative, no traversal, no scheme, no backslash.</summary>
    /// <param name="key">The trimmed key.</param>
    /// <param name="maxLength">The longest accepted key.</param>
    /// <returns><see langword="true"/> when the key is usable.</returns>
    internal static bool IsValid(string key, int maxLength) =>
        key.Length > 0
        && key.Length <= maxLength
        && !key.StartsWith('/')
        && !key.Contains("://", StringComparison.Ordinal)
        && !key.Contains('\\', StringComparison.Ordinal)
        && !key.Split('/').Contains("..");

    /// <summary>Returns the key.</summary>
    /// <returns>The key.</returns>
    public override string ToString() => Value;
}
