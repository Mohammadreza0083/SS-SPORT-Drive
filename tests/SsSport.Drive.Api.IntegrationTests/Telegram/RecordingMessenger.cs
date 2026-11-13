using System.Collections.Concurrent;
using SsSport.Drive.Application.Telegram;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>One call the bot made to Telegram.</summary>
/// <param name="Kind">Send, Edit, Answer or Photo.</param>
/// <param name="ChatId">The chat, for Send and Edit.</param>
/// <param name="MessageId">The edited message, for Edit.</param>
/// <param name="Text">The text (the callback answer text, or the photo caption).</param>
/// <param name="Keyboard">The buttons, if any.</param>
/// <param name="ShowAlert">For Answer: shown as a pop-up.</param>
internal sealed record BotCall(string Kind, long? ChatId, int? MessageId, string? Text, BotKeyboard? Keyboard, bool ShowAlert)
{
    /// <summary>Gets every button's callback data, row by row.</summary>
    public IEnumerable<string> ButtonData => Keyboard?.Rows.SelectMany(row => row).Select(button => button.CallbackData) ?? [];

    /// <summary>Gets the message a Send or Photo replies to, or <see langword="null"/>.</summary>
    public int? ReplyTo { get; init; }

    /// <summary>Gets the image bytes of a Photo call (T-6b), or <see langword="null"/>.</summary>
    public byte[]? Photo { get; init; }
}

/// <summary>Stands in for Telegram: records every message the bot sends, edits or answers, and serves files.</summary>
internal sealed class RecordingMessenger : ITelegramMessenger
{
    /// <summary>The next message id.</summary>
    private int _nextMessageId = 1000;

    /// <summary>Gets every call, in order.</summary>
    public ConcurrentQueue<BotCall> Calls { get; } = new();

    /// <summary>Gets the files users sent, by Telegram file id, for <see cref="DownloadFileAsync"/>.</summary>
    public ConcurrentDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether sends fail, as when Telegram is unreachable.</summary>
    public bool FailSends { get; set; }

    /// <summary>Gets or sets a value indicating whether edits fail, as when the user deleted the message.</summary>
    public bool FailEdits { get; set; }

    /// <summary>Gets or sets how long every send takes, to stand in for a slow Telegram (T-6c).</summary>
    public TimeSpan SendDelay { get; set; }

    /// <summary>Gets the calls sent to or edited in a chat.</summary>
    /// <param name="chatId">The chat.</param>
    /// <returns>The calls.</returns>
    public IReadOnlyList<BotCall> ToChat(long chatId) => Calls.Where(call => call.ChatId == chatId).ToList();

    /// <summary>Waits until a call matching the condition has been recorded, as a background notice arrives later.</summary>
    /// <param name="match">The condition.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The first matching call.</returns>
    /// <exception cref="TimeoutException">No such call arrived in time.</exception>
    public async Task<BotCall> WaitForAsync(Func<BotCall, bool> match, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Calls.FirstOrDefault(match) is { } call)
            {
                return call;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No matching bot call within {timeout}.");
    }

    /// <inheritdoc />
    public Task<int> SendAsync(long chatId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken) =>
        SendReplyAsync(chatId, null, text, keyboard, cancellationToken);

    /// <inheritdoc />
    public async Task<int> SendReplyAsync(long chatId, int? replyToMessageId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken)
    {
        if (SendDelay > TimeSpan.Zero)
        {
            await Task.Delay(SendDelay, cancellationToken);
        }

        if (FailSends)
        {
            throw new HttpRequestException("Telegram is unreachable.");
        }

        var id = Interlocked.Increment(ref _nextMessageId);
        Calls.Enqueue(new BotCall("Send", chatId, id, text, keyboard, false) { ReplyTo = replyToMessageId });
        return id;
    }

    /// <inheritdoc />
    public Task<int> SendPhotoAsync(long chatId, int? replyToMessageId, byte[] photo, string caption, CancellationToken cancellationToken)
    {
        if (FailSends)
        {
            throw new HttpRequestException("Telegram is unreachable.");
        }

        var id = Interlocked.Increment(ref _nextMessageId);
        Calls.Enqueue(new BotCall("Photo", chatId, id, caption, null, false) { ReplyTo = replyToMessageId, Photo = photo });
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

    /// <inheritdoc />
    public Task<byte[]> DownloadFileAsync(string fileId, CancellationToken cancellationToken) =>
        Files.TryGetValue(fileId, out var bytes) ? Task.FromResult(bytes) : throw new InvalidOperationException($"Unknown file {fileId}.");
}
