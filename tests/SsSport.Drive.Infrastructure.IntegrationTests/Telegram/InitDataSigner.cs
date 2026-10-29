using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Telegram;

/// <summary>
/// Builds Mini App <c>initData</c> the way Telegram does, for tests: the fields are sorted into the data-check string,
/// signed with HMAC-SHA256 under <c>HMAC-SHA256("WebAppData", botToken)</c> and URL-encoded. Shared with the API tests.
/// </summary>
public static class InitDataSigner
{
    /// <summary>Builds signed initData for a Telegram user.</summary>
    /// <param name="botToken">The bot token to sign with.</param>
    /// <param name="telegramId">The user's Telegram id.</param>
    /// <param name="authDate">When Telegram issued the data.</param>
    /// <param name="firstName">The user's first name.</param>
    /// <param name="username">The user's username, or <see langword="null"/>.</param>
    /// <returns>The URL-encoded initData.</returns>
    public static string ForUser(string botToken, long telegramId, DateTimeOffset authDate, string firstName = "Reza", string? username = "reza_analyst")
    {
        var user = username is null
            ? $$"""{"id":{{telegramId}},"first_name":"{{firstName}}"}"""
            : $$"""{"id":{{telegramId}},"first_name":"{{firstName}}","username":"{{username}}"}""";
        return Sign(botToken, new Dictionary<string, string>
        {
            ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
            ["user"] = user,
            ["auth_date"] = authDate.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>Signs arbitrary fields and appends the <c>hash</c>.</summary>
    /// <param name="botToken">The bot token to sign with.</param>
    /// <param name="fields">The fields (raw, not encoded).</param>
    /// <returns>The URL-encoded initData.</returns>
    public static string Sign(string botToken, IReadOnlyDictionary<string, string> fields)
    {
        var dataCheckString = string.Join('\n', fields.OrderBy(field => field.Key, StringComparer.Ordinal).Select(field => $"{field.Key}={field.Value}"));
        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexStringLower(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString)));
        return string.Join('&', fields.Append(new KeyValuePair<string, string>("hash", hash)).Select(field => $"{field.Key}={Uri.EscapeDataString(field.Value)}"));
    }
}
