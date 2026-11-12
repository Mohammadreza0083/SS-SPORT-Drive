using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Links;

/// <summary>Where an admin's decision on a forwarded link stands.</summary>
public enum ForwardStatus
{
    /// <summary>No admin has decided yet; the link does not open for the email.</summary>
    Pending,

    /// <summary>An admin kept it; the link opens for the email.</summary>
    Kept,

    /// <summary>An admin blocked it; the email can never open the link.</summary>
    Blocked,
}

/// <summary>Raised when an email verifies on a link as a forward for the first time; admins are alerted in the bot.</summary>
/// <param name="ForwardId">The forward.</param>
public sealed record LinkForwarded(Guid ForwardId) : IDomainEvent;

/// <summary>
/// A personal link forwarded to another email (L-3): recorded the first time that email verifies its code on the link.
/// The link does not open for it until an admin keeps it (owner, 2026-11-12); a block shuts it out for good. The first
/// decision is final. One row per link and email, kept as the audit trail of who the link reached.
/// </summary>
public sealed class LinkForward : AggregateRoot<Guid>
{
    /// <summary>Initializes a forward; use <see cref="Record"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The forwarded email.</param>
    /// <param name="firstVerifiedAt">When it first verified (UTC).</param>
    private LinkForward(Guid id, Guid shareLinkId, EmailAddress email, DateTimeOffset firstVerifiedAt)
        : base(id)
    {
        ShareLinkId = shareLinkId;
        Email = email;
        FirstVerifiedAt = firstVerifiedAt;
    }

    /// <summary>Gets the link.</summary>
    public Guid ShareLinkId { get; }

    /// <summary>Gets the forwarded email.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets when the email first verified on the link (UTC).</summary>
    public DateTimeOffset FirstVerifiedAt { get; }

    /// <summary>Gets the admin's decision so far.</summary>
    public ForwardStatus Status { get; private set; } = ForwardStatus.Pending;

    /// <summary>Gets when an admin decided (UTC), or <see langword="null"/> while pending.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Gets the admin who decided, or <see langword="null"/> while pending.</summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>Gets whether the link opens for the email: only once kept.</summary>
    public bool OpensLink => Status == ForwardStatus.Kept;

    /// <summary>Records a new forward, pending, and raises <see cref="LinkForwarded"/>.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The forwarded email.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The forward.</returns>
    public static LinkForward Record(Guid shareLinkId, EmailAddress email, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        var forward = new LinkForward(Guid.CreateVersion7(now), shareLinkId, email, now);
        forward.RaiseDomainEvent(new LinkForwarded(forward.Id));
        return forward;
    }

    /// <summary>Keeps or blocks the forward, unless an admin already decided.</summary>
    /// <param name="keep"><see langword="true"/> to keep, <see langword="false"/> to block.</param>
    /// <param name="adminUserId">The admin.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="true"/> when this was the decision; <see langword="false"/> when one was made before.</returns>
    public bool Decide(bool keep, Guid adminUserId, DateTimeOffset now)
    {
        if (Status != ForwardStatus.Pending)
        {
            return false;
        }

        Status = keep ? ForwardStatus.Kept : ForwardStatus.Blocked;
        DecidedAt = now;
        DecidedByUserId = adminUserId;
        return true;
    }
}
