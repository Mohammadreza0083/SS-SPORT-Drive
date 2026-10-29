using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>The Telegram user a Mini App was opened by, as signed by Telegram in initData.</summary>
/// <param name="Id">The Telegram user id.</param>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name, or <see langword="null"/>.</param>
/// <param name="Username">The username without <c>@</c>, or <see langword="null"/>.</param>
public sealed record MiniAppUser(long Id, string FirstName, string? LastName, string? Username);

/// <summary>Why initData was not trusted.</summary>
public enum MiniAppInitDataError
{
    /// <summary>No initData was sent.</summary>
    Missing,

    /// <summary>The data cannot be read: no hash, a repeated field, or no readable user or issue date.</summary>
    Malformed,

    /// <summary>The hash does not match our bot token's signature (forged, tampered or for another bot).</summary>
    InvalidSignature,

    /// <summary>Telegram issued the data more than <see cref="MiniAppInitData.MaxAge"/> ago.</summary>
    Expired,

    /// <summary>The issue date lies further in the future than clock differences explain.</summary>
    IssuedInFuture,
}

/// <summary>The outcome of checking initData.</summary>
/// <param name="User">The signed-in Telegram user when valid.</param>
/// <param name="AuthDate">When Telegram issued the data, when valid.</param>
/// <param name="Error">Why the data was rejected, or <see langword="null"/> when valid.</param>
public sealed record MiniAppInitDataResult(MiniAppUser? User, DateTimeOffset? AuthDate, MiniAppInitDataError? Error)
{
    /// <summary>Gets a value indicating whether the data is trusted.</summary>
    public bool IsValid => Error is null;

    /// <summary>Creates a rejection.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The result.</returns>
    public static MiniAppInitDataResult Fail(MiniAppInitDataError error) => new(null, null, error);
}

/// <summary>
/// Checks Telegram Mini App initData (T-3, https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app):
/// every field except <c>hash</c>, sorted by key as <c>key=value</c> lines, must hash with HMAC-SHA256 under
/// <c>HMAC-SHA256("WebAppData", botToken)</c> to <c>hash</c>, and <c>auth_date</c> must be at most 24 hours old.
/// </summary>
public static class MiniAppInitData
{
    /// <summary>How long initData stays valid after Telegram issued it (owner, 2026-10-29).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>How far in the future an issue date may lie, for clock differences between Telegram and us.</summary>
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>The fixed key Telegram derives the Mini App secret with.</summary>
    private static readonly byte[] _webAppDataKey = Encoding.UTF8.GetBytes("WebAppData");

    /// <summary>Checks initData against the bot token and the current time.</summary>
    /// <param name="initData">The raw, URL-encoded initData.</param>
    /// <param name="botToken">Our bot token; empty rejects everything.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The user when valid, otherwise the reason.</returns>
    public static MiniAppInitDataResult Validate(string? initData, string botToken, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(initData))
        {
            return MiniAppInitDataResult.Fail(MiniAppInitDataError.Missing);
        }

        if (!TryParse(initData, out var fields) || !fields.Remove("hash", out var hash))
        {
            return MiniAppInitDataResult.Fail(MiniAppInitDataError.Malformed);
        }

        if (string.IsNullOrEmpty(botToken) || !SignatureMatches(fields, hash, botToken))
        {
            return MiniAppInitDataResult.Fail(MiniAppInitDataError.InvalidSignature);
        }

        if (!fields.TryGetValue("auth_date", out var rawAuthDate)
            || !long.TryParse(rawAuthDate, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !TryReadUser(fields, out var user))
        {
            return MiniAppInitDataResult.Fail(MiniAppInitDataError.Malformed);
        }

        var authDate = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (authDate > now + AllowedClockSkew)
        {
            return MiniAppInitDataResult.Fail(MiniAppInitDataError.IssuedInFuture);
        }

        return now - authDate > MaxAge
            ? MiniAppInitDataResult.Fail(MiniAppInitDataError.Expired)
            : new MiniAppInitDataResult(user, authDate, null);
    }

    /// <summary>Splits URL-encoded <c>key=value</c> pairs; a pair without <c>=</c> or a repeated key fails.</summary>
    /// <param name="initData">The raw initData.</param>
    /// <param name="fields">The decoded fields.</param>
    /// <returns><see langword="true"/> when every pair could be read.</returns>
    private static bool TryParse(string initData, out Dictionary<string, string> fields)
    {
        fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Trim().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || !fields.TryAdd(Decode(pair[..separator]), Decode(pair[(separator + 1)..])))
            {
                return false;
            }
        }

        return fields.Count > 0;
    }

    /// <summary>Decodes a form-encoded value (<c>+</c> is a space, as in the browser's URLSearchParams).</summary>
    /// <param name="value">The encoded value.</param>
    /// <returns>The decoded value.</returns>
    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    /// <summary>Recomputes the hash and compares it in constant time.</summary>
    /// <param name="fields">Every field except <c>hash</c>.</param>
    /// <param name="hash">The received hex hash.</param>
    /// <param name="botToken">Our bot token.</param>
    /// <returns><see langword="true"/> when the hash is Telegram's signature under our token.</returns>
    private static bool SignatureMatches(Dictionary<string, string> fields, string hash, string botToken)
    {
        if (hash.Length != SHA256.HashSizeInBytes * 2)
        {
            return false;
        }

        byte[] received;
        try
        {
            received = Convert.FromHexString(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        var dataCheckString = string.Join('\n', fields.OrderBy(field => field.Key, StringComparer.Ordinal).Select(field => $"{field.Key}={field.Value}"));
        var secretKey = HMACSHA256.HashData(_webAppDataKey, Encoding.UTF8.GetBytes(botToken));
        var expected = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    /// <summary>Reads the <c>user</c> JSON field; it needs a positive id and a first name.</summary>
    /// <param name="fields">The fields.</param>
    /// <param name="user">The user.</param>
    /// <returns><see langword="true"/> when the user could be read.</returns>
    private static bool TryReadUser(Dictionary<string, string> fields, out MiniAppUser user)
    {
        user = null!;
        if (!fields.TryGetValue("user", out var json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var telegramId) || telegramId <= 0
                || !root.TryGetProperty("first_name", out var firstName) || firstName.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            user = new MiniAppUser(telegramId, firstName.GetString()!, OptionalString(root, "last_name"), OptionalString(root, "username"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads an optional string property.</summary>
    /// <param name="root">The JSON object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/> when absent or not a string.</returns>
    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
