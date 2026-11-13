using SsSport.Drive.Domain.Notifications;

namespace SsSport.Drive.Domain.UnitTests.Notifications;

/// <summary>
/// T-6b: the progress message remembers when the current build started (the base of the time-left estimate) and
/// whether the player's QR code was already sent under it, so it is sent only once.
/// </summary>
public sealed class ProgressMessageBuildTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A new message has no build and no QR code yet.</summary>
    [Fact]
    public void Starts_without_a_build_or_a_QR_code()
    {
        var message = NewMessage();

        message.BuildStartedAt.Should().BeNull();
        message.QrSentAt.Should().BeNull();
    }

    /// <summary>The first refresh that sees files in the pipeline sets the start; later ones keep it.</summary>
    [Fact]
    public void The_build_start_is_set_once()
    {
        var message = NewMessage();

        message.TrackBuild(building: true, _now).Should().BeTrue("the start changed");
        message.TrackBuild(building: true, _now.AddMinutes(5)).Should().BeFalse("the start is kept");

        message.BuildStartedAt.Should().Be(_now);
    }

    /// <summary>When nothing is building any more, the start is cleared; clearing twice changes nothing.</summary>
    [Fact]
    public void A_finished_build_clears_the_start()
    {
        var message = NewMessage();
        message.TrackBuild(building: true, _now);

        message.TrackBuild(building: false, _now.AddMinutes(10)).Should().BeTrue();
        message.TrackBuild(building: false, _now.AddMinutes(11)).Should().BeFalse();

        message.BuildStartedAt.Should().BeNull();
    }

    /// <summary>A later build (new files from the sync) measures its own time, not the first build's.</summary>
    [Fact]
    public void A_new_build_measures_from_its_own_start()
    {
        var message = NewMessage();
        message.TrackBuild(building: true, _now);
        message.TrackBuild(building: false, _now.AddMinutes(10));

        message.TrackBuild(building: true, _now.AddDays(1));

        message.BuildStartedAt.Should().Be(_now.AddDays(1));
    }

    /// <summary>The QR code is recorded once; a second call keeps the first time.</summary>
    [Fact]
    public void The_QR_code_is_recorded_once()
    {
        var message = NewMessage();

        message.QrSent(_now);
        message.QrSent(_now.AddMinutes(3));

        message.QrSentAt.Should().Be(_now);
    }

    /// <summary>Builds a progress message.</summary>
    /// <returns>The message.</returns>
    private static ProgressMessage NewMessage() => ProgressMessage.Start(Guid.CreateVersion7(_now), 901, 1001, "⏳ Arash", _now);
}
