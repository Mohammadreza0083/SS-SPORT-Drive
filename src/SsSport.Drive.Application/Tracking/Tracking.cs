using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Application.Tracking;

/// <summary>The rules of watch tracking (L-5).</summary>
public static class TrackingRules
{
    /// <summary>How often the player sends a heartbeat, in seconds (proposed).</summary>
    public const int HeartbeatSeconds = 10;

    /// <summary>The error code of a video the viewer cannot watch on this link.</summary>
    public const string UnknownVideoCode = "tracking.unknown_video";

    /// <summary>The error code of a watch that is not this viewer's.</summary>
    public const string UnknownWatchCode = "tracking.unknown_watch";

    /// <summary>The error code of a heartbeat that cannot be real.</summary>
    public const string InvalidRangeCode = "tracking.invalid_range";

    /// <summary>Reads an event type by name, case-insensitively; numbers are not names.</summary>
    /// <param name="value">The value sent.</param>
    /// <param name="type">The event type.</param>
    /// <returns><see langword="true"/> when the value names an event type.</returns>
    public static bool TryParseEvent(string? value, out PlaybackEventType type)
    {
        type = default;
        return !string.IsNullOrEmpty(value)
            && !value.Any(char.IsDigit)
            && Enum.TryParse(value, ignoreCase: true, out type)
            && Enum.IsDefined(type);
    }

    /// <summary>Finds the viewer's own watch on a link.</summary>
    /// <param name="watches">The watches.</param>
    /// <param name="watchId">The watch id from the URL.</param>
    /// <param name="session">The viewer's live session on the link.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The watch.</returns>
    /// <exception cref="NotFoundException"><c>tracking.unknown_watch</c> when the watch does not exist or belongs to another session.</exception>
    public static async Task<WatchSession> FindWatchAsync(
        this IRepository<WatchSession, Guid> watches,
        Guid watchId,
        ViewerSession session,
        CancellationToken cancellationToken)
    {
        var watch = await watches.GetByIdAsync(watchId, cancellationToken);
        return watch is not null && watch.ViewerSessionId == session.Id
            ? watch
            : throw new NotFoundException("Watch", UnknownWatchCode);
    }
}

/// <summary>One ready, visible video of a player.</summary>
public sealed class WatchableVideoSpec : Specification<Asset>, ISingleResultSpecification<Asset>
{
    /// <summary>Filters by player and video.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="assetId">The video.</param>
    public WatchableVideoSpec(Guid playerId, Guid assetId) =>
        Query.Where(asset => asset.Id == assetId
            && asset.PlayerId == playerId
            && asset.File.Kind == AssetKind.Video
            && asset.Visibility == AssetVisibility.Visible
            && asset.State == ProcessingState.Ready);
}

/// <summary>A watch just started.</summary>
public sealed record WatchStartedDto
{
    /// <summary>Gets the watch id the player sends with every heartbeat and event.</summary>
    public required Guid WatchId { get; init; }

    /// <summary>Gets how often the player should send a heartbeat, in seconds.</summary>
    public required int HeartbeatSeconds { get; init; }
}

/// <summary>The body of a heartbeat: the range played since the last one, in seconds.</summary>
public sealed record HeartbeatFormDto
{
    /// <summary>Gets the start of the range.</summary>
    public double From { get; init; }

    /// <summary>Gets the end of the range.</summary>
    public double To { get; init; }
}

/// <summary>The body of a player event.</summary>
public sealed record PlaybackEventFormDto
{
    /// <summary>Gets the event: <c>Play</c>, <c>Pause</c>, <c>Seek</c> or <c>Ended</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Gets where in the video it happened, in seconds.</summary>
    public double Position { get; init; }
}

/// <summary>One viewer's viewing of one video, summed over their watches (L-6).</summary>
public sealed record ViewingRowDto
{
    /// <summary>Gets the viewer's full email (admins only, decisions §7a).</summary>
    public required string Email { get; init; }

    /// <summary>Gets the player.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>Gets the video.</summary>
    public required Guid AssetId { get; init; }

    /// <summary>Gets how many times the video was played.</summary>
    public required int Plays { get; init; }

    /// <summary>Gets every second played, rewatches included.</summary>
    public required double WatchSeconds { get; init; }

    /// <summary>Gets the unique seconds watched.</summary>
    public required double UniqueSeconds { get; init; }

    /// <summary>Gets the share of the video watched, 0 to 1.</summary>
    public required double Completion { get; init; }

    /// <summary>Gets a value indicating whether the video was watched to the end (from 90 %).</summary>
    public required bool IsComplete { get; init; }

    /// <summary>Gets when the viewer was last heard from.</summary>
    public required DateTimeOffset LastSeenAt { get; init; }
}

