using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Tracking;

/// <summary>What the player reports besides heartbeats.</summary>
public enum PlaybackEventType
{
    /// <summary>Playing started or resumed.</summary>
    Play,

    /// <summary>Playing was paused.</summary>
    Pause,

    /// <summary>The viewer jumped to another position.</summary>
    Seek,

    /// <summary>The video played to its end.</summary>
    Ended,
}

/// <summary>
/// One viewer watching one video from one page (L-5): it starts on the first play, takes a heartbeat with the range
/// played every 10 seconds and counts a new play when the viewer plays again after the end. Contiguous heartbeats join
/// into one range, so a whole match stays a handful of values.
/// </summary>
public sealed class WatchSession : AggregateRoot<Guid>
{
    /// <summary>The longest range one heartbeat may report (proposed): a 10 s beat at 2x speed, with slack.</summary>
    public const double MaxHeartbeatSeconds = 30;

    /// <summary>The most ranges a watch keeps.</summary>
    public const int MaxRanges = 2000;

    /// <summary>The ranges, in the order they were played.</summary>
    private readonly List<WatchRange> _ranges = [];

    /// <summary>Initializes a watch; use <see cref="Start"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="viewerSessionId">The viewer's session on the link.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="assetId">The video.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="durationSeconds">The video's length.</param>
    /// <param name="startedAt">When it started (UTC).</param>
    private WatchSession(Guid id, Guid viewerSessionId, Guid shareLinkId, Guid playerId, Guid assetId, EmailAddress email, double durationSeconds, DateTimeOffset startedAt)
        : base(id)
    {
        ViewerSessionId = viewerSessionId;
        ShareLinkId = shareLinkId;
        PlayerId = playerId;
        AssetId = assetId;
        Email = email;
        DurationSeconds = durationSeconds;
        StartedAt = startedAt;
        LastSeenAt = startedAt;
        Plays = 1;
    }

    /// <summary>Gets the viewer session that started the watch.</summary>
    public Guid ViewerSessionId { get; }

    /// <summary>Gets the link.</summary>
    public Guid ShareLinkId { get; }

    /// <summary>Gets the player.</summary>
    public Guid PlayerId { get; }

    /// <summary>Gets the video.</summary>
    public Guid AssetId { get; }

    /// <summary>Gets the viewer's email.</summary>
    public EmailAddress Email { get; }

    /// <summary>Gets the video's length in seconds.</summary>
    public double DurationSeconds { get; }

    /// <summary>Gets how many times the video was played.</summary>
    public int Plays { get; private set; }

    /// <summary>Gets the last event the player reported, or <see langword="null"/>.</summary>
    public PlaybackEventType? LastEventType { get; private set; }

    /// <summary>Gets the raw played ranges.</summary>
    public IReadOnlyList<WatchRange> Ranges => _ranges;

    /// <summary>Gets when the watch started (UTC).</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets when the watch was last heard from (UTC).</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Gets the unique seconds watched.</summary>
    public double UniqueSeconds => WatchMetrics.UniqueSeconds(_ranges);

    /// <summary>Gets every second played.</summary>
    public double WatchSeconds => WatchMetrics.WatchSeconds(_ranges);

    /// <summary>Gets the share of the video watched.</summary>
    public double Completion => WatchMetrics.Completion(_ranges, DurationSeconds);

    /// <summary>Gets a value indicating whether the video was watched to the end.</summary>
    public bool IsComplete => WatchMetrics.IsComplete(_ranges, DurationSeconds);

    /// <summary>Starts a watch on the first play.</summary>
    /// <param name="viewerSessionId">The viewer's session.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="assetId">The video.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="durationSeconds">The video's length: more than 0.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The watch, with one play.</returns>
    public static WatchSession Start(Guid viewerSessionId, Guid shareLinkId, Guid playerId, Guid assetId, EmailAddress email, double durationSeconds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(durationSeconds, 0);
        return new WatchSession(Guid.CreateVersion7(now), viewerSessionId, shareLinkId, playerId, assetId, email, durationSeconds, now);
    }

    /// <summary>Takes a heartbeat: the range played since the last one.</summary>
    /// <param name="from">The start in seconds.</param>
    /// <param name="to">The end in seconds.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><see langword="false"/> when the range cannot be real; nothing changes then.</returns>
    public bool Heartbeat(double from, double to, DateTimeOffset now)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to) || from < 0 || to <= from || to - from > MaxHeartbeatSeconds || to > DurationSeconds + 1)
        {
            return false;
        }

        to = Math.Min(to, DurationSeconds);
        if (to <= from)
        {
            return false;
        }

        if (_ranges.Count > 0 && Math.Abs(from - _ranges[^1].To) <= WatchMetrics.MergeGap)
        {
            _ranges[^1] = new WatchRange(_ranges[^1].From, Math.Max(_ranges[^1].To, to));
        }
        else if (_ranges.Count < MaxRanges)
        {
            _ranges.Add(new WatchRange(from, to));
        }
        else
        {
            return false;
        }

        LastSeenAt = now;
        return true;
    }

    /// <summary>Records a player event; playing again after the end counts a new play.</summary>
    /// <param name="type">The event.</param>
    /// <param name="positionSeconds">Where in the video it happened.</param>
    /// <param name="now">The current time (UTC).</param>
    public void RecordEvent(PlaybackEventType type, double positionSeconds, DateTimeOffset now)
    {
        if (type == PlaybackEventType.Play && LastEventType == PlaybackEventType.Ended)
        {
            Plays++;
        }

        LastEventType = type;
        LastSeenAt = now;
    }

    /// <summary>Counts the ranges that touch each bucket of the video.</summary>
    /// <param name="bucketSeconds">The bucket width.</param>
    /// <returns>One count per bucket.</returns>
    public IReadOnlyList<int> Hotspots(double bucketSeconds = WatchMetrics.HotspotBucketSeconds) =>
        WatchMetrics.Hotspots(_ranges, DurationSeconds, bucketSeconds);
}

/// <summary>One player event of a watch (L-5): play, pause, seek or ended.</summary>
public sealed class TrackingEvent : AggregateRoot<Guid>
{
    /// <summary>Initializes an event; use <see cref="Record"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="watchSessionId">The watch.</param>
    /// <param name="type">The event.</param>
    /// <param name="positionSeconds">Where in the video it happened.</param>
    /// <param name="occurredAt">When it happened (UTC).</param>
    private TrackingEvent(Guid id, Guid watchSessionId, PlaybackEventType type, double positionSeconds, DateTimeOffset occurredAt)
        : base(id)
    {
        WatchSessionId = watchSessionId;
        Type = type;
        PositionSeconds = positionSeconds;
        OccurredAt = occurredAt;
    }

    /// <summary>Gets the watch.</summary>
    public Guid WatchSessionId { get; }

    /// <summary>Gets the event.</summary>
    public PlaybackEventType Type { get; }

    /// <summary>Gets where in the video it happened.</summary>
    public double PositionSeconds { get; }

    /// <summary>Gets when it happened (UTC).</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Records an event.</summary>
    /// <param name="watchSessionId">The watch.</param>
    /// <param name="type">The event.</param>
    /// <param name="positionSeconds">Where in the video it happened.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The event.</returns>
    public static TrackingEvent Record(Guid watchSessionId, PlaybackEventType type, double positionSeconds, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), watchSessionId, type, positionSeconds, now);
}
