using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Domain.UnitTests.Tracking;

/// <summary>
/// The watch metrics (L-6), one test group per metric: unique seconds watched (watched ranges merged), watch time
/// (every second played, rewatches included), completion (unique share of the length; complete at 90 %), and hotspots
/// (how many watched ranges touch each 5-second bucket, for the replay heatmap).
/// </summary>
public sealed class WatchMetricsTests
{
    /// <summary>Overlapping ranges merge into one.</summary>
    [Fact]
    public void Overlapping_ranges_merge()
    {
        var merged = WatchMetrics.Merge([new WatchRange(0, 10), new WatchRange(5, 20)]);

        merged.Should().Equal(new WatchRange(0, 20));
    }

    /// <summary>Ranges merge in any order, and touching ranges or gaps up to half a second join (heartbeat jitter).</summary>
    [Fact]
    public void Touching_ranges_and_small_gaps_merge()
    {
        var merged = WatchMetrics.Merge([new WatchRange(20, 30), new WatchRange(0, 10), new WatchRange(10, 15), new WatchRange(15.4, 18)]);

        merged.Should().Equal(new WatchRange(0, 18), new WatchRange(20, 30));
        WatchMetrics.MergeGap.Should().Be(0.5);
    }

    /// <summary>A range inside another disappears into it.</summary>
    [Fact]
    public void A_contained_range_adds_nothing()
    {
        WatchMetrics.Merge([new WatchRange(0, 60), new WatchRange(10, 20)]).Should().Equal(new WatchRange(0, 60));
    }

    /// <summary>No ranges merge to none.</summary>
    [Fact]
    public void Nothing_merges_to_nothing()
    {
        WatchMetrics.Merge([]).Should().BeEmpty();
        WatchMetrics.UniqueSeconds([]).Should().Be(0);
        WatchMetrics.WatchSeconds([]).Should().Be(0);
    }

    /// <summary>Unique seconds count each second of the video once, however often it was watched.</summary>
    [Fact]
    public void Unique_seconds_count_once()
    {
        WatchMetrics.UniqueSeconds([new WatchRange(0, 10), new WatchRange(5, 20), new WatchRange(0, 10), new WatchRange(30, 40)]).Should().Be(30);
    }

    /// <summary>Watch time counts every second played, rewatches included.</summary>
    [Fact]
    public void Watch_time_counts_rewatches()
    {
        WatchMetrics.WatchSeconds([new WatchRange(0, 10), new WatchRange(5, 20), new WatchRange(0, 10)]).Should().Be(35);
    }

    /// <summary>Completion is the unique share of the length, capped at 1.</summary>
    [Fact]
    public void Completion_is_the_unique_share()
    {
        WatchMetrics.Completion([new WatchRange(0, 25), new WatchRange(50, 75)], 100).Should().Be(0.5);
        WatchMetrics.Completion([new WatchRange(0, 100), new WatchRange(0, 100)], 100).Should().Be(1);
    }

    /// <summary>Seconds past the end of the video do not count toward completion.</summary>
    [Fact]
    public void Completion_ignores_seconds_past_the_end()
    {
        WatchMetrics.Completion([new WatchRange(50, 130)], 100).Should().Be(0.5);
    }

    /// <summary>A video with no length has no completion.</summary>
    /// <param name="duration">The length.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void No_length_no_completion(double duration)
    {
        WatchMetrics.Completion([new WatchRange(0, 10)], duration).Should().Be(0);
    }

    /// <summary>A video counts as complete from 90 % unique watched.</summary>
    [Fact]
    public void Complete_from_ninety_percent()
    {
        WatchMetrics.CompletionThreshold.Should().Be(0.9);
        WatchMetrics.IsComplete([new WatchRange(0, 90)], 100).Should().BeTrue();
        WatchMetrics.IsComplete([new WatchRange(0, 89.9)], 100).Should().BeFalse();
    }

    /// <summary>Hotspots count, per 5-second bucket, the watched ranges that touch it; a rewatched part stands out.</summary>
    [Fact]
    public void Hotspots_count_ranges_per_bucket()
    {
        var hotspots = WatchMetrics.Hotspots([new WatchRange(0, 20), new WatchRange(5, 12), new WatchRange(5, 10)], 22);

        WatchMetrics.HotspotBucketSeconds.Should().Be(5);
        hotspots.Should().Equal(1, 3, 2, 1, 0);
    }

    /// <summary>A range that only touches a bucket's edge does not count in it.</summary>
    [Fact]
    public void Touching_an_edge_is_not_watching_the_bucket()
    {
        WatchMetrics.Hotspots([new WatchRange(5, 10)], 15).Should().Equal(0, 1, 0);
    }

    /// <summary>Another bucket size can be asked for; a video with no length has no buckets.</summary>
    [Fact]
    public void Bucket_size_and_empty_videos()
    {
        WatchMetrics.Hotspots([new WatchRange(0, 30)], 30, bucketSeconds: 10).Should().Equal(1, 1, 1);
        WatchMetrics.Hotspots([new WatchRange(0, 30)], 0).Should().BeEmpty();
    }

    /// <summary>A bucket size of zero or less is a bug.</summary>
    [Fact]
    public void Bucket_size_must_be_positive()
    {
        ((Action)(() => WatchMetrics.Hotspots([], 30, bucketSeconds: 0))).Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>A range must start at zero or later and end after it starts.</summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(double.NaN, 5)]
    [InlineData(0, double.PositiveInfinity)]
    public void A_bad_range_is_refused(double from, double to)
    {
        ((Action)(() => WatchRange.Create(from, to))).Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>A range knows its length.</summary>
    [Fact]
    public void A_range_has_a_length()
    {
        WatchRange.Create(2.5, 10).Length.Should().Be(7.5);
    }
}
