using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Accounts;

/// <summary>Who a magic-link sign-in is for (decisions §7a).</summary>
public enum SignInAudience
{
    /// <summary>A team admin signing in to the console (7-day session).</summary>
    Admin,

    /// <summary>A viewer signing in from the public home page (1-day session).</summary>
    Viewer,
}

/// <summary>
/// A one-time sign-in link sent by email (A-1, P-5). It is issued with no token; the email job draws the token and arms
/// the link with the token's keyed hash, so the token never sits in a request, a job argument or a log. It works once,
/// for 15 minutes after it was armed.
/// </summary>
public sealed class SignInLink : AggregateRoot<Guid>
{
    /// <summary>How long a link works after it was armed (proposed).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>Initializes a link; use <see cref="Issue"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="audience">Who it is for.</param>
    /// <param name="email">The address it is sent to.</param>
    /// <param name="createdAt">When it was asked for (UTC).</param>
    private SignInLink(Guid id, SignInAudience audience, EmailAddress email, DateTimeOffset createdAt)
        : base(id)
    {
        Audience = audience;
        Email = email;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    /// <summary>Gets who the link is for.</summary>
    public SignInAudience Audience { get; }

    /// <summary>Gets the address the link is sent to.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets the keyed hash of the token, or <see langword="null"/> until the link is armed.</summary>
    public string? TokenHash { get; private set; }

    /// <summary>Gets when the link was asked for (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets when the link stops working (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets when the link was used (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Asks for a link.</summary>
    /// <param name="audience">Who it is for.</param>
    /// <param name="email">The address.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The link, not yet armed.</returns>
    public static SignInLink Issue(SignInAudience audience, EmailAddress email, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        return new SignInLink(Guid.CreateVersion7(now), audience, email, now);
    }

    /// <summary>Stores the token's hash and starts the 15 minutes; a retried email job arms again with a new token.</summary>
    /// <param name="tokenHash">The keyed hash of the token.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="false"/> when the link was used or was asked for too long ago.</returns>
    public bool Arm(string tokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (UsedAt is not null || now >= CreatedAt + Lifetime)
        {
            return false;
        }

        TokenHash = tokenHash;
        ExpiresAt = now + Lifetime;
        return true;
    }

    /// <summary>Uses the link.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="true"/> when the link was armed, unused and in time.</returns>
    public bool Redeem(DateTimeOffset now)
    {
        if (TokenHash is null || UsedAt is not null || now >= ExpiresAt)
        {
            return false;
        }

        UsedAt = now;
        return true;
    }
}

/// <summary>
/// A signed-in session (A-1, P-5): 7 days for an admin, 1 day for a viewer. Only the session token's keyed hash is
/// stored; the web app keeps the token in a <c>__Host-</c> cookie and sends it in a header.
/// </summary>
public sealed class AccountSession : AggregateRoot<Guid>
{
    /// <summary>Initializes a session; use <see cref="Start"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="audience">Who it is for.</param>
    /// <param name="subjectId">The user (admin) or viewer account.</param>
    /// <param name="email">The signed-in email.</param>
    /// <param name="tokenHash">The keyed hash of the session token.</param>
    /// <param name="createdAt">When it started (UTC).</param>
    private AccountSession(Guid id, SignInAudience audience, Guid subjectId, EmailAddress email, string tokenHash, DateTimeOffset createdAt)
        : base(id)
    {
        Audience = audience;
        SubjectId = subjectId;
        Email = email;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + LifetimeOf(audience);
    }

    /// <summary>Gets who the session is for.</summary>
    public SignInAudience Audience { get; }

    /// <summary>Gets the user id (admin) or viewer account id.</summary>
    public Guid SubjectId { get; }

    /// <summary>Gets the signed-in email.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets the keyed hash of the session token.</summary>
    public string TokenHash { get; }

    /// <summary>Gets when the session started (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets when the session runs out (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Gets when the person signed out (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Gets how long a session lasts: 7 days for an admin, 1 day for a viewer (decisions §7a).</summary>
    /// <param name="audience">Who it is for.</param>
    /// <returns>The lifetime.</returns>
    public static TimeSpan LifetimeOf(SignInAudience audience) =>
        audience == SignInAudience.Admin ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);

    /// <summary>Starts a session.</summary>
    /// <param name="audience">Who it is for.</param>
    /// <param name="subjectId">The user or viewer account.</param>
    /// <param name="email">The signed-in email.</param>
    /// <param name="tokenHash">The keyed hash of the session token.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The session.</returns>
    public static AccountSession Start(SignInAudience audience, Guid subjectId, EmailAddress email, string tokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentNullException.ThrowIfNull(email);
        if (subjectId == Guid.Empty)
        {
            throw new ArgumentException("A session needs a subject.", nameof(subjectId));
        }

        return new AccountSession(Guid.CreateVersion7(now), audience, subjectId, email, tokenHash, now);
    }

    /// <summary>Tells whether the session is still on.</summary>
    /// <param name="now">The moment (UTC).</param>
    /// <returns><see langword="true"/> before it ends or runs out.</returns>
    public bool IsActiveAt(DateTimeOffset now) => EndedAt is null && now < ExpiresAt;

    /// <summary>Ends the session (sign-out); the first sign-out stands.</summary>
    /// <param name="now">The current time (UTC).</param>
    public void End(DateTimeOffset now) => EndedAt ??= now;
}

/// <summary>A viewer who signed in by email at least once (P-5). Viewers have no dashboard; the account only remembers who signed in.</summary>
public sealed class ViewerAccount : AggregateRoot<Guid>
{
    /// <summary>Initializes an account; use <see cref="Register"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="email">The email.</param>
    /// <param name="createdAt">When it was made (UTC).</param>
    private ViewerAccount(Guid id, EmailAddress email, DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        CreatedAt = createdAt;
        LastSignedInAt = createdAt;
    }

    /// <summary>Gets the email.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets when the account was made (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the latest sign-in (UTC).</summary>
    public DateTimeOffset LastSignedInAt { get; private set; }

    /// <summary>Makes the account on a viewer's first sign-in.</summary>
    /// <param name="email">The email.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The account.</returns>
    public static ViewerAccount Register(EmailAddress email, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        return new ViewerAccount(Guid.CreateVersion7(now), email, now);
    }

    /// <summary>Remembers a later sign-in.</summary>
    /// <param name="now">The current time (UTC).</param>
    public void SignedIn(DateTimeOffset now) => LastSignedInAt = now;
}