/// <summary>Reads a link's viewing, one row per viewer and video, paged in SQL.</summary>
public interface IViewingReader
{
    /// <summary>Reads one page, the most recently watched first.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="request">The page.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<ViewingRowDto>> PageAsync(Guid shareLinkId, PageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A viewer starts watching a video (<c>POST …/videos/{assetId}/watch</c>).</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The viewer session token, if any.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="AssetId">The video.</param>
public sealed record StartWatchCommand(string Code, string? SessionToken, Guid PlayerId, Guid AssetId) : ICommand<WatchStartedDto>;

/// <summary>Handles <see cref="StartWatchCommand"/>: the link opens, the session is live, and the video is a ready, visible video of a player on the link.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="assets">The assets.</param>
/// <param name="watches">The watches.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class StartWatchCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    IRepository<Asset, Guid> assets,
    IRepository<WatchSession, Guid> watches,
    TimeProvider timeProvider) : ICommandHandler<StartWatchCommand, WatchStartedDto>
{
    /// <summary>Starts the watch, with one play.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The watch id and the heartbeat interval.</returns>
    /// <exception cref="ShareLinkUnavailableException">The link does not open.</exception>
    /// <exception cref="ViewerVerificationRequiredException">No live session for this link.</exception>
    /// <exception cref="NotFoundException">The player is not on the link, or the video cannot be watched.</exception>
    public async Task<WatchStartedDto> Handle(StartWatchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        var session = await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);
        var video = await assets.FirstOrDefaultAsync(new WatchableVideoSpec(request.PlayerId, request.AssetId), cancellationToken);
        if (video is not { DurationSeconds: > 0 })
        {
            throw new NotFoundException("Video", TrackingRules.UnknownVideoCode);
        }

        var watch = WatchSession.Start(session.Id, link.Id, request.PlayerId, video.Id, session.Email, video.DurationSeconds.Value, now);
        watches.Add(watch);
        return new WatchStartedDto { WatchId = watch.Id, HeartbeatSeconds = TrackingRules.HeartbeatSeconds };
    }
}

/// <summary>The player sends the range played since the last heartbeat.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The viewer session token, if any.</param>
/// <param name="WatchId">The watch.</param>
/// <param name="From">The start of the range in seconds.</param>
/// <param name="To">The end of the range in seconds.</param>
public sealed record RecordHeartbeatCommand(string Code, string? SessionToken, Guid WatchId, double From, double To) : ICommand;

/// <summary>Handles <see cref="RecordHeartbeatCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="watches">The watches.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class RecordHeartbeatCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    IRepository<WatchSession, Guid> watches,
    TimeProvider timeProvider) : ICommandHandler<RecordHeartbeatCommand>
{
    /// <summary>Adds the range to the watch.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the heartbeat is recorded.</returns>
    /// <exception cref="DomainException"><c>tracking.invalid_range</c> when the range cannot be real.</exception>
    public async Task Handle(RecordHeartbeatCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        var session = await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        var watch = await watches.FindWatchAsync(request.WatchId, session, cancellationToken);
        if (!watch.Heartbeat(request.From, request.To, now))
        {
            throw new DomainException(TrackingRules.InvalidRangeCode, "That range cannot have been played.");
        }
    }
}

/// <summary>The player reports an event: play, pause, seek or ended.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The viewer session token, if any.</param>
/// <param name="WatchId">The watch.</param>
/// <param name="Type">The event name.</param>
/// <param name="Position">Where in the video it happened, in seconds.</param>
public sealed record RecordPlaybackEventCommand(string Code, string? SessionToken, Guid WatchId, string? Type, double Position) : ICommand;

/// <summary>The event is one of <c>Play</c>, <c>Pause</c>, <c>Seek</c>, <c>Ended</c>, and the position is a finite time from 0 (400).</summary>
public sealed class RecordPlaybackEventCommandValidator : AbstractValidator<RecordPlaybackEventCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordPlaybackEventCommandValidator()
    {
        RuleFor(command => command.Type)
            .Must(type => TrackingRules.TryParseEvent(type, out _))
            .WithMessage("Use Play, Pause, Seek or Ended.")
            .OverridePropertyName("type");
        RuleFor(command => command.Position)
            .Must(position => double.IsFinite(position) && position >= 0)
            .WithMessage("The position is a time in seconds from 0.")
            .OverridePropertyName("position");
    }
}

/// <summary>Handles <see cref="RecordPlaybackEventCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="watches">The watches.</param>
/// <param name="events">The event log.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class RecordPlaybackEventCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    IRepository<WatchSession, Guid> watches,
    IRepository<TrackingEvent, Guid> events,
    TimeProvider timeProvider) : ICommandHandler<RecordPlaybackEventCommand>
{
    /// <summary>Logs the event and counts a replay.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the event is recorded.</returns>
    public async Task Handle(RecordPlaybackEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        var session = await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        var watch = await watches.FindWatchAsync(request.WatchId, session, cancellationToken);
        TrackingRules.TryParseEvent(request.Type, out var type);
        watch.RecordEvent(type, request.Position, now);
        events.Add(TrackingEvent.Record(watch.Id, type, request.Position, now));
    }
}

/// <summary>Reads how a link was watched (<c>GET /admin/links/{id}/viewing</c>), for admins only.</summary>
/// <param name="ShareLinkId">The link.</param>
/// <param name="Paging">The page.</param>
public sealed record GetLinkViewingQuery(Guid ShareLinkId, PageRequest Paging) : IPagedQuery<ViewingRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Handles <see cref="GetLinkViewingQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="reader">Reads the viewing.</param>
public sealed class GetLinkViewingQueryHandler(IRepository<ShareLink, Guid> links, IViewingReader reader) : IQueryHandler<GetLinkViewingQuery, PagedResult<ViewingRowDto>>
{
    /// <summary>Reads one page of the viewing.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="NotFoundException">The link does not exist.</exception>
    public async Task<PagedResult<ViewingRowDto>> Handle(GetLinkViewingQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await links.GetByIdAsync(request.ShareLinkId, cancellationToken) ?? throw new NotFoundException("Share link");
        return await reader.PageAsync(request.ShareLinkId, request.Paging, cancellationToken);
    }
}
