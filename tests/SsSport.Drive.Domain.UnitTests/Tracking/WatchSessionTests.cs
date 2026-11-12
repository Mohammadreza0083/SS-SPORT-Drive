using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Domain.UnitTests.Tracking;

/// <summary>
/// One viewer watching one video from one page (L-5): it starts on the first play, takes a heartbeat with the range
/// played every 10 seconds, and counts a new play when the viewer plays again after the end. Heartbeats that cannot be
/// real (backwards, longer than 30 s, past the end) are refused. Contiguous heartbeats join into one range, so a whole
/// match stays a handful of rows.
/// </summary>
public sealed class WatchSessionTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The ids of the watch's context.</summary>
    private static readonly (Guid Session, Guid Link, Guid Player, Guid Asset) _ids =
        (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    /// <summary>A started watch counts one play and nothing watched yet.</summary>
    [Fact]
    public void A_watch_starts_with_one_play()
    {
        var watch = Start();

        watch.ViewerSessionId.Should().Be(_ids.Session);
        watch.ShareLinkId.Should().Be(_ids.Link);
        watch.PlayerId.Should().Be(_ids.Player);
        watch.AssetId.Should().Be(_ids.Asset);
        watch.Email.Value.Should().Be("marco@lube.it");
        watch.DurationSeconds.Should().Be(100);
        watch.Plays.Should().Be(1);
        watch.Ranges.Should().BeEmpty();
        watch.StartedAt.Should().Be(_now);
        watch.LastSeenAt.Should().Be(_now);
        watch.UniqueSeconds.Should().Be(0);
        watch.WatchSeconds.Should().Be(0);
        watch.Completion.Should().Be(0);
        watch.IsComplete.Should().BeFalse();
    }

    /// <summary>Contiguous heartbeats join into one range and move the last-seen time.</summary>
    [Fact]
    public void Contiguous_heartbeats_join()
    {
        var watch = Start();

        watch.Heartbeat(0, 10, _now.AddSeconds(10)).Should().BeTrue();
        watch.Heartbeat(10, 20, _now.AddSeconds(20)).Should().BeTrue();
        watch.Heartbeat(20.3, 30, _now.AddSeconds(30)).Should().BeTrue();

        watch.Ranges.Should().Equal(new WatchRange(0, 30));
        watch.LastSeenAt.Should().Be(_now.AddSeconds(30));
        watch.UniqueSeconds.Should().Be(30);
        watch.WatchSeconds.Should().Be(30);
        watch.Completion.Should().Be(0.3);
    }

    /// <summary>A seek back starts a new range: rewatches add watch time but not unique seconds.</summary>
    [Fact]
    public void A_seek_back_is_a_rewatch()
    {
        var watch = Start();
        watch.Heartbeat(0, 10, _now);
        watch.Heartbeat(10, 20, _now);

        watch.Heartbeat(5, 15, _now).Should().BeTrue();

        watch.Ranges.Should().Equal(new WatchRange(0, 20), new WatchRange(5, 15));
        watch.UniqueSeconds.Should().Be(20);
        watch.WatchSeconds.Should().Be(30);
    }

    /// <summary>Heartbeats that cannot be real are refused and change nothing.</summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    [Theory]
    [InlineData(-1, 5)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 30.5)]
    [InlineData(95, 102)]
    [InlineData(double.NaN, 5)]
    public void Impossible_heartbeats_are_refused(double from, double to)
    {
        var watch = Start();

        watch.Heartbeat(from, to, _now.AddSeconds(10)).Should().BeFalse();

        watch.Ranges.Should().BeEmpty();
        watch.LastSeenAt.Should().Be(_now);
        WatchSession.MaxHeartbeatSeconds.Should().Be(30);
    }

    /// <summary>A heartbeat ending up to one second past the end is clipped to the end (rounding in the player).</summary>
    [Fact]
    public void A_heartbeat_just_past_the_end_is_clipped()
    {
        var watch = Start();

        watch.Heartbeat(95, 100.8, _now).Should().BeTrue();

        watch.Ranges.Should().Equal(new WatchRange(95, 100));
    }

    /// <summary>Watching 90 % of the video makes it complete.</summary>
    [Fact]
    public void Ninety_percent_is_complete()
    {
        var watch = Start();
        for (var second = 0; second < 90; second += 10)
        {
            watch.Heartbeat(second, second + 10, _now);
        }

        watch.Completion.Should().Be(0.9);
        watch.IsComplete.Should().BeTrue();
    }

    /// <summary>Playing again after the end is a new play; pausing and resuming is not.</summary>
    [Fact]
    public void A_replay_after_the_end_is_a_new_play()
    {
        var watch = Start();

        watch.RecordEvent(PlaybackEventType.Pause, 30, _now);
        watch.RecordEvent(PlaybackEventType.Play, 30, _now);
        watch.Plays.Should().Be(1);

        watch.RecordEvent(PlaybackEventType.Ended, 100, _now);
        watch.RecordEvent(PlaybackEventType.Play, 0, _now.AddSeconds(5));
        watch.Plays.Should().Be(2);
        watch.LastSeenAt.Should().Be(_now.AddSeconds(5));

        watch.RecordEvent(PlaybackEventType.Seek, 50, _now);
        watch.RecordEvent(PlaybackEventType.Play, 50, _now);
        watch.Plays.Should().Be(2);
    }

    /// <summary>Hotspots of one watch come from its raw ranges.</summary>
    [Fact]
    public void A_watch_has_hotspots()
    {
        var watch = Start(duration: 15);
        watch.Heartbeat(0, 10, _now);
        watch.Heartbeat(5, 10, _now);

        watch.Hotspots().Should().Equal(1, 2, 0);
    }

    /// <summary>A watch keeps at most 2000 ranges; heartbeats past that are refused.</summary>
    [Fact]
    public void Ranges_are_capped()
    {
        var watch = Start(duration: 10_000);
        for (var i = 0; i < WatchSession.MaxRanges; i++)
        {
            watch.Heartbeat(i * 2, (i * 2) + 1, _now).Should().BeTrue();
        }

        watch.Heartbeat(0, 1, _now).Should().BeFalse();

        WatchSession.MaxRanges.Should().Be(2000);
        watch.Ranges.Should().HaveCount(2000);
    }

    /// <summary>A watch needs a positive length.</summary>
    [Fact]
    public void A_watch_needs_a_length()
    {
        ((Action)(() => Start(duration: 0))).Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Starts a watch of a 100-second video.</summary>
    /// <param name="duration">The video length.</param>
    /// <returns>The watch.</returns>
    private static WatchSession Start(double duration = 100) =>
        WatchSession.Start(_ids.Session, _ids.Link, _ids.Player, _ids.Asset, EmailAddress.Create("marco@lube.it"), duration, _now);
}
