namespace SsSport.Drive.Infrastructure.Edge;

/// <summary>The <c>Edge</c> configuration section (<c>Edge__*</c> in <c>.env</c>): settings shared with the Cloudflare Worker.</summary>
public sealed record EdgeOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Edge";

    /// <summary>The smallest accepted signing key, in bytes.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// Gets the HLS token signing key, base64 of at least 32 random bytes. The Worker holds the same value as the
    /// <c>HLS_SIGNING_KEY</c> secret. Empty turns token issuing off (local development without the edge). Never logged.
    /// </summary>
    public string HlsSigningKey { get; init; } = string.Empty;

    /// <summary>Gets how long an HLS token works; 30 minutes (decisions §6).</summary>
    public TimeSpan HlsTokenLifetime { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Checks that a signing key is base64 of at least <see cref="MinimumKeyBytes"/> bytes.
    /// </summary>
    /// <param name="key">The configured key.</param>
    /// <returns><see langword="true"/> when the key can be used.</returns>
    public static bool IsValidSigningKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var buffer = new byte[key.Length];
        return Convert.TryFromBase64String(key, buffer, out var written) && written >= MinimumKeyBytes;
    }
}
