using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Links;

/// <summary>Whether a link shows one player or several.</summary>
public enum ShareLinkType
{
    /// <summary>One player.</summary>
    Personal,

    /// <summary>Several players on one page (the canvas's Roster).</summary>
    Roster,
}

/// <summary>Where a link is in its lifecycle.</summary>
public enum ShareLinkStatus
{
    /// <summary>Opens until it expires.</summary>
    Active,

    /// <summary>Stopped for now; can be resumed.</summary>
    Paused,

    /// <summary>Stopped for good.</summary>
    Revoked,
}

/// <summary>Who may open a forwarded link after verifying their email (L-3).</summary>
public enum ForwardingScope
{
    /// <summary>Any verified email.</summary>
    AnyEmail,

    /// <summary>Only emails on the same domain as one of the recipient's.</summary>
    SameDomain,
}

/// <summary>Whether a link opens at a given moment, and why not.</summary>
public enum ShareLinkAccess
{
    /// <summary>The link opens.</summary>
    Open,

    /// <summary>The expiry has passed.</summary>
    Expired,

    /// <summary>An admin paused it.</summary>
    Paused,

    /// <summary>An admin revoked it.</summary>
    Revoked,
}

/// <summary>Raised when a link is created.</summary>
/// <param name="ShareLinkId">The new link.</param>
public sealed record ShareLinkCreated(Guid ShareLinkId) : IDomainEvent;

/// <summary>
/// The switches of a link (brief §11). Downloads are not among them: nothing can be downloaded (decisions §6).
/// </summary>
/// <param name="VerifyOnNewDevice">Whether another device must verify an email first (L-2).</param>
/// <param name="AllowForwarding">Whether an email that is not the recipient's may open it after verifying (L-3).</param>
/// <param name="ForwardingScope">Which emails a forward may come from.</param>
/// <param name="NotifyOnOpen">Whether admins are told when the link is opened (L-7).</param>
public sealed record ShareLinkSettings(bool VerifyOnNewDevice, bool AllowForwarding, ForwardingScope ForwardingScope, bool NotifyOnOpen)
{
    /// <summary>Gets the defaults: verify new devices, forwarding open to any email (decisions §7), no open notifications.</summary>
    public static ShareLinkSettings Default { get; } = new(VerifyOnNewDevice: true, AllowForwarding: true, ForwardingScope.AnyEmail, NotifyOnOpen: false);
}

/// <summary>
/// A personal link (L-1): <c>/r/{code}</c>, made for one <see cref="Recipient"/> and one published player (Personal) or
/// several (Roster). It opens while it is active and not expired. Admins can pause and resume it, move or remove its
/// expiry, and revoke it for good.
/// </summary>
public sealed class ShareLink : AggregateRoot<Guid>
{
    /// <summary>The most players one link may show.</summary>
    public const int MaxPlayers = 20;

    /// <summary>The longest personal note.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>The players, in page order.</summary>
    private readonly List<Guid> _playerIds = [];

