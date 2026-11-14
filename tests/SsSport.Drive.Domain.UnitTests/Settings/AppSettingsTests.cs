using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Settings;

namespace SsSport.Drive.Domain.UnitTests.Settings;

/// <summary>
/// The console's global settings (A-10, owner 2026-11-14: one row in the database; configuration only gives the first
/// values): the contact email shown on every player page, the daily digest time and the quiet hours (both Tehran wall
/// clock), and the default expiry the New share link modal preselects (7, 30 or 90 days; never "no expiry").
/// </summary>
public sealed class AppSettingsTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The defaults are stored as given, on the one fixed id.</summary>
    [Fact]
    public void Defaults_are_stored_on_the_single_row()
    {
        var settings = Defaults();

        settings.Id.Should().Be(AppSettings.SingletonId);
        settings.ContactEmail.Value.Should().Be("info@ss-sport.co");
        settings.DigestAt.Should().Be(new TimeOnly(9, 0));
        settings.QuietHoursStart.Should().Be(new TimeOnly(23, 0));
        settings.QuietHoursEnd.Should().Be(new TimeOnly(8, 0));
        settings.DefaultLinkExpiryDays.Should().Be(30);
        settings.UpdatedAt.Should().Be(_now);
        settings.UpdatedByUserId.Should().BeNull();
    }

    /// <summary>An update returns the names of the fields that changed and raises one event with them.</summary>
    [Fact]
    public void Update_reports_what_changed()
    {
        var settings = Defaults();
        var admin = Guid.NewGuid();

        var changed = settings.Update(EmailAddress.Create("info@ss-sport.co"), new TimeOnly(10, 30), new TimeOnly(23, 0), new TimeOnly(7, 0), 90, admin, _now.AddHours(1));

        changed.Should().Equal("digestAt", "quietHoursEnd", "defaultLinkExpiryDays");
        settings.DigestAt.Should().Be(new TimeOnly(10, 30));
        settings.QuietHoursEnd.Should().Be(new TimeOnly(7, 0));
        settings.DefaultLinkExpiryDays.Should().Be(90);
        settings.UpdatedAt.Should().Be(_now.AddHours(1));
        settings.UpdatedByUserId.Should().Be(admin);
        settings.DomainEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(new AppSettingsChanged(changed));
    }

    /// <summary>Saving the same values changes nothing and raises nothing.</summary>
    [Fact]
    public void An_unchanged_update_is_a_no_op()
    {
        var settings = Defaults();

        var changed = settings.Update(EmailAddress.Create("info@ss-sport.co"), new TimeOnly(9, 0), new TimeOnly(23, 0), new TimeOnly(8, 0), 30, Guid.NewGuid(), _now.AddHours(1));

        changed.Should().BeEmpty();
        settings.UpdatedAt.Should().Be(_now);
        settings.DomainEvents.Should().BeEmpty();
    }

    /// <summary>Every link expires (decisions §1b, 2026-11-13): the default is 7, 30 or 90 days, nothing else.</summary>
    /// <param name="days">A refused default.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(365)]
    public void The_default_expiry_is_7_30_or_90_days(int days)
    {
        var settings = Defaults();

        var update = () => settings.Update(EmailAddress.Create("info@ss-sport.co"), new TimeOnly(9, 0), new TimeOnly(23, 0), new TimeOnly(8, 0), days, null, _now);

        update.Should().Throw<DomainException>().Which.Code.Should().Be("settings.invalid_link_expiry");
        AppSettings.LinkExpiryChoices.Should().Equal(7, 30, 90);
    }

    /// <summary>Times are whole minutes; seconds would make the digest schedule and the quiet-hours check disagree.</summary>
    [Fact]
    public void Times_are_whole_minutes()
    {
        var settings = Defaults();

        var update = () => settings.Update(EmailAddress.Create("info@ss-sport.co"), new TimeOnly(9, 0, 30), new TimeOnly(23, 0), new TimeOnly(8, 0), 30, null, _now);

        update.Should().Throw<DomainException>().Which.Code.Should().Be("settings.invalid_time");
    }

    /// <summary>The settings as configuration gives them on a fresh database.</summary>
    /// <returns>The settings, with no events.</returns>
    private static AppSettings Defaults()
    {
        var settings = AppSettings.CreateDefault(EmailAddress.Create("info@ss-sport.co"), new TimeOnly(9, 0), new TimeOnly(23, 0), new TimeOnly(8, 0), 30, _now);
        settings.DomainEvents.Should().BeEmpty();
        return settings;
    }
}
