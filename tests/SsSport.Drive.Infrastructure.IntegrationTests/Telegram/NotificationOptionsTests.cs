using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Telegram;

/// <summary>The <c>Notifications</c> settings (T-8) default to the owner's rules and refuse values that make no sense.</summary>
public sealed class NotificationOptionsTests
{
    /// <summary>The defaults are 10 minutes and quiet from 23:00 to 08:00 Tehran time.</summary>
    [Fact]
    public void Defaults_are_the_owners_rules()
    {
        var options = new NotificationOptions();

        options.IsValid().Should().BeTrue();
        var rules = options.ToRules();
        rules.Throttle.Should().Be(TimeSpan.FromMinutes(10));
        rules.QuietStart.Should().Be(new TimeOnly(23, 0));
        rules.QuietEnd.Should().Be(new TimeOnly(8, 0));
        rules.IsQuiet(new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.FromHours(3.5))).Should().BeTrue();
    }

    /// <summary>A bad time zone, time or throttle is refused at start-up.</summary>
    /// <param name="throttle">The throttle in minutes.</param>
    /// <param name="start">The quiet hours start.</param>
    /// <param name="end">The quiet hours end.</param>
    /// <param name="zone">The time zone.</param>
    [Theory]
    [InlineData(10, "23:00", "08:00", "Mars/Olympus")]
    [InlineData(10, "25:00", "08:00", "Asia/Tehran")]
    [InlineData(10, "23:00", "eight", "Asia/Tehran")]
    [InlineData(0, "23:00", "08:00", "Asia/Tehran")]
    public void Bad_settings_are_refused(int throttle, string start, string end, string zone)
    {
        var options = new NotificationOptions { ThrottleMinutes = throttle, QuietHoursStart = start, QuietHoursEnd = end, TimeZone = zone };

        options.IsValid().Should().BeFalse();
    }
}
