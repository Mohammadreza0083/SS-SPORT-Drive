using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Telegram;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>Sends, edits and answers the bot's messages through the Telegram Bot API (plain text, inline buttons).</summary>
/// <param name="client">The Bot API client.</param>
internal sealed class TelegramMessenger(ITelegramBotClient client) : ITelegramMessenger
{
    /// <summary>Sends a message.</summary>
    /// <param name="chatId">The chat.</param>
    /// <param name="text">The text.</param>
    /// <param name="keyboard">The buttons, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The message id.</returns>
    public async Task<int> SendAsync(long chatId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken)
    {
        var message = await client.SendMessage(chatId, text, replyMarkup: Markup(keyboard), cancellationToken: cancellationToken);
        return message.MessageId;
    }

    /// <summary>Edits a message; an edit that changes nothing is ignored.</summary>
    /// <param name="chatId">The chat.</param>
    /// <param name="messageId">The message.</param>
    /// <param name="text">The new text.</param>
    /// <param name="keyboard">The new buttons, or <see langword="null"/> to remove them.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when Telegram has the edit.</returns>
    public async Task EditAsync(long chatId, int messageId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken)
    {
        try
        {
            await client.EditMessageText(chatId, messageId, text, replyMarkup: Markup(keyboard), cancellationToken: cancellationToken);
        }
        catch (ApiRequestException exception) when (exception.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // Pressing the same button twice produces the same text; Telegram rejects such edits.
        }
    }

    /// <summary>Answers a button press.</summary>
    /// <param name="callbackQueryId">The press.</param>
    /// <param name="text">A note, or <see langword="null"/>.</param>
    /// <param name="showAlert">Show as a pop-up.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when Telegram has the answer.</returns>
    public Task AnswerCallbackAsync(string callbackQueryId, string? text, bool showAlert, CancellationToken cancellationToken) =>
        client.AnswerCallbackQuery(callbackQueryId, text, showAlert, cancellationToken: cancellationToken);

    /// <summary>Turns our keyboard into Telegram's inline markup.</summary>
    /// <param name="keyboard">The keyboard, or <see langword="null"/>.</param>
    /// <returns>The markup, or <see langword="null"/>.</returns>
    private static InlineKeyboardMarkup? Markup(BotKeyboard? keyboard) =>
        keyboard is null
            ? null
            : new InlineKeyboardMarkup(keyboard.Rows.Select(row => row.Select(button => InlineKeyboardButton.WithCallbackData(button.Text, button.CallbackData))));
}

/// <summary>Exposes the configured first admin to the Application layer.</summary>
/// <param name="options">The Telegram settings.</param>
internal sealed class TelegramBotAdminSettings(IOptions<TelegramOptions> options) : IBotAdminSettings
{
    /// <summary>Gets the first admin's Telegram id.</summary>
    public long? AdminTelegramId => options.Value.AdminUserId;

    /// <summary>Gets the first admin's email.</summary>
    public string? AdminEmail => string.IsNullOrWhiteSpace(options.Value.AdminEmail) ? null : options.Value.AdminEmail;
}
