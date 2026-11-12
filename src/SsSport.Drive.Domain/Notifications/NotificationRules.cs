using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Notifications;

/// <summary>
/// When a bot notice may reach a team member (T-8, owner 2026-11-12): at most one message every
/// <see cref="Throttle"/>, and nothing in the quiet hours (by default 23:00 to 08:00 Tehran time). Notices that may not
/// go out yet are held and sent together when they may.
/// </summary>
public sealed class NotificationRules
{
    /// <summary>The longest throttle that makes sense.</summary>
    private static readonly TimeSpan _maxThrottle = TimeSpan.FromDays(1);

    /// <summary>Initializes the rules.</summary>
    /// <param name="throttle">The least time between two messages to one recipient.</param>
    /// <param name="quietStart">When the quiet hours start (local time).</param>
    /// <param name="quietEnd">When the quiet hours end (local time).</param>
    /// <param name="timeZone">The time zone the quiet hours are in.</param>
    private NotificationRules(TimeSpan throttle, TimeOnly quietStart, TimeOnly quietEnd, TimeZoneInfo timeZone)
    {
        Throttle = throttle;
        QuietStart = quietStart;
        QuietEnd = quietEnd;
        TimeZone = timeZone;
    }

    /// <summary>Gets the least time between two messages to one recipient.</summary>
    public TimeSpan Throttle { get; }

    /// <summary>Gets when the quiet hours start, in <see cref="TimeZone"/>.</summary>
    public TimeOnly QuietStart { get; }

    /// <summary>Gets when the quiet hours end, in <see cref="TimeZone"/>; equal to the start means no quiet hours.</summary>
    public TimeOnly QuietEnd { get; }

    /// <summary>Gets the time zone the quiet hours are in.</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>Creates the rules.</summary>
    /// <param name="throttle">The least time between two messages to one recipient (more than zero, at most a day).</param>
    /// <param name="quietStart">When the quiet hours start (local time).</param>
    /// <param name="quietEnd">When the quiet hours end (local time); the same as the start turns them off.</param>
    /// <param name="timeZone">The time zone the quiet hours are in.</param>
    /// <returns>The rules.</returns>
    /// <exception cref="DomainException"><c>notice.rules_invalid</c> for a throttle out of range.</exception>
    public static NotificationRules Create(TimeSpan throttle, TimeOnly quietStart, TimeOnly quietEnd, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (throttle <= TimeSpan.Zero || throttle > _maxThrottle)
        {
            throw new DomainException("notice.rules_invalid", "The notice throttle must be more than zero and at most a day.");
        }

        return new NotificationRules(throttle, quietStart, quietEnd, timeZone);
    }

    /// <summary>Tells whether an instant falls in the quiet hours (start included, end excluded).</summary>
    /// <param name="instant">The instant.</param>
    /// <returns><see langword="true"/> when no notice may go out.</returns>
    public bool IsQuiet(DateTimeOffset instant)
    {
        if (QuietStart == QuietEnd)
        {
            return false;
        }

        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);
        return QuietStart < QuietEnd
            ? local >= QuietStart && local < QuietEnd
            : local >= QuietStart || local < QuietEnd;
    }

    /// <summary>
    /// Works out when the next message to a recipient may go out: now, or once the throttle since the last message has
    /// passed, moved to the end of the quiet hours when it falls inside them.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="lastSentAt">When the recipient last got a notice message, or <see langword="null"/>.</param>
    /// <returns>The earliest time the message may go out.</returns>
    public DateTimeOffset NextSendTime(DateTimeOffset now, DateTimeOffset? lastSentAt)
    {
        var next = lastSentAt is { } last && last + Throttle > now ? last + Throttle : now;
        return IsQuiet(next) ? QuietEndAfter(next) : next;
    }

    /// <summary>Finds the first end of the quiet hours after an instant inside them.</summary>
    /// <param name="instant">An instant in the quiet hours.</param>
    /// <returns>The instant the quiet hours end.</returns>
    private DateTimeOffset QuietEndAfter(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TimeZone);
        var endToday = local.Date + QuietEnd.ToTimeSpan();
        var end = endToday > local.DateTime ? endToday : endToday.AddDays(1);
        return new DateTimeOffset(end, TimeZone.GetUtcOffset(end)).ToUniversalTime();
    }
}
