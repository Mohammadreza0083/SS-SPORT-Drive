using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Conversations;

/// <summary>The question a chat flow waits at: a new-player question (T-10a), or the My players search text (T-10b).</summary>
public enum NewPlayerStep
{
    /// <summary>Men or Women.</summary>
    Category,

    /// <summary>The full name, typed.</summary>
    Name,

    /// <summary>The nationality: typed search, then a country button.</summary>
    Nationality,

    /// <summary>One or two positions, toggled with buttons.</summary>
    Positions,

    /// <summary>The Volleybox link, typed.</summary>
    Volleybox,

    /// <summary>The Volleybox link belongs to a known player: add a folder, update in the Mini App, or another link.</summary>
    ExistingPlayer,

    /// <summary>The Google Drive folder link, typed.</summary>
    DriveFolder,

    /// <summary>A photo, or Skip.</summary>
    Photo,

    /// <summary>The summary: Create, Edit a field or Cancel.</summary>
    Summary,

    /// <summary>Which field to change from the summary.</summary>
    EditField,

    /// <summary>The text to search My players for (T-10b); the draft stays empty.</summary>
    PlayersSearch,
}

/// <summary>
/// The answers of a new-player chat so far (T-10a). Values are kept as typed and checked by the same rules as the
/// Mini App form when they are given and again on Create.
/// </summary>
public sealed record NewPlayerDraft
{
    /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the full name, Title Cased.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the two-letter country code.</summary>
    public string? NationalityCode { get; init; }

    /// <summary>Gets the position codes, primary first (at most two).</summary>
    public IReadOnlyList<string> Positions { get; init; } = [];

    /// <summary>Gets the normalised Volleybox player URL.</summary>
    public string? VolleyboxUrl { get; init; }

    /// <summary>Gets the known player the Volleybox link belongs to, when the analyst adds a folder to it.</summary>
    public Guid? ExistingPlayerId { get; init; }

    /// <summary>Gets that player's name.</summary>
    public string? ExistingPlayerName { get; init; }

    /// <summary>Gets the Google Drive folder link.</summary>
    public string? DriveFolderUrl { get; init; }

    /// <summary>Gets the folder's name in Drive.</summary>
    public string? DriveFolderName { get; init; }

    /// <summary>Gets the Telegram file id of the photo, already checked; <see langword="null"/> when skipped or not given.</summary>
    public string? PhotoFileId { get; init; }

    /// <summary>Gets a value indicating whether the current question was opened from the summary, so its answer returns there.</summary>
    public bool ReturnToSummary { get; init; }

    /// <summary>Gets a value indicating whether anything has been answered yet.</summary>
    public bool HasAnswers => Category is not null || Name is not null || NationalityCode is not null || Positions.Count > 0 || VolleyboxUrl is not null;

    /// <summary>Compares two drafts by value, positions included.</summary>
    /// <param name="other">The other draft.</param>
    /// <returns><see langword="true"/> when every answer is the same.</returns>
    public bool Equals(NewPlayerDraft? other) =>
        other is not null
        && Category == other.Category
        && Name == other.Name
        && NationalityCode == other.NationalityCode
        && Positions.SequenceEqual(other.Positions)
        && VolleyboxUrl == other.VolleyboxUrl
        && ExistingPlayerId == other.ExistingPlayerId
        && ExistingPlayerName == other.ExistingPlayerName
        && DriveFolderUrl == other.DriveFolderUrl
        && DriveFolderName == other.DriveFolderName
        && PhotoFileId == other.PhotoFileId
        && ReturnToSummary == other.ReturnToSummary;

    /// <summary>Hashes the draft consistently with <see cref="Equals(NewPlayerDraft?)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => HashCode.Combine(Category, Name, NationalityCode, Positions.Count, VolleyboxUrl, DriveFolderUrl, PhotoFileId);
}

/// <summary>
/// One team member's unfinished chat flow with the bot (T-10a, owner-approved 2026-11-06): the question it waits at and
/// the answers so far. A user has at most one; it is forgotten 24 hours after the last answer.
/// </summary>
public sealed class BotConversation : AggregateRoot<Guid>
{
    /// <summary>How long a draft is kept after the last answer.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>Initializes the conversation (EF Core and <see cref="StartNewPlayer"/>).</summary>
    /// <param name="id">The id.</param>
    /// <param name="telegramUserId">The user's Telegram id.</param>
    /// <param name="step">The question it waits at.</param>
    /// <param name="draft">The answers so far.</param>
    /// <param name="updatedAt">When it last changed.</param>
    private BotConversation(Guid id, long telegramUserId, NewPlayerStep step, NewPlayerDraft draft, DateTimeOffset updatedAt)
        : base(id)
    {
        TelegramUserId = telegramUserId;
        Step = step;
        Draft = draft;
        UpdatedAt = updatedAt;
        ExpiresAt = updatedAt + Lifetime;
    }

    /// <summary>Initializes the conversation for EF Core, which sets the draft from its column.</summary>
    /// <param name="id">The id.</param>
    /// <param name="telegramUserId">The user's Telegram id.</param>
    /// <param name="step">The question it waits at.</param>
    /// <param name="updatedAt">When it last changed.</param>
    private BotConversation(Guid id, long telegramUserId, NewPlayerStep step, DateTimeOffset updatedAt)
        : this(id, telegramUserId, step, new NewPlayerDraft(), updatedAt)
    {
    }

    /// <summary>Gets the user's Telegram id (one conversation per user).</summary>
    public long TelegramUserId { get; }

    /// <summary>Gets the question the conversation waits at.</summary>
    public NewPlayerStep Step { get; private set; }

    /// <summary>Gets the answers so far.</summary>
    public NewPlayerDraft Draft { get; private set; }

    /// <summary>Gets when the conversation last changed (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets when the draft is forgotten (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Starts a new-player chat at the category question.</summary>
    /// <param name="telegramUserId">The user's Telegram id.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The conversation.</returns>
    public static BotConversation StartNewPlayer(long telegramUserId, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), telegramUserId, NewPlayerStep.Category, new NewPlayerDraft(), now);

    /// <summary>Starts a My players search that waits for the typed text (T-10b).</summary>
    /// <param name="telegramUserId">The user's Telegram id.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The conversation.</returns>
    public static BotConversation StartPlayersSearch(long telegramUserId, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), telegramUserId, NewPlayerStep.PlayersSearch, new NewPlayerDraft(), now);

    /// <summary>Moves to a question with the answers so far, and keeps the draft another 24 hours.</summary>
    /// <param name="step">The question.</param>
    /// <param name="draft">The answers.</param>
    /// <param name="now">The current time (UTC).</param>
    public void MoveTo(NewPlayerStep step, NewPlayerDraft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Step = step;
        Draft = draft;
        UpdatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    /// <summary>Tells whether the draft has been forgotten.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="true"/> from <see cref="ExpiresAt"/> on.</returns>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
