using System.Text.RegularExpressions;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>The <c>Telegram</c> configuration section (<c>Telegram__*</c> in <c>.env</c>).</summary>
public sealed partial record TelegramOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Telegram";

    /// <summary>Gets the bot's username without <c>@</c> (<c>sssportdrive_bot</c>).</summary>
    public string BotUsername { get; init; } = string.Empty;

    /// <summary>Gets the bot token from BotFather. Never logged.</summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>Gets the Telegram user id of the first admin.</summary>
    public long? AdminUserId { get; init; }

    /// <summary>
    /// Gets the secret Telegram sends in <c>X-Telegram-Bot-Api-Secret-Token</c> with every webhook call
    /// (1–256 of A–Z, a–z, 0–9, <c>_</c>, <c>-</c>). Empty disables the webhook: every call is rejected. Never logged.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>Gets the public HTTPS URL of <c>/telegram/webhook</c>, registered by the <c>telegram-set-webhook</c> step.</summary>
    public string WebhookUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets an optional HTTP proxy for calls to the Bot API, e.g. <c>http://127.0.0.1:10808</c> where Telegram is only
    /// reachable through a VPN. Empty means a direct connection (the VPS).
    /// </summary>
    public string Proxy { get; init; } = string.Empty;

    /// <summary>Checks a webhook secret against Telegram's rule; empty is allowed (webhook disabled).</summary>
    /// <param name="secret">The secret.</param>
    /// <returns><see langword="true"/> when the secret can be used.</returns>
    public static bool IsValidSecret(string? secret) => string.IsNullOrEmpty(secret) || SecretPattern().IsMatch(secret);

    /// <summary>1–256 characters from A–Z, a–z, 0–9, underscore and hyphen.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex("^[A-Za-z0-9_-]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();
}
