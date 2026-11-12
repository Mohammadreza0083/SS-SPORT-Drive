using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Notifications;

/// <summary>
/// When a bot notice may go out (T-8): at most one message per recipient every 10 minutes, and nothing between 23:00
/// and 08:00 Tehran time; held notices go out when the quiet hours end.
/// </summary>
public sealed class NotificationRulesTests
{
    /// <summary>Tehran is UTC+3:30 all year (Iran dropped daylight saving time in 2022).</summary>
    private static readonly TimeSpan _tehran = TimeSpan.FromHours(3.5);

    /// <summary>The owner's rules: 10 minutes, 23:00 to 08:00 Tehran (built here, so no time zone database is needed).</summary>
    private static readonly NotificationRules _rules = NotificationRules.Create(
        TimeSpan.FromMinutes(10), new TimeOnly(23, 0), new TimeOnly(8, 0), TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", _tehran, "Tehran", "Tehran"));

    /// <summary>A Tehran wall-clock time on 12 November 2026.</summary>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="second">The second.</param>
    /// <returns>The instant.</returns>
    private static DateTimeOffset Tehran(int hour, int minute, int second = 0) => new(2026, 11, 12, hour, minute, second, _tehran);

    /// <summary>A first notice in the day goes out at once.</summary>
    [Fact]
    public void Nothing_sent_recently_goes_out_now()
    {
        _rules.NextSendTime(Tehran(12, 0), lastSentAt: null).Should().Be(Tehran(12, 0));
    }

    /// <summary>A notice soon after the last message waits until 10 minutes have passed.</summary>
    [Fact]
    public void Throttle_waits_ten_minutes_after_the_last_message()
    {
        _rules.NextSendTime(Tehran(12, 0), lastSentAt: Tehran(11, 55)).Should().Be(Tehran(12, 5));
    }

    /// <summary>A notice long after the last message goes out at once.</summary>
    [Fact]
    public void Old_last_message_does_not_delay()
    {
        _rules.NextSendTime(Tehran(12, 0), lastSentAt: Tehran(11, 0)).Should().Be(Tehran(12, 0));
    }

    /// <summary>Late in the evening, a notice is held until 08:00 the next morning.</summary>
    [Fact]
    public void Evening_notice_waits_for_the_morning()
    {
        _rules.NextSendTime(Tehran(23, 30), lastSentAt: null).Should().Be(Tehran(8, 0).AddDays(1));
    }

    /// <summary>After midnight, a notice is held until 08:00 the same day.</summary>
    [Fact]
    public void Night_notice_waits_until_eight()
    {
        _rules.NextSendTime(Tehran(2, 0), lastSentAt: null).Should().Be(Tehran(8, 0));
    }

    /// <summary>A throttled notice whose turn falls inside the quiet hours waits for the morning.</summary>
    [Fact]
    public void Throttle_into_quiet_hours_waits_for_the_morning()
    {
        _rules.NextSendTime(Tehran(22, 55), lastSentAt: Tehran(22, 50)).Should().Be(Tehran(8, 0).AddDays(1));
    }

    /// <summary>23:00 is already quiet; 08:00 is not any more; a second before 23:00 is not.</summary>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="second">The second.</param>
    /// <param name="quiet">Whether the time is quiet.</param>
    [Theory]
    [InlineData(23, 0, 0, true)]
    [InlineData(7, 59, 59, true)]
    [InlineData(0, 0, 0, true)]
    [InlineData(8, 0, 0, false)]
    [InlineData(22, 59, 59, false)]
    [InlineData(12, 0, 0, false)]
    public void Quiet_hours_bounds(int hour, int minute, int second, bool quiet)
    {
        _rules.IsQuiet(Tehran(hour, minute, second)).Should().Be(quiet);
    }

    /// <summary>A window that does not cross midnight works too.</summary>
    [Fact]
    public void Daytime_window_is_supported()
    {
        var lunch = NotificationRules.Create(TimeSpan.FromMinutes(10), new TimeOnly(13, 0), new TimeOnly(14, 0), TimeZoneInfo.Utc);
        var at = new DateTimeOffset(2026, 11, 12, 13, 30, 0, TimeSpan.Zero);

        lunch.IsQuiet(at).Should().BeTrue();
        lunch.IsQuiet(at.AddHours(1)).Should().BeFalse();
        lunch.NextSendTime(at, null).Should().Be(new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero));
    }

    /// <summary>The same start and end turns quiet hours off.</summary>
    [Fact]
    public void Equal_start_and_end_means_no_quiet_hours()
    {
        var always = NotificationRules.Create(TimeSpan.FromMinutes(10), new TimeOnly(0, 0), new TimeOnly(0, 0), TimeZoneInfo.Utc);

        always.IsQuiet(new DateTimeOffset(2026, 11, 12, 3, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    /// <summary>The throttle must be positive and at most a day.</summary>
    /// <param name="minutes">The throttle in minutes.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(24 * 60 + 1)]
    public void Throttle_must_be_sensible(int minutes)
    {
        var create = () => NotificationRules.Create(TimeSpan.FromMinutes(minutes), new TimeOnly(23, 0), new TimeOnly(8, 0), TimeZoneInfo.Utc);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("notice.rules_invalid");
    }
}
