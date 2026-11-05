using System.Text.RegularExpressions;
using SsSport.Drive.Application.Telegram;
using Telegram.Bot.Types;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>
/// Turns a Telegram update into <see cref="HandleTelegramUpdateCommand"/>, for the webhook and for local polling alike.
/// Only ids, the sender's profile, a bot command name, our button data and a typed message (for the chat flow, T-10a;
/// never logged) are kept.
/// </summary>
public static partial class TelegramUpdateMapper
{
    /// <summary>The longest display name a user can have.</summary>
    private const int MaxDisplayNameLength = 100;

    /// <summary>Maps an update.</summary>
    /// <param name="update">The update.</param>
    /// <returns>The command.</returns>
    public static HandleTelegramUpdateCommand ToCommand(Update update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var user = update.Message?.From ?? update.CallbackQuery?.From ?? update.MyChatMember?.From;
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id ?? user?.Id;

        // A button on the bot's photo picker carries the photo through the message the picker replies to.
        var photoMessage = update.Message ?? update.CallbackQuery?.Message?.ReplyToMessage;

        return new HandleTelegramUpdateCommand(update.Id, update.Type.ToString(), user?.Id)
        {
            Sender = user is null ? null : new TelegramSender(user.Id, chatId ?? user.Id, user.Username, DisplayName(user)),
            BotCommand = ParseCommand(update.Message?.Text),
            Text = update.Message?.Text is { } text && ParseCommand(text) is null && !text.StartsWith('/') ? text : null,
            CallbackData = update.CallbackQuery?.Data,
            CallbackQueryId = update.CallbackQuery?.Id,
            CallbackMessageId = update.CallbackQuery?.Message?.MessageId,
            Photo = PhotoOf(photoMessage),
            MessageId = photoMessage?.MessageId,
            ReplyToMessageId = update.Message?.ReplyToMessage?.MessageId,
        };
    }

    /// <summary>
    /// Reads the photo of a message (T-4d): the largest Telegram photo size, or an image sent as a File.
    /// </summary>
    /// <param name="message">The message, or <see langword="null"/>.</param>
    /// <returns>The photo, or <see langword="null"/> when the message has none.</returns>
    private static IncomingPhoto? PhotoOf(Message? message)
    {
        if (message?.Photo is { Length: > 0 } sizes)
        {
            var largest = sizes.MaxBy(size => (long)size.Width * size.Height)!;
            return new IncomingPhoto(largest.FileId, largest.FileSize, IsFile: false);
        }

        return message?.Document is { MimeType: { } type } document && type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? new IncomingPhoto(document.FileId, document.FileSize, IsFile: true)
            : null;
    }

    /// <summary>Builds a display name from first and last name (falling back to the username or the id).</summary>
    /// <param name="user">The Telegram user.</param>
    /// <returns>The name, at most 100 characters.</returns>
    private static string DisplayName(global::Telegram.Bot.Types.User user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (name.Length == 0)
        {
            name = user.Username ?? user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return name.Length > MaxDisplayNameLength ? name[..MaxDisplayNameLength] : name;
    }

    /// <summary>Reads a bot command (<c>/start@bot args</c> → <c>start</c>); anything else gives <see langword="null"/>.</summary>
    /// <param name="text">The message text.</param>
    /// <returns>The command name, or <see langword="null"/>.</returns>
    private static string? ParseCommand(string? text)
    {
        if (text is null || !text.StartsWith('/'))
        {
            return null;
        }

        var token = text[1..].Split([' ', '\n'], 2)[0].Split('@', 2)[0].ToLowerInvariant();
        return CommandPattern().IsMatch(token) ? token : null;
    }

    /// <summary>A Telegram command name.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommandPattern();
}