    /// <summary>Initializes a link; use <see cref="Create"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="code">The URL code.</param>
    /// <param name="recipientId">The recipient.</param>
    /// <param name="createdAt">When it was created (UTC).</param>
    private ShareLink(Guid id, ShareLinkCode code, Guid recipientId, DateTimeOffset createdAt)
        : base(id)
    {
        Code = code;
        RecipientId = recipientId;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the URL code.</summary>
    public ShareLinkCode Code { get; }

    /// <summary>Gets whether the link shows one player or several.</summary>
    public ShareLinkType Type { get; private set; }

    /// <summary>Gets the recipient the link is made for.</summary>
    public Guid RecipientId { get; }

    /// <summary>Gets the players, in page order.</summary>
    public IReadOnlyList<Guid> PlayerIds => _playerIds.AsReadOnly();

    /// <summary>Gets the personal note shown on the page, or <see langword="null"/>.</summary>
    public string? Note { get; private set; }

    /// <summary>Gets when the link stops opening (UTC), or <see langword="null"/> for never.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Gets whether the link is active, paused or revoked.</summary>
    public ShareLinkStatus Status { get; private set; } = ShareLinkStatus.Active;

    /// <summary>Gets when the link was revoked (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Gets whether another device must verify an email first.</summary>
    public bool VerifyOnNewDevice { get; private set; }

    /// <summary>Gets whether an email that is not the recipient's may open it after verifying.</summary>
    public bool AllowForwarding { get; private set; }

    /// <summary>Gets which emails a forward may come from.</summary>
    public ForwardingScope ForwardingScope { get; private set; }

    /// <summary>Gets whether admins are told when the link is opened.</summary>
    public bool NotifyOnOpen { get; private set; }

    /// <summary>Gets the team user who created the link, or <see langword="null"/> when the system did (approved access requests).</summary>
    public Guid? CreatedByUserId { get; private set; }

    /// <summary>Gets when the link was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Creates an active link and raises <see cref="ShareLinkCreated"/>.</summary>
    /// <param name="code">A code no other link uses.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="players">One to twenty published players, in page order; repeats are dropped.</param>
    /// <param name="note">The personal note (at most 500 characters), or blank.</param>
    /// <param name="expiresAt">When it stops opening, or <see langword="null"/> for never.</param>
    /// <param name="settings">The switches.</param>
    /// <param name="createdByUserId">The admin, or <see langword="null"/> for the system.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The link.</returns>
    /// <exception cref="DomainException">
    /// <c>share_link.no_players</c>, <c>share_link.too_many_players</c>, <c>share_link.player_not_published</c>,
    /// <c>share_link.note_too_long</c> or <c>share_link.expiry_in_past</c>.
    /// </exception>
    public static ShareLink Create(
        ShareLinkCode code,
        Recipient recipient,
        IReadOnlyCollection<Player> players,
        string? note,
        DateTimeOffset? expiresAt,
        ShareLinkSettings settings,
        Guid? createdByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(settings);

        var unique = players.DistinctBy(player => player.Id).ToList();
        if (unique.Count == 0)
        {
            throw new DomainException("share_link.no_players", "Choose at least one player.");
        }

        if (unique.Count > MaxPlayers)
        {
            throw new DomainException("share_link.too_many_players", $"A link can show at most {MaxPlayers} players.");
        }

        if (unique.Any(player => !player.IsPublished))
        {
            throw new DomainException("share_link.player_not_published", "Links can only be made for published players.");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > MaxNoteLength)
        {
            throw new DomainException("share_link.note_too_long", $"Use a note of at most {MaxNoteLength} characters.");
        }

        EnsureFuture(expiresAt, now);
        var link = new ShareLink(Guid.CreateVersion7(now), code, recipient.Id, now)
        {
            Type = unique.Count == 1 ? ShareLinkType.Personal : ShareLinkType.Roster,
            Note = trimmedNote,
            ExpiresAt = expiresAt,
            VerifyOnNewDevice = settings.VerifyOnNewDevice,
            AllowForwarding = settings.AllowForwarding,
            ForwardingScope = settings.ForwardingScope,
            NotifyOnOpen = settings.NotifyOnOpen,
            CreatedByUserId = createdByUserId,
        };
        link._playerIds.AddRange(unique.Select(player => player.Id));
        link.RaiseDomainEvent(new ShareLinkCreated(link.Id));
        return link;
    }

    /// <summary>Tells whether the link opens at a moment, and why not.</summary>
    /// <param name="now">The moment (UTC).</param>
    /// <returns>Revoked and paused win over expired.</returns>
    public ShareLinkAccess AccessAt(DateTimeOffset now) => Status switch
    {
        ShareLinkStatus.Revoked => ShareLinkAccess.Revoked,
        ShareLinkStatus.Paused => ShareLinkAccess.Paused,
        _ when ExpiresAt <= now => ShareLinkAccess.Expired,
        _ => ShareLinkAccess.Open,
    };

    /// <summary>Stops the link for now.</summary>
    /// <returns><see langword="true"/> when it was active.</returns>
    /// <exception cref="DomainException"><c>share_link.revoked</c>.</exception>
    public bool Pause()
    {
        EnsureNotRevoked();
        if (Status == ShareLinkStatus.Paused)
        {
            return false;
        }

        Status = ShareLinkStatus.Paused;
        return true;
    }

    /// <summary>Lets a paused link open again; its expiry still applies.</summary>
    /// <returns><see langword="true"/> when it was paused.</returns>
    /// <exception cref="DomainException"><c>share_link.revoked</c>.</exception>
    public bool Resume()
    {
        EnsureNotRevoked();
        if (Status == ShareLinkStatus.Active)
        {
            return false;
        }

        Status = ShareLinkStatus.Active;
        return true;
    }

    /// <summary>Stops the link for good.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="true"/> when it was not revoked yet.</returns>
    public bool Revoke(DateTimeOffset now)
    {
        if (Status == ShareLinkStatus.Revoked)
        {
            return false;
        }

        Status = ShareLinkStatus.Revoked;
        RevokedAt = now;
        return true;
    }

    /// <summary>Moves or removes the expiry; a later expiry opens an expired link again.</summary>
    /// <param name="expiresAt">The new expiry, or <see langword="null"/> for never.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="true"/> when the expiry changed.</returns>
    /// <exception cref="DomainException"><c>share_link.revoked</c> or <c>share_link.expiry_in_past</c>.</exception>
    public bool ChangeExpiry(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        EnsureNotRevoked();
        if (expiresAt == ExpiresAt)
        {
            return false;
        }

        EnsureFuture(expiresAt, now);
        ExpiresAt = expiresAt;
        return true;
    }

    /// <summary>Refuses an expiry that is not in the future.</summary>
    /// <param name="expiresAt">The expiry, or <see langword="null"/>.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>share_link.expiry_in_past</c>.</exception>
    private static void EnsureFuture(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt <= now)
        {
            throw new DomainException("share_link.expiry_in_past", "Choose an expiry in the future.");
        }
    }

    /// <summary>Refuses changes to a revoked link.</summary>
    /// <exception cref="DomainException"><c>share_link.revoked</c>.</exception>
    private void EnsureNotRevoked()
    {
        if (Status == ShareLinkStatus.Revoked)
        {
            throw new DomainException("share_link.revoked", "This link was revoked and cannot be changed.");
        }
    }
}
