using System.Collections.Concurrent;
using SsSport.Drive.Application.Telegram;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>One call the bot made to Telegram.</summary>
/// <param name="Kind">Send, Edit or Answer.</param>
/// <param name="ChatId">The chat, for Send and Edit.</param>
/// <param name="MessageId">The edited message, for Edit.</param>
/// <param name="Text">The text (or the callback answer text).</param>
/// <param name="Keyboard">The buttons, if any.</param>
/// <param name="ShowAlert">For Answer: shown as a pop-up.</param>
internal sealed record BotCall(string Kind, long? ChatId, int? MessageId, string? Text, BotKeyboard? Keyboard, bool ShowAlert)
{
    /// <summary>Gets every button's callback data, row by row.</summary>
    public IEnumerable<string> ButtonData => Keyboard?.Rows.SelectMany(row => row).Select(button => button.CallbackData) ?? [];
}

/// <summary>Stands in for Telegram: records every message the bot sends, edits or answers.</summary>
internal sealed class RecordingMessenger : ITelegramMessenger
{
    /// <summary>The next message id.</summary>
    private int _nextMessageId = 1000;

    /// <summary>Gets every call, in order.</summary>
    public ConcurrentQueue<BotCall> Calls { get; } = new();

    /// <summary>Gets or sets a value indicating whether sends fail, as when Telegram is unreachable.</summary>
    public bool FailSends { get; set; }

    /// <summary>Gets or sets a value indicating whether edits fail, as when the user deleted the message.</summary>
    public bool FailEdits { get; set; }

    /// <summary>Gets the calls sent to or edited in a chat.</summary>
    /// <param name="chatId">The chat.</param>
    /// <returns>The calls.</returns>
    public IReadOnlyList<BotCall> ToChat(long chatId) => Calls.Where(call => call.ChatId == chatId).ToList();

    /// <inheritdoc />
    public Task<int> SendAsync(long chatId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken)
    {
        if (FailSends)
        {
            throw new HttpRequestException("Telegram is unreachable.");
        }

        var id = Interlocked.Increment(ref _nextMessageId);
        Calls.Enqueue(new BotCall("Send", chatId, id, text, keyboard, false));
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task EditAsync(long chatId, int messageId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken)
    {
        if (FailEdits)
        {
            throw new InvalidOperationException("Bad Request: message to edit not found");
        }

        Calls.Enqueue(new BotCall("Edit", chatId, messageId, text, keyboard, false));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AnswerCallbackAsync(string callbackQueryId, string? text, bool showAlert, CancellationToken cancellationToken)
    {
        Calls.Enqueue(new BotCall("Answer", null, null, text, null, showAlert));
        return Task.CompletedTask;
    }
}
