using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Links;

/// <summary>What checking a typed code found.</summary>
public enum EmailCodeCheck
{
    /// <summary>The right code, in time: the email is verified.</summary>
    Verified,

    /// <summary>A wrong code; tries are left.</summary>
    Wrong,

    /// <summary>No tries are left; a new code is needed.</summary>
    Exhausted,

    /// <summary>The code ran out, or was never sent; a new code is needed.</summary>
    Expired,

    /// <summary>The code already opened the link once.</summary>
    Used,
}

/// <summary>Raised when a viewer asks for a code; the job that hears it draws the code and mails it.</summary>
/// <param name="ChallengeId">The challenge.</param>
public sealed record EmailCodeRequested(Guid ChallengeId) : IDomainEvent;

/// <summary>
/// One emailed code behind the email gate (L-2): asked for by a viewer of a link, then issued by the background job
/// that mails it, so the code itself never passes through the request. It is valid 10 minutes and 5 tries, and opens
/// once. Only a salted keyed hash of the code is kept (decisions §1b).
/// </summary>
public sealed class EmailChallenge : AggregateRoot<Guid>
{
    /// <summary>How many digits a code has.</summary>
    public const int CodeLength = 6;

    /// <summary>How many wrong tries spend a code.</summary>
    public const int MaxAttempts = 5;

    /// <summary>How long a code is valid after it is sent.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long a viewer waits before asking for another code.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(60);

    /// <summary>Initializes a challenge; use <see cref="Request"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="shareLinkId">The link the code opens.</param>
    /// <param name="email">The email the code goes to.</param>
    /// <param name="isForward">Whether the email is not the recipient's.</param>
    /// <param name="requestedAt">When it was asked for (UTC).</param>
    private EmailChallenge(Guid id, Guid shareLinkId, EmailAddress email, bool isForward, DateTimeOffset requestedAt)
        : base(id)
    {
        ShareLinkId = shareLinkId;
        Email = email;
        IsForward = isForward;
        RequestedAt = requestedAt;
    }

    /// <summary>Gets the link the code opens.</summary>
    public Guid ShareLinkId { get; }

    /// <summary>Gets the email the code goes to.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets whether the email is not one of the recipient's (a forward, L-3).</summary>
    public bool IsForward { get; }

    /// <summary>Gets when the code was asked for (UTC).</summary>
    public DateTimeOffset RequestedAt { get; }

    /// <summary>Gets the salted keyed hash of the code, or <see langword="null"/> before it is sent.</summary>
    public string? CodeHash { get; private set; }

    /// <summary>Gets when the code stops working (UTC), or <see langword="null"/> before it is sent.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Gets how many wrong codes were typed.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>Gets when the right code was typed (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Gets when another code may be asked for.</summary>
    public DateTimeOffset ResendAt => RequestedAt + ResendAfter;

    /// <summary>Gets how many tries are left.</summary>
    public int AttemptsLeft => Math.Max(0, MaxAttempts - FailedAttempts);

    /// <summary>Records a request for a code and raises <see cref="EmailCodeRequested"/>.</summary>
    /// <param name="shareLinkId">The link the code opens.</param>
    /// <param name="email">An email the link admits.</param>
    /// <param name="isForward">Whether the email is not the recipient's.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The challenge, without a code yet.</returns>
    public static EmailChallenge Request(Guid shareLinkId, EmailAddress email, bool isForward, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        var challenge = new EmailChallenge(Guid.CreateVersion7(now), shareLinkId, email, isForward, now);
        challenge.RaiseDomainEvent(new EmailCodeRequested(challenge.Id));
        return challenge;
    }

    /// <summary>
    /// Stores the hash of a freshly drawn code and starts its 10 minutes; a retried job replaces the code before it.
    /// </summary>
    /// <param name="codeHash">The salted keyed hash of the code.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="false"/> when the challenge was used, or is too old to be worth sending.</returns>
    public bool IssueCode(string codeHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        if (VerifiedAt is not null || now >= RequestedAt + CodeLifetime)
        {
            return false;
        }

        CodeHash = codeHash;
        ExpiresAt = now + CodeLifetime;
        FailedAttempts = 0;
        return true;
    }

    /// <summary>Checks a typed code. A wrong code uses a try; expired, spent and used codes use none.</summary>
    /// <param name="codeMatches">Whether the typed code matches <see cref="CodeHash"/>.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>What the check found.</returns>
    public EmailCodeCheck Check(bool codeMatches, DateTimeOffset now)
    {
        if (VerifiedAt is not null)
        {
            return EmailCodeCheck.Used;
        }

        if (FailedAttempts >= MaxAttempts)
        {
            return EmailCodeCheck.Exhausted;
        }

        if (CodeHash is null || now >= ExpiresAt)
        {
            return EmailCodeCheck.Expired;
        }

        if (!codeMatches)
        {
            FailedAttempts++;
            return FailedAttempts >= MaxAttempts ? EmailCodeCheck.Exhausted : EmailCodeCheck.Wrong;
        }

        VerifiedAt = now;
        return EmailCodeCheck.Verified;
    }
}
