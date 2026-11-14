using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Settings;

/// <summary>Raised when an admin changed the settings.</summary>
/// <param name="ChangedFields">The camelCase names of the fields that changed, in a fixed order.</param>
public sealed record AppSettingsChanged(IReadOnlyList<string> ChangedFields) : IDomainEvent;

/// <summary>
/// The console's global settings (A-10, owner 2026-11-14): one row in the database; configuration only gives the first
/// values. Holds the contact email shown on every player page, the daily digest time and the quiet hours (Tehran wall
/// clock) and the default expiry of a new share link (7, 30 or 90 days; every link expires).
/// </summary>
public sealed class AppSettings : AggregateRoot<Guid>
{
    /// <summary>The fixed identifier of the single settings row.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Initializes the settings; use <see cref="CreateDefault"/>.</summary>
    /// <param name="id">The identifier (always <see cref="SingletonId"/>).</param>
    /// <param name="contactEmail">The contact email.</param>
    /// <param name="digestAt">The digest time.</param>
    /// <param name="quietHoursStart">The quiet hours start.</param>
    /// <param name="quietHoursEnd">The quiet hours end.</param>
    /// <param name="defaultLinkExpiryDays">The default link expiry in days.</param>
    /// <param name="updatedAt">The creation time (UTC).</param>
    private AppSettings(Guid id, EmailAddress contactEmail, TimeOnly digestAt, TimeOnly quietHoursStart, TimeOnly quietHoursEnd, int defaultLinkExpiryDays, DateTimeOffset updatedAt)
        : base(id)
    {
        ContactEmail = contactEmail;
        DigestAt = digestAt;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        DefaultLinkExpiryDays = defaultLinkExpiryDays;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the days a new share link may last.</summary>
    public static IReadOnlyList<int> LinkExpiryChoices { get; } = [7, 30, 90];

    /// <summary>Gets the email shown on every player page.</summary>
    public EmailAddress ContactEmail { get; private set; }

    /// <summary>Gets when the daily digest goes out (Tehran wall clock).</summary>
    public TimeOnly DigestAt { get; private set; }

    /// <summary>Gets when quiet hours start (Tehran wall clock).</summary>
    public TimeOnly QuietHoursStart { get; private set; }

    /// <summary>Gets when quiet hours end (Tehran wall clock).</summary>
    public TimeOnly QuietHoursEnd { get; private set; }

    /// <summary>Gets the expiry the New share link modal preselects, in days.</summary>
    public int DefaultLinkExpiryDays { get; private set; }

    /// <summary>Gets when the settings last changed (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the admin who last changed them, or <see langword="null"/> for the configured defaults.</summary>
    public Guid? UpdatedByUserId { get; private set; }

    /// <summary>Creates the settings from the configured first values.</summary>
    /// <param name="contactEmail">The contact email.</param>
    /// <param name="digestAt">The digest time.</param>
    /// <param name="quietStart">The quiet hours start.</param>
    /// <param name="quietEnd">The quiet hours end.</param>
    /// <param name="defaultLinkExpiryDays">The default link expiry in days.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>The settings.</returns>
    /// <exception cref="DomainException">A value breaks a rule.</exception>
    public static AppSettings CreateDefault(EmailAddress contactEmail, TimeOnly digestAt, TimeOnly quietStart, TimeOnly quietEnd, int defaultLinkExpiryDays, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(contactEmail);
        Validate(digestAt, quietStart, quietEnd, defaultLinkExpiryDays);
        return new AppSettings(SingletonId, contactEmail, digestAt, quietStart, quietEnd, defaultLinkExpiryDays, now);
    }

    /// <summary>Changes the settings and reports what changed.</summary>
    /// <param name="contactEmail">The contact email.</param>
    /// <param name="digestAt">The digest time.</param>
    /// <param name="quietStart">The quiet hours start.</param>
    /// <param name="quietEnd">The quiet hours end.</param>
    /// <param name="defaultLinkExpiryDays">The default link expiry in days.</param>
    /// <param name="actorUserId">The admin, or <see langword="null"/>.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>The camelCase names of the changed fields; empty when nothing changed.</returns>
    /// <exception cref="DomainException">A value breaks a rule.</exception>
    public IReadOnlyList<string> Update(EmailAddress contactEmail, TimeOnly digestAt, TimeOnly quietStart, TimeOnly quietEnd, int defaultLinkExpiryDays, Guid? actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(contactEmail);
        Validate(digestAt, quietStart, quietEnd, defaultLinkExpiryDays);
        var changed = new List<string>(5);
        if (contactEmail != ContactEmail)
        {
            changed.Add("contactEmail");
        }

        if (digestAt != DigestAt)
        {
            changed.Add("digestAt");
        }

        if (quietStart != QuietHoursStart)
        {
            changed.Add("quietHoursStart");
        }

        if (quietEnd != QuietHoursEnd)
        {
            changed.Add("quietHoursEnd");
        }

        if (defaultLinkExpiryDays != DefaultLinkExpiryDays)
        {
            changed.Add("defaultLinkExpiryDays");
        }

        if (changed.Count == 0)
        {
            return changed;
        }

        (ContactEmail, DigestAt, QuietHoursStart, QuietHoursEnd, DefaultLinkExpiryDays) = (contactEmail, digestAt, quietStart, quietEnd, defaultLinkExpiryDays);
        UpdatedAt = now;
        UpdatedByUserId = actorUserId;
        RaiseDomainEvent(new AppSettingsChanged(changed));
        return changed;
    }

    /// <summary>Checks the rules shared by creating and updating.</summary>
    /// <param name="digestAt">The digest time.</param>
    /// <param name="quietStart">The quiet hours start.</param>
    /// <param name="quietEnd">The quiet hours end.</param>
    /// <param name="expiryDays">The default link expiry in days.</param>
    private static void Validate(TimeOnly digestAt, TimeOnly quietStart, TimeOnly quietEnd, int expiryDays)
    {
        if (!LinkExpiryChoices.Contains(expiryDays))
        {
            throw new DomainException("settings.invalid_link_expiry", "A share link lasts 7, 30 or 90 days.");
        }

        foreach (var time in new[] { digestAt, quietStart, quietEnd })
        {
            if (time.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                throw new DomainException("settings.invalid_time", "Times are whole minutes.");
            }
        }
    }
}
