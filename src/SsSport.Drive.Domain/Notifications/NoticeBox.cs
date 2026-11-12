using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Notifications;

/// <summary>What a held bot notice is about (T-8).</summary>
public enum NoticeKind
{
    /// <summary>Someone else added a Drive folder to the recipient's player (T-5).</summary>
    FolderAdded,

    /// <summary>An admin published the recipient's player.</summary>
    PlayerPublished,
}

/// <summary>One bot notice waiting in a <see cref="NoticeBox"/>: a ready-to-send line about one player.</summary>
public sealed class PendingNotice : Entity<Guid>
{
    /// <summary>The longest notice line.</summary>
    public const int MaxTextLength = 1000;

    /// <summary>Initializes the notice (EF Core and <see cref="NoticeBox.Queue"/>).</summary>
    /// <param name="id">The id.</param>
    /// <param name="kind">What it is about.</param>
    /// <param name="playerId">The player it is about, or <see langword="null"/>.</param>
    /// <param name="text">The line to send.</param>
    /// <param name="createdAt">When it was raised.</param>
    internal PendingNotice(Guid id, NoticeKind kind, Guid? playerId, string text, DateTimeOffset createdAt)
        : base(id)
    {
        Kind = kind;
        PlayerId = playerId;
        Text = text;
        CreatedAt = createdAt;
    }

    /// <summary>Gets what the notice is about.</summary>
    public NoticeKind Kind { get; }

    /// <summary>Gets the player it is about, for grouping, or <see langword="null"/>.</summary>
    public Guid? PlayerId { get; }

    /// <summary>Gets the line to send.</summary>
    public string Text { get; }

    /// <summary>Gets when the notice was raised (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// A team member's bot notices that may not go out yet (T-8): one box per recipient, holding the notices and when the
/// next message is due under the <see cref="NotificationRules"/>. Due notices are taken together and sent as one
/// grouped message.
/// </summary>
public sealed class NoticeBox : AggregateRoot<Guid>
{
    /// <summary>The held notices, oldest first.</summary>
    private readonly List<PendingNotice> _notices = [];

    /// <summary>Initializes the box (EF Core and <see cref="Open"/>).</summary>
    /// <param name="id">The id.</param>
    /// <param name="userId">The recipient.</param>
    /// <param name="chatId">The recipient's private chat.</param>
    private NoticeBox(Guid id, Guid userId, long chatId)
        : base(id)
    {
        UserId = userId;
        ChatId = chatId;
    }

    /// <summary>Gets the recipient (a team member).</summary>
    public Guid UserId { get; }

    /// <summary>Gets the recipient's private chat (their Telegram id).</summary>
    public long ChatId { get; private set; }

    /// <summary>Gets when the recipient last got a notice message (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSentAt { get; private set; }

    /// <summary>Gets when the held notices may go out (UTC), or <see langword="null"/> when none are held.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>Gets the held notices, oldest first.</summary>
    public IReadOnlyList<PendingNotice> Notices => _notices.AsReadOnly();

    /// <summary>Opens a recipient's box.</summary>
    /// <param name="userId">The recipient.</param>
    /// <param name="chatId">Their private chat.</param>
    /// <param name="openedAt">The current time (UTC); seeds the time-ordered id.</param>
    /// <returns>The empty box.</returns>
    public static NoticeBox Open(Guid userId, long chatId, DateTimeOffset openedAt) => new(Guid.CreateVersion7(openedAt), userId, chatId);

    /// <summary>
    /// Holds a notice. The first one held sets when the box is due; later ones join it and keep that time.
    /// </summary>
    /// <param name="kind">What it is about.</param>
    /// <param name="playerId">The player, or <see langword="null"/>.</param>
    /// <param name="text">The line to send (1 to 1000 characters).</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="rules">The notification rules.</param>
    /// <exception cref="DomainException"><c>notice.text_invalid</c> for an empty or overlong text.</exception>
    public void Queue(NoticeKind kind, Guid? playerId, string text, DateTimeOffset now, NotificationRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (string.IsNullOrWhiteSpace(text) || text.Length > PendingNotice.MaxTextLength)
        {
            throw new DomainException("notice.text_invalid", $"A notice needs 1 to {PendingNotice.MaxTextLength} characters.");
        }

        _notices.Add(new PendingNotice(Guid.CreateVersion7(now), kind, playerId, text, now));
        DueAt ??= rules.NextSendTime(now, LastSentAt);
    }

    /// <summary>Tells whether held notices may go out now.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the box is due.</returns>
    public bool IsDue(DateTimeOffset now) => _notices.Count > 0 && DueAt is { } due && due <= now;

    /// <summary>Takes the held notices once they are due, and records the message as sent now.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>The notices, oldest first; empty when the box is not due.</returns>
    public IReadOnlyList<PendingNotice> TakeDue(DateTimeOffset now)
    {
        if (!IsDue(now))
        {
            return [];
        }

        var taken = _notices.ToList();
        _notices.Clear();
        DueAt = null;
        LastSentAt = now;
        return taken;
    }

    /// <summary>Moves the due time on, keeping the notices (Telegram could not be reached).</summary>
    /// <param name="until">When to try again.</param>
    public void Postpone(DateTimeOffset until) => DueAt = until;

    /// <summary>Drops every held notice (the recipient left the team).</summary>
    public void Discard()
    {
        _notices.Clear();
        DueAt = null;
    }
}
