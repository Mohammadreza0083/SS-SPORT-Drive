using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Security;

namespace SsSport.Drive.Infrastructure.Links;

/// <summary>The <c>Viewer</c> configuration section (<c>Viewer__*</c> in <c>.env</c>): the email gate's secrets (L-2).</summary>
public sealed record ViewerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Viewer";

    /// <summary>The smallest accepted key, in bytes.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// Gets the pepper for device, session and code hashes: base64 of at least 32 random bytes. Changing it signs every
    /// viewer out. Never logged.
    /// </summary>
    public string TokenKey { get; init; } = string.Empty;

    /// <summary>Reads the key, or fails when it is missing or too short.</summary>
    /// <returns>The key bytes.</returns>
    /// <exception cref="InvalidOperationException">The key is not configured.</exception>
    public byte[] KeyBytes()
    {
        var buffer = new byte[TokenKey.Length];
        return !string.IsNullOrEmpty(TokenKey) && Convert.TryFromBase64String(TokenKey, buffer, out var written) && written >= MinimumKeyBytes
            ? buffer[..written]
            : throw new InvalidOperationException("Viewer__TokenKey is not configured: set base64 of at least 32 random bytes.");
    }
}

/// <summary>
/// The gate's secrets with HMAC-SHA256 (L-2, decisions §1b): tokens are 32 random bytes and are stored as
/// <c>HMAC(key, token)</c>; codes are 6 random digits stored as <c>salt.HMAC(key, salt + code)</c> with a 16-byte random
/// salt, checked in constant time.
/// </summary>
/// <param name="options">The settings with the key.</param>
public sealed class HmacViewerSecrets(IOptions<ViewerOptions> options) : IViewerSecrets
{
    /// <summary>Bytes of randomness in a token.</summary>
    private const int TokenBytes = 32;

    /// <summary>Bytes of salt per code.</summary>
    private const int SaltBytes = 16;

    /// <summary>One more than the largest code.</summary>
    private const int CodeRange = 1_000_000;

    /// <inheritdoc />
    public string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <inheritdoc />
    public string HashToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Base64Url.EncodeToString(HMACSHA256.HashData(options.Value.KeyBytes(), Encoding.UTF8.GetBytes(token)));
    }

    /// <inheritdoc />
    public string NewCode() => RandomNumberGenerator.GetInt32(CodeRange).ToString("D6", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public string HashCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return $"{Base64Url.EncodeToString(salt)}.{Base64Url.EncodeToString(Mac(salt, code))}";
    }

    /// <inheritdoc />
    public bool CodeMatches(string code, string? storedHash)
    {
        ArgumentNullException.ThrowIfNull(code);
        var parts = storedHash?.Split('.');
        if (parts is not { Length: 2 })
        {
            return false;
        }

        try
        {
            var salt = Base64Url.DecodeFromChars(parts[0]);
            var expected = Base64Url.DecodeFromChars(parts[1]);
            return CryptographicOperations.FixedTimeEquals(Mac(salt, code), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The keyed hash of a salted code.</summary>
    /// <param name="salt">The salt.</param>
    /// <param name="code">The code.</param>
    /// <returns>The MAC.</returns>
    private byte[] Mac(byte[] salt, string code) => HMACSHA256.HashData(options.Value.KeyBytes(), (byte[])[.. salt, .. Encoding.ASCII.GetBytes(code)]);
}
