namespace SsSport.Drive.Application.Telegram;

/// <summary>
/// An inline button: its label and either the data Telegram sends back when it is pressed (at most 64 bytes) or a
/// Mini App page it opens.
/// </summary>
/// <param name="Text">The label.</param>
/// <param name="CallbackData">The data sent back, e.g. <c>access:request</c>; empty for a Mini App button.</param>
/// <param name="WebAppUrl">The Mini App page the button opens, or <see langword="null"/> for a callback button.</param>
public sealed record BotButton(string Text, string CallbackData, string? WebAppUrl = null)
{
    /// <summary>A button that opens a page of the Mini App instead of sending callback data.</summary>
    /// <param name="text">The label.</param>
    /// <param name="url">The HTTPS page to open.</param>
    /// <returns>The button.</returns>
    public static BotButton OpenMiniApp(string text, string url) => new(text, string.Empty, url);
}

/// <summary>Inline buttons under a message, row by row.</summary>
/// <param name="Rows">The button rows.</param>
public sealed record BotKeyboard(IReadOnlyList<IReadOnlyList<BotButton>> Rows)
{
    /// <summary>Builds a keyboard with one button per row.</summary>
    /// <param name="buttons">The buttons, top to bottom.</param>
    /// <returns>The keyboard.</returns>
    public static BotKeyboard Column(params BotButton[] buttons) => new(buttons.Select(button => (IReadOnlyList<BotButton>)[button]).ToList());
}

/// <summary>
/// Sends, edits and answers the bot's messages. Implemented in Infrastructure with the Telegram Bot API; tests replace it.
/// Texts are plain (no Markdown/HTML), so names with special characters are shown as typed.
/// </summary>
public interface ITelegramMessenger
{
    /// <summary>Sends a message.</summary>
    /// <param name="chatId">The chat (a user's private chat id is their Telegram id).</param>
    /// <param name="text">The text.</param>
    /// <param name="keyboard">Inline buttons, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The sent message's id, for editing it later.</returns>
    Task<int> SendAsync(long chatId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken);

    /// <summary>Replaces a message's text and buttons (one message per operation, edited in place).</summary>
    /// <param name="chatId">The chat.</param>
    /// <param name="messageId">The message.</param>
    /// <param name="text">The new text.</param>
    /// <param name="keyboard">The new buttons, or <see langword="null"/> to remove them.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when Telegram has the edit.</returns>
    Task EditAsync(long chatId, int messageId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken);

    /// <summary>Answers a button press; Telegram shows a spinner until it is answered.</summary>
    /// <param name="callbackQueryId">The press.</param>
    /// <param name="text">A short note, or <see langword="null"/>.</param>
    /// <param name="showAlert">Show the note as a pop-up instead of a toast.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when Telegram has the answer.</returns>
    Task AnswerCallbackAsync(string callbackQueryId, string? text, bool showAlert, CancellationToken cancellationToken);
}

/// <summary>Links to pages of the Mini App, for bot buttons that open it (T-7).</summary>
public interface IMiniAppLinks
{
    /// <summary>Builds the URL of a Mini App page.</summary>
    /// <param name="page">The page under the Mini App, e.g. <c>players</c> or <c>new</c>.</param>
    /// <returns>The HTTPS URL, or <see langword="null"/> when the Mini App URL is not configured.</returns>
    string? PageUrl(string page);
}

/// <summary>The configured first admin (<c>Telegram__AdminUserId</c>, <c>Telegram__AdminEmail</c>).</summary>
public interface IBotAdminSettings
{
    /// <summary>Gets the first admin's Telegram id, or <see langword="null"/>.</summary>
    long? AdminTelegramId { get; }

    /// <summary>Gets the first admin's sign-in email, or <see langword="null"/>.</summary>
    string? AdminEmail { get; }
}
