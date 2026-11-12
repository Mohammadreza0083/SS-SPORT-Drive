using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.AccessRequests;

/// <summary>How a request's email was proven.</summary>
public enum AccessRequestSource
{
    /// <summary>The visitor typed a code mailed to the email.</summary>
    EmailCode,

    /// <summary>The email came from a signed-in viewer's session.</summary>
    SignedIn,
}

/// <summary>Where a request stands; P-4 adds the decisions.</summary>
public enum AccessRequestStatus
{
    /// <summary>Waiting for an admin.</summary>
    Pending,

    /// <summary>An admin approved it.</summary>
    Approved,

    /// <summary>An admin declined it.</summary>
    Declined,
}

/// <summary>Raised when a request is made; the admin inbox and the bot alert (P-4) listen to it.</summary>
/// <param name="AccessRequestId">The request.</param>
public sealed record AccessRequested(Guid AccessRequestId) : IDomainEvent;

/// <summary>A visitor's request to see a locked player (P-3, P-7, P-8): a verified email, the player and a description.</summary>
public sealed class AccessRequest : AggregateRoot<Guid>
{
    /// <summary>The longest description accepted.</summary>
    public const int MaxAboutLength = 1000;

    /// <summary>The code clients see for a bad description.</summary>
    public const string AboutInvalidCode = "access_request.about_invalid";

    /// <summary>Initializes a request; use <see cref="Submit"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="playerId">The player asked for.</param>
    /// <param name="email">The verified email.</param>
    /// <param name="about">The description.</param>
    /// <param name="source">How the email was proven.</param>
    /// <param name="createdAt">When it was made (UTC).</param>
    private AccessRequest(Guid id, Guid playerId, EmailAddress email, string about, AccessRequestSource source, DateTimeOffset createdAt)
        : base(id)
    {
        PlayerId = playerId;
        Email = email;
        About = about;
        Source = source;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the player asked for.</summary>
    public Guid PlayerId { get; }

    /// <summary>Gets the verified email.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets what the visitor wrote about themselves.</summary>
    public string About { get; }

    /// <summary>Gets how the email was proven.</summary>
    public AccessRequestSource Source { get; }

    /// <summary>Gets where the request stands.</summary>
    public AccessRequestStatus Status { get; private set; } = AccessRequestStatus.Pending;

    /// <summary>Gets when it was made (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Makes a pending request and raises <see cref="AccessRequested"/>.</summary>
    /// <param name="playerId">The player asked for.</param>
    /// <param name="email">The verified email.</param>
    /// <param name="about">The description, 1 to 1000 characters after trimming.</param>
    /// <param name="source">How the email was proven.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The request.</returns>
    /// <exception cref="DomainException"><c>access_request.about_invalid</c>.</exception>
    public static AccessRequest Submit(Guid playerId, EmailAddress email, string? about, AccessRequestSource source, DateTimeOffset now)
    {
        if (playerId == Guid.Empty)
        {
            throw new ArgumentException("A request needs a player.", nameof(playerId));
        }

        ArgumentNullException.ThrowIfNull(email);
        var text = about?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxAboutLength)
        {
            throw new DomainException(AboutInvalidCode, $"Describe yourself in 1 to {MaxAboutLength} characters.");
        }

        var request = new AccessRequest(Guid.CreateVersion7(now), playerId, email, text, source, now);
        request.RaiseDomainEvent(new AccessRequested(request.Id));
        return request;
    }
}
