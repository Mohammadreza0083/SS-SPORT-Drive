using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Viewers;

namespace SsSport.Drive.Domain.UnitTests.Viewers;

/// <summary>
/// A viewer is one email address that can watch (A-7, A-11): a recipient of a share link or a verified forward. The
/// console addresses viewers by this opaque id, so an email never travels in a URL (Privacy rules). One row per address.
/// </summary>
public sealed class ViewerTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Registering keeps the normalised address and the first time it was seen, under a time-ordered id.</summary>
    [Fact]
    public void Register_keeps_the_address_and_first_seen_time()
    {
        var viewer = Viewer.Register(EmailAddress.Create(" Marco@Lube.it "), _now);

        viewer.Id.Should().NotBeEmpty();
        viewer.Id.Version.Should().Be(7);
        viewer.Email.Value.Should().Be("marco@lube.it");
        viewer.FirstSeenAt.Should().Be(_now);
    }

    /// <summary>Two registrations are two different ids; uniqueness by address is the database's job.</summary>
    [Fact]
    public void Each_registration_gets_its_own_id()
    {
        var first = Viewer.Register(EmailAddress.Create("marco@lube.it"), _now);
        var second = Viewer.Register(EmailAddress.Create("jan@aurora.nl"), _now);

        first.Id.Should().NotBe(second.Id);
    }
}
