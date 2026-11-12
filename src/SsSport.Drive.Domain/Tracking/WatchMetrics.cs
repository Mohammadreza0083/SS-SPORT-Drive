namespace SsSport.Drive.Domain.Tracking;

/// <summary>A stretch of a video that was played, in seconds from its start.</summary>
/// <param name="From">The start.</param>
/// <param name="To">The end.</param>
public readonly record struct WatchRange(double From, double To)
{
    /// <summary>Gets the length in seconds.</summary>
    public double Length => To - From;

    /// <summary>Makes a checked range.</summary>
    /// <param name="from">The start: finite and at least 0.</param>
    /// <param name="to">The end: finite and after the start.</param>
    /// <returns>The range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not possible.</exception>
    public static WatchRange Create(double from, double to)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || from < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(from), "A watched range starts at 0 or later and is finite.");
        }

        if (to <= from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "A watched range ends after it starts.");
        }

        return new WatchRange(from, to);
    }
}

/// <summary>
/// The watch metrics (L-6): unique seconds watched (ranges merged), watch time (every second played, rewatches
/// included), completion and the hotspots of the replay heatmap.
/// </summary>
public static class WatchMetrics
{
    /// <summary>Ranges closer than this join: heartbeat jitter (proposed).</summary>
    public const double MergeGap = 0.5;

    /// <summary>A video counts as complete from this share of its length (proposed).</summary>
    public const double CompletionThreshold = 0.9;

    /// <summary>The width of a hotspot bucket in seconds (proposed).</summary>
    public const double HotspotBucketSeconds = 5;

    /// <summary>Merges overlapping ranges, and ranges at most <see cref="MergeGap"/> apart.</summary>
    /// <param name="ranges">The ranges, in any order.</param>
    /// <returns>The merged ranges, in order.</returns>
    public static IReadOnlyList<WatchRange> Merge(IEnumerable<WatchRange> ranges) => MergeWithGap(ranges, MergeGap);

    /// <summary>Counts every second of the video watched once, however often it was watched.</summary>
    /// <param name="ranges">The ranges.</param>
    /// <returns>The unique seconds.</returns>
    public static double UniqueSeconds(IEnumerable<WatchRange> ranges) => MergeWithGap(ranges, 0).Sum(range => range.Length);

    /// <summary>Counts every second played, rewatches included.</summary>
    /// <param name="ranges">The ranges.</param>
    /// <returns>The watch time in seconds.</returns>
    public static double WatchSeconds(IEnumerable<WatchRange> ranges) => ranges.Sum(range => range.Length);

    /// <summary>Gives the share of the video watched, 0 to 1; seconds past the end do not count.</summary>
    /// <param name="ranges">The ranges.</param>
    /// <param name="durationSeconds">The video's length.</param>
    /// <returns>The completion, or 0 for a video with no length.</returns>
    public static double Completion(IEnumerable<WatchRange> ranges, double durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            return 0;
        }

        var unique = MergeWithGap(ranges, 0)
            .Sum(range => Math.Max(0, Math.Min(range.To, durationSeconds) - Math.Min(range.From, durationSeconds)));
        return Math.Min(1, unique / durationSeconds);
    }

    /// <summary>Tells whether the video counts as watched to the end.</summary>
    /// <param name="ranges">The ranges.</param>
    /// <param name="durationSeconds">The video's length.</param>
    /// <returns><see langword="true"/> from <see cref="CompletionThreshold"/>.</returns>
    public static bool IsComplete(IEnumerable<WatchRange> ranges, double durationSeconds) =>
        Completion(ranges, durationSeconds) >= CompletionThreshold;

    /// <summary>Counts, per bucket of the video, the ranges that touch it; a rewatched part stands out.</summary>
    /// <param name="ranges">The raw ranges.</param>
    /// <param name="durationSeconds">The video's length.</param>
    /// <param name="bucketSeconds">The bucket width.</param>
    /// <returns>One count per bucket, none for a video with no length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bucket width is not positive.</exception>
    public static IReadOnlyList<int> Hotspots(IEnumerable<WatchRange> ranges, double durationSeconds, double bucketSeconds = HotspotBucketSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bucketSeconds, 0);
        if (durationSeconds <= 0)
        {
            return [];
        }

        var list = ranges.ToList();
        var buckets = (int)Math.Ceiling(durationSeconds / bucketSeconds);
        var counts = new int[buckets];
        for (var index = 0; index < buckets; index++)
        {
            var (start, end) = (index * bucketSeconds, (index + 1) * bucketSeconds);
            counts[index] = list.Count(range => Math.Min(range.To, end) - Math.Max(range.From, start) > 0);
        }

        return counts;
    }

    /// <summary>Merges ranges that overlap or are at most a gap apart.</summary>
    /// <param name="ranges">The ranges.</param>
    /// <param name="gap">The largest gap that still joins.</param>
    /// <returns>The merged ranges, in order.</returns>
    private static List<WatchRange> MergeWithGap(IEnumerable<WatchRange> ranges, double gap)
    {
        var merged = new List<WatchRange>();
        foreach (var range in ranges.OrderBy(item => item.From))
        {
            if (merged.Count > 0 && range.From <= merged[^1].To + gap)
            {
                merged[^1] = new WatchRange(merged[^1].From, Math.Max(merged[^1].To, range.To));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }
}
