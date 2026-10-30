using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Notifications;

/// <summary>
/// The one bot message that shows a player's progress to its analyst (T-6a): which Telegram message it is and what
/// it shows now, so it is edited in place (never a new message per change) and only when the text really changes.
/// </summary>
public sealed class ProgressMessage : AggregateRoot<Guid>
{
    /// <summary>Telegram's limit for a message text.</summary>
    public const int MaxTextLength = 4096;

    /// <summary>Initializes the message (EF Core and <see cref="Start"/>).</summary>
    /// <param name="id">The id.</param>
    /// <param name="playerId">The player it reports on.</param>
    /// <param name="chatId">The chat it was sent to.</param>
    /// <param name="messageId">The Telegram message id.</param>
    /// <param name="text">The text it shows.</param>
    /// <param name="updatedAt">When it was last sent or edited.</param>
    private ProgressMessage(Guid id, Guid playerId, long chatId, int messageId, string text, DateTimeOffset updatedAt)
        : base(id)
    {
        PlayerId = playerId;
        ChatId = chatId;
        MessageId = messageId;
        Text = text;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the player the message reports on (one message per player).</summary>
    public Guid PlayerId { get; }

    /// <summary>Gets the chat the message lives in (the analyst's private chat).</summary>
    public long ChatId { get; private set; }

    /// <summary>Gets the Telegram message id to edit.</summary>
    public int MessageId { get; private set; }

    /// <summary>Gets the text the message shows now.</summary>
    public string Text { get; private set; }

    /// <summary>Gets when the message was last sent or edited (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records the first message sent for a player.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="chatId">The chat it was sent to.</param>
    /// <param name="messageId">The Telegram message id.</param>
    /// <param name="text">The text it shows.</param>
    /// <param name="sentAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The message.</returns>
    /// <exception cref="DomainException"><c>progress.text_invalid</c> for an empty or overlong text.</exception>
    public static ProgressMessage Start(Guid playerId, long chatId, int messageId, string text, DateTimeOffset sentAt) =>
        new(Guid.CreateVersion7(sentAt), playerId, chatId, messageId, Checked(text), sentAt);

    /// <summary>Tells whether the message already shows a text, so an edit would change nothing.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text is the same.</returns>
    public bool Shows(string text) => string.Equals(Text, text, StringComparison.Ordinal);

    /// <summary>Records an edit in place.</summary>
    /// <param name="text">The new text.</param>
    /// <param name="editedAt">The current time (UTC).</param>
    public void Edited(string text, DateTimeOffset editedAt)
    {
        Text = Checked(text);
        UpdatedAt = editedAt;
    }

    /// <summary>Records that the message was sent again (the old one could not be edited) and is the one to edit now.</summary>
    /// <param name="messageId">The new Telegram message id.</param>
    /// <param name="text">The text it shows.</param>
    /// <param name="sentAt">The current time (UTC).</param>
    public void Resent(int messageId, string text, DateTimeOffset sentAt)
    {
        MessageId = messageId;
        Edited(text, sentAt);
    }

    /// <summary>Checks a message text against Telegram's rules.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text.</returns>
    private static string Checked(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength
            ? text
            : throw new DomainException("progress.text_invalid", $"A progress message needs 1 to {MaxTextLength} characters.");
}
