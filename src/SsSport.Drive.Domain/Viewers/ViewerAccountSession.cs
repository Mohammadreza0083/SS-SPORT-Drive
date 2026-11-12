using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Viewers;

/// <summary>
/// A signed-in viewer's session on the public site (decisions §7a: one day). P-5 starts it from the magic link; P-8
/// reads it and ends it. Only the token's keyed hash is stored.
/// </summary>
public sealed class ViewerAccountSession : AggregateRoot<Guid>
{
    /// <summary>How long a session lasts.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>Initializes a session; use <see cref="Start"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="tokenHash">The keyed hash of the session token.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="startedAt">When it started (UTC).</param>
    private ViewerAccountSession(Guid id, string tokenHash, EmailAddress email, DateTimeOffset startedAt)
        : base(id)
    {
        TokenHash = tokenHash;
        Email = email;
        StartedAt = startedAt;
        ExpiresAt = startedAt + Lifetime;
    }

    /// <summary>Gets the keyed hash of the session token.</summary>
    public string TokenHash { get; }

    /// <summary>Gets the viewer's email.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets when the session started (UTC).</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets when the session ends (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Gets when the viewer signed out (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? SignedOutAt { get; private set; }

    /// <summary>Starts a session.</summary>
    /// <param name="tokenHash">The keyed hash of the session token.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The session.</returns>
    public static ViewerAccountSession Start(string tokenHash, EmailAddress email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentNullException.ThrowIfNull(email);
        return new ViewerAccountSession(Guid.CreateVersion7(now), tokenHash, email, now);
    }

    /// <summary>Tells whether the session is on: not ended, not signed out.</summary>
    /// <param name="now">The moment (UTC).</param>
    /// <returns><see langword="true"/> while the viewer may use it.</returns>
    public bool IsLiveAt(DateTimeOffset now) => SignedOutAt is null && now < ExpiresAt;

    /// <summary>Ends the session now; signing out twice keeps the first time.</summary>
    /// <param name="now">The current time (UTC).</param>
    public void SignOut(DateTimeOffset now) => SignedOutAt ??= now;
}
