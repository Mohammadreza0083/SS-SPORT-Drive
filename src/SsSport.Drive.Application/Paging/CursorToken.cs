using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SsSport.Drive.Application.Paging;

/// <summary>
/// Packs a cursor: the sort key's type, the sort key and the id of the last item seen, as base64url JSON
/// (<c>{"t":"DateTimeOffset","k":…,"i":"…"}</c>). The type stops a cursor from one list being read by a list sorted by
/// another key whose JSON looks the same (a date and a text are both JSON strings).
/// It is opaque to clients; a cursor only moves within a list the caller may already read, so it is not signed.
/// </summary>
public static class CursorToken
{
    /// <summary>The longest cursor accepted, so oversized input is rejected before it is decoded.</summary>
    public const int MaxLength = 512;

    /// <summary>Builds the cursor that continues after an item.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="key">The item's sort key.</param>
    /// <param name="id">The item's id (the tie-breaker).</param>
    /// <returns>The URL-safe cursor.</returns>
    public static string Encode<TKey>(TKey key, Guid id) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Payload<TKey>(typeof(TKey).Name, key, id)));

    /// <summary>Checks that text is a cursor of any key type, without knowing the list it came from.</summary>
    /// <param name="token">The text the client sent.</param>
    /// <returns><see langword="true"/> when it has a key and an id.</returns>
    public static bool IsWellFormed(string? token) => TryRead(token, out _, out _, out _);

    /// <summary>Unpacks a cursor made for a list sorted by <typeparamref name="TKey"/>.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="token">The text the client sent.</param>
    /// <param name="key">The sort key of the last item seen.</param>
    /// <param name="id">The id of the last item seen.</param>
    /// <returns><see langword="true"/> when the cursor is valid for this key type.</returns>
    public static bool TryDecode<TKey>(string? token, [MaybeNullWhen(false)] out TKey key, out Guid id)
    {
        key = default;
        if (!TryRead(token, out var keyType, out var keyElement, out id) || keyType != typeof(TKey).Name)
        {
            return false;
        }

        try
        {
            key = keyElement.Deserialize<TKey>();
            return key is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Decodes the base64url JSON and reads its key type, key element and id.</summary>
    /// <param name="token">The text the client sent.</param>
    /// <param name="keyType">The key type's name.</param>
    /// <param name="key">The key element.</param>
    /// <param name="id">The id.</param>
    /// <returns><see langword="true"/> when all three parts are present.</returns>
    private static bool TryRead(string? token, out string? keyType, out JsonElement key, out Guid id)
    {
        keyType = null;
        key = default;
        id = Guid.Empty;
        if (string.IsNullOrEmpty(token) || token.Length > MaxLength || !Base64Url.IsValid(token))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(token));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("t", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("k", out var keyElement)
                || keyElement.ValueKind == JsonValueKind.Null
                || !root.TryGetProperty("i", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || !idElement.TryGetGuid(out id))
            {
                return false;
            }

            keyType = typeElement.GetString();
            key = keyElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The cursor's JSON shape.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="T">The sort key type's name.</param>
    /// <param name="K">The sort key.</param>
    /// <param name="I">The id.</param>
    private sealed record Payload<TKey>(
        [property: System.Text.Json.Serialization.JsonPropertyName("t")] string T,
        [property: System.Text.Json.Serialization.JsonPropertyName("k")] TKey K,
        [property: System.Text.Json.Serialization.JsonPropertyName("i")] Guid I);
}
