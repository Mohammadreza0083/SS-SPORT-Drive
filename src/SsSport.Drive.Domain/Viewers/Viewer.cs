using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Viewers;

/// <summary>
/// One email address that can watch (A-7, A-11): a recipient of a share link or a verified forward. The console
/// addresses viewers by this opaque id, so an email never travels in a URL. One row per address.
/// </summary>
public sealed class Viewer : AggregateRoot<Guid>
{
    /// <summary>Initializes a viewer; use <see cref="Register"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="email">The address.</param>
    /// <param name="firstSeenAt">When the address was first seen (UTC).</param>
    private Viewer(Guid id, EmailAddress email, DateTimeOffset firstSeenAt)
        : base(id)
    {
        Email = email;
        FirstSeenAt = firstSeenAt;
    }

    /// <summary>Gets the address.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets when the address was first seen (UTC).</summary>
    public DateTimeOffset FirstSeenAt { get; }

    /// <summary>Registers a viewer.</summary>
    /// <param name="email">The address.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The viewer.</returns>
    public static Viewer Register(EmailAddress email, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        return new Viewer(Guid.CreateVersion7(now), email, now);
    }
}
