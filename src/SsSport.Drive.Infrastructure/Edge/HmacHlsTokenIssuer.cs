using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Media;

namespace SsSport.Drive.Infrastructure.Edge;

/// <summary>
/// Signs HLS edge tokens with HMAC-SHA256 (V-5a). The format is shared with the Cloudflare Worker and pinned by
/// <c>contracts/hls-token-vectors.json</c>:
/// <c>base64url(JSON {v, p, s, e}) + "." + base64url(HMAC-SHA256(key, first part))</c>, where <c>p</c> is the video's
/// HLS folder, <c>s</c> is base64url of the first 16 bytes of SHA-256 of the session cookie, and <c>e</c> the expiry in
/// Unix seconds.
/// </summary>
/// <param name="options">The edge settings with the signing key.</param>
/// <param name="timeProvider">The clock.</param>
public sealed partial class HmacHlsTokenIssuer(IOptions<EdgeOptions> options, TimeProvider timeProvider) : IHlsTokenIssuer
{
    /// <summary>The token format version.</summary>
    private const int FormatVersion = 1;

    /// <summary>How many bytes of the session hash go into the token.</summary>
    private const int SessionHashBytes = 16;

    /// <inheritdoc />
    public bool IsEnabled => EdgeOptions.IsValidSigningKey(options.Value.HlsSigningKey);

    /// <inheritdoc />
    public HlsAccess Issue(string pathPrefix, string sessionCookie)
    {
        if (!IsSafePrefix(pathPrefix))
        {
            throw new ArgumentException("The HLS path prefix must be a plain object key folder without leading or trailing slashes.", nameof(pathPrefix));
        }

        if (string.IsNullOrEmpty(sessionCookie))
        {
            throw new ArgumentException("A viewer session is required.", nameof(sessionCookie));
        }

        var settings = options.Value;
        if (!EdgeOptions.IsValidSigningKey(settings.HlsSigningKey))
        {
            throw new InvalidOperationException("Edge__HlsSigningKey is not configured.");
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().Add(settings.HlsTokenLifetime).ToUnixTimeSeconds());
        var sessionHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionCookie)).AsSpan(0, SessionHashBytes));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new TokenPayload(FormatVersion, pathPrefix, sessionHash, expiresAt.ToUnixTimeSeconds()), TokenJsonContext.Default.TokenPayload);

        var body = Base64Url.EncodeToString(payload);
        var signature = Base64Url.EncodeToString(HMACSHA256.HashData(Convert.FromBase64String(settings.HlsSigningKey), Encoding.ASCII.GetBytes(body)));
        var token = $"{body}.{signature}";

        return new HlsAccess(token, $"/hls/{token}/{pathPrefix}/master.m3u8", expiresAt);
    }

    /// <summary>
    /// Checks that a path prefix is a plain folder key: segments of letters, digits, <c>_</c>, <c>-</c> and <c>.</c>,
    /// separated by single slashes, with no <c>.</c> or <c>..</c> segment.
    /// </summary>
    /// <param name="prefix">The prefix to check.</param>
    /// <returns><see langword="true"/> when the prefix is safe.</returns>
    private static bool IsSafePrefix(string prefix) =>
        SafePrefix().IsMatch(prefix) && prefix.Split('/').All(segment => segment is not ("." or ".."));

    /// <summary>Matches folder keys made of safe segments.</summary>
    /// <returns>The regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$")]
    private static partial Regex SafePrefix();

    /// <summary>The signed token payload; property order is part of the format.</summary>
    /// <param name="V">The format version.</param>
    /// <param name="P">The video's HLS folder.</param>
    /// <param name="S">The session hash.</param>
    /// <param name="E">The expiry, Unix seconds.</param>
    internal sealed record TokenPayload(
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("p")] string P,
        [property: JsonPropertyName("s")] string S,
        [property: JsonPropertyName("e")] long E);

    /// <summary>Source-generated JSON for <see cref="TokenPayload"/>, so the bytes never depend on runtime options.</summary>
    [JsonSerializable(typeof(TokenPayload))]
    [JsonSourceGenerationOptions(WriteIndented = false)]
    internal sealed partial class TokenJsonContext : JsonSerializerContext;
}
