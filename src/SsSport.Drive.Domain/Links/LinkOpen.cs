using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Links;

/// <summary>Raised when an email opens a link for the first time; admins are alerted in the bot when the link has "notify on open" on (L-7).</summary>
/// <param name="LinkOpenId">The record.</param>
public sealed record LinkOpened(Guid LinkOpenId) : IDomainEvent;

/// <summary>
/// The first time an email opened a link (L-7): one record per link and email, made when that email's first viewer
/// session starts. It is never purged, so a later session, even after the gate clean-up, never alerts again.
/// </summary>
public sealed class LinkOpen : AggregateRoot<Guid>
{
    /// <summary>Initializes a new instance of the <see cref="LinkOpen"/> class.</summary>
    /// <param name="id">The id.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="isForward">Whether the email was a forward.</param>
    /// <param name="firstOpenedAt">When it first opened.</param>
    private LinkOpen(Guid id, Guid shareLinkId, EmailAddress email, bool isForward, DateTimeOffset firstOpenedAt)
        : base(id)
    {
        ShareLinkId = shareLinkId;
        Email = email;
        IsForward = isForward;
        FirstOpenedAt = firstOpenedAt;
    }

    /// <summary>Gets the link that was opened.</summary>
    public Guid ShareLinkId { get; }

    /// <summary>Gets the email that opened it.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets a value indicating whether the email was a forward (not one of the recipient's own).</summary>
    public bool IsForward { get; }

    /// <summary>Gets when the email first opened the link.</summary>
    public DateTimeOffset FirstOpenedAt { get; }

    /// <summary>Records a first open and raises <see cref="LinkOpened"/>.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="isForward">Whether the email is a forward.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The record.</returns>
    public static LinkOpen Record(Guid shareLinkId, EmailAddress email, bool isForward, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        var open = new LinkOpen(Guid.CreateVersion7(now), shareLinkId, email, isForward, now);
        open.RaiseDomainEvent(new LinkOpened(open.Id));
        return open;
    }
}
