using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.AccessRequests;

/// <summary>Raised when a visitor asks for a code; the job that hears it draws the code and mails it.</summary>
/// <param name="ChallengeId">The challenge.</param>
public sealed record AccessCodeRequested(Guid ChallengeId) : IDomainEvent;

/// <summary>
/// One emailed code a visitor types before asking for access (P-8): the link gate's rules (6 digits, 10 minutes,
/// 5 tries, 60 seconds before another) but tied to no link. Only a salted keyed hash of the code is kept.
/// </summary>
public sealed class AccessCodeChallenge : AggregateRoot<Guid>
{
    /// <summary>How many digits a code has.</summary>
    public const int CodeLength = EmailChallenge.CodeLength;

    /// <summary>How many wrong tries spend a code.</summary>
    public const int MaxAttempts = EmailChallenge.MaxAttempts;

    /// <summary>How long a code is valid after it is sent.</summary>
    public static readonly TimeSpan CodeLifetime = EmailChallenge.CodeLifetime;

    /// <summary>How long a visitor waits before asking for another code.</summary>
    public static readonly TimeSpan ResendAfter = EmailChallenge.ResendAfter;

    /// <summary>Initializes a challenge; use <see cref="Request"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="email">The email the code goes to.</param>
    /// <param name="requestedAt">When it was asked for (UTC).</param>
    private AccessCodeChallenge(Guid id, EmailAddress email, DateTimeOffset requestedAt)
        : base(id)
    {
        Email = email;
        RequestedAt = requestedAt;
    }

    /// <summary>Gets the email the code goes to.</summary>
    public EmailAddress Email { get; }

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

    /// <summary>Records a request for a code and raises <see cref="AccessCodeRequested"/>.</summary>
    /// <param name="email">The visitor's email.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The challenge, without a code yet.</returns>
    public static AccessCodeChallenge Request(EmailAddress email, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        var challenge = new AccessCodeChallenge(Guid.CreateVersion7(now), email, now);
        challenge.RaiseDomainEvent(new AccessCodeRequested(challenge.Id));
        return challenge;
    }

    /// <summary>Stores the hash of a freshly drawn code and starts its 10 minutes.</summary>
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
