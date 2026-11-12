using System.Globalization;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>
/// The <c>Notifications</c> configuration section (<c>Notifications__*</c> in <c>.env</c>): the bot notice rules (T-8).
/// The defaults are the owner's: one message per member every 10 minutes, quiet from 23:00 to 08:00 Tehran time.
/// </summary>
public sealed record NotificationOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Notifications";

    /// <summary>
    /// Fixed-offset stand-ins for zones the host may not know: Windows hosts built with invariant globalization cannot
    /// map IANA ids. Iran has kept UTC+3:30 all year since it dropped daylight saving time in 2022.
    /// </summary>
    private static readonly Dictionary<string, TimeSpan> _fixedZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Tehran"] = TimeSpan.FromHours(3.5),
    };

    /// <summary>Gets the least minutes between two notice messages to one member (1 to 1440).</summary>
    public int ThrottleMinutes { get; init; } = 10;

    /// <summary>Gets when the quiet hours start, as <c>HH:mm</c> local time.</summary>
    public string QuietHoursStart { get; init; } = "23:00";

    /// <summary>Gets when the quiet hours end, as <c>HH:mm</c> local time; the same as the start turns them off.</summary>
    public string QuietHoursEnd { get; init; } = "08:00";

    /// <summary>Gets the IANA time zone the quiet hours are in.</summary>
    public string TimeZone { get; init; } = "Asia/Tehran";

    /// <summary>Checks the settings, for the start-up validation.</summary>
    /// <returns><see langword="true"/> when they make a valid set of rules.</returns>
    public bool IsValid()
    {
        try
        {
            ToRules();
            return true;
        }
        catch (Exception exception) when (exception is DomainException or FormatException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>Builds the rules.</summary>
    /// <returns>The rules.</returns>
    /// <exception cref="FormatException">A time is not <c>HH:mm</c>.</exception>
    /// <exception cref="TimeZoneNotFoundException">The time zone is unknown.</exception>
    /// <exception cref="DomainException"><c>notice.rules_invalid</c> for a throttle out of range.</exception>
    public NotificationRules ToRules() => NotificationRules.Create(
        TimeSpan.FromMinutes(ThrottleMinutes),
        TimeOnly.ParseExact(QuietHoursStart, "HH:mm", CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(QuietHoursEnd, "HH:mm", CultureInfo.InvariantCulture),
        ResolveTimeZone(TimeZone));

    /// <summary>Finds a time zone by id, falling back to a fixed offset for known zones the host cannot map.</summary>
    /// <param name="id">The IANA (or Windows) id.</param>
    /// <returns>The time zone.</returns>
    /// <exception cref="TimeZoneNotFoundException">The id is unknown.</exception>
    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (_fixedZones.TryGetValue(id, out var offset))
        {
            return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
        }
    }
}

/// <summary>Exposes the configured notification rules to the Application layer, built once.</summary>
/// <param name="options">The settings, checked at start-up.</param>
internal sealed class ConfiguredNotificationSettings(IOptions<NotificationOptions> options) : INotificationSettings
{
    /// <summary>The rules, built on first use.</summary>
    private readonly Lazy<NotificationRules> _rules = new(() => options.Value.ToRules());

    /// <summary>Gets the rules.</summary>
    public NotificationRules Rules => _rules.Value;
}
