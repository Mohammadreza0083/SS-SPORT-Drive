using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Links;

/// <summary>How a link treats an email typed at the gate.</summary>
public enum EmailAdmission
{
    /// <summary>One of the recipient's own emails.</summary>
    Recipient,

    /// <summary>Another email the link's forwarding switches allow (L-3).</summary>
    Forward,

    /// <summary>An email the link does not open for.</summary>
    Refused,
}

/// <summary>
/// A browser trusted to open one link (L-2), for 90 days after its email was verified there. The browser keeps one
/// device token (the <c>__Host-ssd_dev</c> cookie) and gets one row per link it verified; only the token's keyed hash is
/// stored.
/// </summary>
public sealed class TrustedDevice : AggregateRoot<Guid>
{
    /// <summary>How long a verified device is remembered (the canvas's "90 days").</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>Initializes a device; use <see cref="Trust"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="tokenHash">The keyed hash of the device token.</param>
    /// <param name="shareLinkId">The link it may open.</param>
    /// <param name="email">The verified email.</param>
    /// <param name="trustedAt">When it was verified (UTC).</param>
    private TrustedDevice(Guid id, string tokenHash, Guid shareLinkId, EmailAddress email, DateTimeOffset trustedAt)
        : base(id)
    {
        TokenHash = tokenHash;
        ShareLinkId = shareLinkId;
        Email = email;
        TrustedAt = trustedAt;
        LastSeenAt = trustedAt;
        ExpiresAt = trustedAt + Lifetime;
    }

    /// <summary>Gets the keyed hash of the device token.</summary>
    public string TokenHash { get; }

    /// <summary>Gets the link the device may open.</summary>
    public Guid ShareLinkId { get; }

    /// <summary>Gets the email verified on this device.</summary>
    public EmailAddress Email { get; private set; }

    /// <summary>Gets whether that email is a forward.</summary>
    public bool IsForward { get; private set; }

    /// <summary>Gets when the email was last verified here (UTC).</summary>
    public DateTimeOffset TrustedAt { get; private set; }

    /// <summary>Gets when the device stops being trusted (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets when the device last opened the link (UTC).</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Trusts a device for a link after its email was verified.</summary>
    /// <param name="tokenHash">The keyed hash of the device token.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The verified email.</param>
    /// <param name="isForward">Whether the email is a forward.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The trusted device.</returns>
    public static TrustedDevice Trust(string tokenHash, Guid shareLinkId, EmailAddress email, bool isForward, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentNullException.ThrowIfNull(email);
        return new TrustedDevice(Guid.CreateVersion7(now), tokenHash, shareLinkId, email, now) { IsForward = isForward };
    }

    /// <summary>Records a new verification on the same device: the email may change and the 90 days start again.</summary>
    /// <param name="email">The verified email.</param>
    /// <param name="isForward">Whether the email is a forward.</param>
    /// <param name="now">The current time (UTC).</param>
    public void Retrust(EmailAddress email, bool isForward, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        Email = email;
        IsForward = isForward;
        TrustedAt = now;
        LastSeenAt = now;
        ExpiresAt = now + Lifetime;
    }

    /// <summary>Tells whether the device is still trusted.</summary>
    /// <param name="now">The moment (UTC).</param>
    /// <returns><see langword="true"/> before <see cref="ExpiresAt"/>.</returns>
    public bool IsTrustedAt(DateTimeOffset now) => now < ExpiresAt;

    /// <summary>Records that the device opened the link.</summary>
    /// <param name="now">The current time (UTC).</param>
    public void Seen(DateTimeOffset now) => LastSeenAt = now;
}

/// <summary>
/// A viewer's session on one link (L-2): one day long (decisions §7a), carried by the <c>__Host-ssd_vs</c> cookie that
/// the HLS edge tokens are bound to. It remembers the email shown over the videos. Only the token's keyed hash is stored.
/// </summary>
public sealed class ViewerSession : AggregateRoot<Guid>
{
    /// <summary>How long a session lasts.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>Initializes a session; use <see cref="Start"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="tokenHash">The keyed hash of the session token.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="createdAt">When it started (UTC).</param>
    private ViewerSession(Guid id, string tokenHash, Guid shareLinkId, EmailAddress email, DateTimeOffset createdAt)
        : base(id)
    {
        TokenHash = tokenHash;
        ShareLinkId = shareLinkId;
        Email = email;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    /// <summary>Gets the keyed hash of the session token.</summary>
    public string TokenHash { get; }

    /// <summary>Gets the link the session opens.</summary>
    public Guid ShareLinkId { get; }

    /// <summary>Gets the viewer's email, laid over the videos (V-4).</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets whether the email is a forward.</summary>
    public bool IsForward { get; private init; }

    /// <summary>Gets the trusted device the session came from, or <see langword="null"/> for links that skip the gate.</summary>
    public Guid? DeviceId { get; private init; }

    /// <summary>Gets when the session started (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets when the session ends (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Starts a session.</summary>
    /// <param name="tokenHash">The keyed hash of the session token.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="isForward">Whether the email is a forward.</param>
    /// <param name="deviceId">The trusted device, or <see langword="null"/>.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The session.</returns>
    public static ViewerSession Start(string tokenHash, Guid shareLinkId, EmailAddress email, bool isForward, Guid? deviceId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentNullException.ThrowIfNull(email);
        return new ViewerSession(Guid.CreateVersion7(now), tokenHash, shareLinkId, email, now) { IsForward = isForward, DeviceId = deviceId };
    }

    /// <summary>Tells whether the session is still on.</summary>
    /// <param name="now">The moment (UTC).</param>
    /// <returns><see langword="true"/> before <see cref="ExpiresAt"/>.</returns>
    public bool IsActiveAt(DateTimeOffset now) => now < ExpiresAt;
}
