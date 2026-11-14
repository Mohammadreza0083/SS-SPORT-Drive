using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Tracking;
using SsSport.Drive.Domain.Viewers;

namespace SsSport.Drive.Application.Admin;

/// <summary>How a viewer signs in, as the viewers list shows it.</summary>
public static class ViewerAccountKind
{
    /// <summary>The viewer has a viewer account (signed in with an email link, P-5).</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>The viewer opened a link and verified a code, but has no account.</summary>
    public const string LinkOnly = "LinkOnly";

    /// <summary>The viewer has not opened anything yet.</summary>
    public const string NotYet = "NotYet";

    /// <summary>Gets the activity filters of the viewers list.</summary>
    public static IReadOnlyList<string> ActivityFilters { get; } = ["all", "active", "never"];

    /// <summary>The widest a heatmap is: the buckets of a long video are wider so there are never more of them.</summary>
    public const int MaxHeatmapBuckets = 200;

    /// <summary>The narrowest a heatmap bucket is, in seconds.</summary>
    public const double MinBucketSeconds = 5;

    /// <summary>Gets how wide a heatmap bucket is for a video.</summary>
    /// <param name="durationSeconds">The video's length.</param>
    /// <returns>The bucket width in seconds.</returns>
    public static double BucketSeconds(double durationSeconds) => Math.Max(MinBucketSeconds, Math.Ceiling(durationSeconds / MaxHeatmapBuckets));
}

/// <summary>One viewer on the console's viewers list (A-11).</summary>
public sealed record AdminViewerRowDto
{
    /// <summary>Gets the viewer id; the console's addresses use it, never the email.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the viewer's email; admins see viewer identities.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets the organisation of the newest recipient with this email, or <see langword="null"/>.</summary>
    public string? Organisation { get; init; }

    /// <summary>Gets up to three players the viewer can open.</summary>
    public IReadOnlyList<string> Players { get; init; } = [];

    /// <summary>Gets how many players the viewer can open.</summary>
    public int PlayerCount { get; init; }

    /// <summary>Gets how many times the viewer played videos.</summary>
    public int Plays { get; init; }

    /// <summary>Gets every second the viewer played.</summary>
    public double WatchSeconds { get; init; }

    /// <summary>Gets when the viewer was last heard from, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>Gets when the viewer's latest open link expires, or <see langword="null"/>.</summary>
    public DateTimeOffset? AccessUntil { get; init; }

    /// <summary>Gets <see cref="ViewerAccountKind.SignedIn"/>, <see cref="ViewerAccountKind.LinkOnly"/> or <see cref="ViewerAccountKind.NotYet"/>.</summary>
    public string Account { get; init; } = string.Empty;
}

/// <summary>A viewer's profile (A-7).</summary>
public sealed record AdminViewerDto
{
    /// <summary>Gets the viewer id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the viewer's email.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets the organisation of the newest recipient with this email, or <see langword="null"/>.</summary>
    public string? Organisation { get; init; }

    /// <summary>Gets when the address was first seen.</summary>
    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>Gets when the viewer was last heard from, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>Gets how the viewer signs in.</summary>
    public string Account { get; init; } = string.Empty;

    /// <summary>Gets how many times the viewer played videos.</summary>
    public int Plays { get; init; }

    /// <summary>Gets every second the viewer played.</summary>
    public double WatchSeconds { get; init; }

    /// <summary>Gets how many players the viewer can open.</summary>
    public int PlayersAccessible { get; init; }

    /// <summary>Gets the average share of a video watched (0 to 1), or <see langword="null"/>.</summary>
    public double? AverageCompletion { get; init; }
}

/// <summary>One player a viewer can open through one link.</summary>
public sealed record ViewerAccessRowDto
{
    /// <summary>Gets the player id.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the player's name.</summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>Gets the link id.</summary>
    public Guid LinkId { get; init; }

    /// <summary>Gets the link's code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the link's access now: <c>Active</c>, <c>Paused</c>, <c>Revoked</c> or <c>Expired</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets when the link was made.</summary>
    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>Gets when the link expires, or <see langword="null"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets how many times the viewer played this player's videos through this link.</summary>
    public int Plays { get; init; }

    /// <summary>Gets the seconds the viewer played through this link.</summary>
    public double WatchSeconds { get; init; }
}

/// <summary>How one viewer watched one video, summed over every play and link.</summary>
public sealed record ViewerVideoRowDto
{
    /// <summary>Gets the player id.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the player's name.</summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>Gets the video id.</summary>
    public Guid AssetId { get; init; }

    /// <summary>Gets the video's title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets how many times it was played.</summary>
    public int Plays { get; init; }

    /// <summary>Gets every second played.</summary>
    public double WatchSeconds { get; init; }

    /// <summary>Gets the seconds watched at least once (the merged ranges).</summary>
    public double UniqueSeconds { get; init; }

    /// <summary>Gets the share of the video watched (0 to 1).</summary>
    public double Completion { get; init; }

    /// <summary>Gets when it was last watched.</summary>
    public DateTimeOffset LastWatchedAt { get; init; }
}

/// <summary>The replay heatmap of one video for one viewer (A-7).</summary>
public sealed record ViewerHeatmapDto
{
    /// <summary>Gets the video id.</summary>
    public Guid AssetId { get; init; }

    /// <summary>Gets the video's title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the video's length in seconds.</summary>
    public double DurationSeconds { get; init; }

    /// <summary>Gets how wide each bucket is, in seconds.</summary>
    public double BucketSeconds { get; init; }

    /// <summary>Gets, per bucket, how many plays covered it.</summary>
    public IReadOnlyList<int> Buckets { get; init; } = [];
}

/// <summary>One play in a viewer's session list.</summary>
public sealed record ViewerSessionRowDto
{
    /// <summary>Gets the watch id.</summary>
    public Guid WatchId { get; init; }

    /// <summary>Gets the player's name.</summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>Gets the video's title.</summary>
    public string VideoTitle { get; init; } = string.Empty;

    /// <summary>Gets when the play started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets when the play was last heard from.</summary>
    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>Gets the seconds played.</summary>
    public double WatchSeconds { get; init; }

    /// <summary>Gets the share of the video watched (0 to 1).</summary>
    public double Completion { get; init; }
}

/// <summary>What the viewers list is filtered by.</summary>
/// <param name="Search">Part of an email or an organisation, or <see langword="null"/>.</param>
/// <param name="Activity">A lower-case activity filter, or <see langword="null"/> for all.</param>
/// <param name="PlayerId">Only viewers of links to this player, or <see langword="null"/>.</param>
public sealed record AdminViewerCriteria(string? Search, string? Activity, Guid? PlayerId);

/// <summary>
/// Reads the console's viewers with SQL projections, paged in SQL. A recorded exception to "conditions in specifications"
/// (decisions §2): a row spans viewers, recipients, links, watches and sessions with no navigations between them.
/// </summary>
public interface IViewerReader
{
    /// <summary>Reads one page of viewers, most recently seen first.</summary>
    /// <param name="criteria">The filters.</param>
    /// <param name="request">The page.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<AdminViewerRowDto>> PageAsync(AdminViewerCriteria criteria, PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads one viewer's profile.</summary>
    /// <param name="viewerId">The viewer.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The profile, or <see langword="null"/>.</returns>
    Task<AdminViewerDto?> GetAsync(Guid viewerId, CancellationToken cancellationToken = default);

    /// <summary>Reads one page of the players a viewer can open, one row per link and player.</summary>
    /// <param name="viewerId">The viewer.</param>
    /// <param name="request">The page.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<ViewerAccessRowDto>> AccessPageAsync(Guid viewerId, PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads one page of a viewer's watch log, one row per video.</summary>
    /// <param name="email">The viewer's email.</param>
    /// <param name="playerId">Only this player's videos, or <see langword="null"/>.</param>
    /// <param name="request">The page.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<ViewerVideoRowDto>> VideosPageAsync(EmailAddress email, Guid? playerId, PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the heatmap of one video for one viewer.</summary>
    /// <param name="email">The viewer's email.</param>
    /// <param name="assetId">The video.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The heatmap, or <see langword="null"/> when the viewer never played the video.</returns>
    Task<ViewerHeatmapDto?> HeatmapAsync(EmailAddress email, Guid assetId, CancellationToken cancellationToken = default);
}

/// <summary>Reads the viewers list (<c>GET /admin/viewers</c>), for admins only.</summary>
/// <param name="Paging">The page.</param>
/// <param name="Search">Part of an email or an organisation.</param>
/// <param name="Activity">The activity filter.</param>
/// <param name="PlayerId">Only viewers of links to this player.</param>
public sealed record GetAdminViewersQuery(PageRequest Paging, string? Search, string? Activity, Guid? PlayerId) : IPagedQuery<AdminViewerRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetAdminViewersQuery"/> on the server (decisions §1b rule 3); unknown filters are a 400.</summary>
public sealed class GetAdminViewersQueryValidator : AbstractValidator<GetAdminViewersQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAdminViewersQueryValidator()
    {
        RuleFor(query => query.Paging).ValidPaging();
        RuleFor(query => query.Search).MaximumLength(100).OverridePropertyName("q");
        RuleFor(query => query.Activity)
            .Must(activity => ViewerAccountKind.ActivityFilters.Contains(activity!, StringComparer.OrdinalIgnoreCase))
            .When(query => !string.IsNullOrEmpty(query.Activity))
            .WithMessage("Use all, active or never.")
            .OverridePropertyName("activity");
    }
}

/// <summary>Handles <see cref="GetAdminViewersQuery"/>.</summary>
/// <param name="reader">Reads the viewers.</param>
public sealed class GetAdminViewersQueryHandler(IViewerReader reader) : IQueryHandler<GetAdminViewersQuery, PagedResult<AdminViewerRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    public Task<PagedResult<AdminViewerRowDto>> Handle(GetAdminViewersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var activity = string.IsNullOrEmpty(request.Activity) ? null : request.Activity.ToLowerInvariant();
        return reader.PageAsync(new AdminViewerCriteria(search, activity, request.PlayerId), request.Paging, cancellationToken);
    }
}

/// <summary>Reads a viewer's profile (<c>GET /admin/viewers/{id}</c>), for admins only.</summary>
/// <param name="ViewerId">The viewer.</param>
public sealed record GetAdminViewerQuery(Guid ViewerId) : IQuery<AdminViewerDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetAdminViewerQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetAdminViewerQueryValidator : AbstractValidator<GetAdminViewerQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAdminViewerQueryValidator() => RuleFor(query => query.ViewerId).ValidId().OverridePropertyName("viewerId");
}

/// <summary>Handles <see cref="GetAdminViewerQuery"/>.</summary>
/// <param name="reader">Reads the profile.</param>
public sealed class GetAdminViewerQueryHandler(IViewerReader reader) : IQueryHandler<GetAdminViewerQuery, AdminViewerDto>
{
    /// <summary>Reads the profile.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="NotFoundException">There is no such viewer.</exception>
    public async Task<AdminViewerDto> Handle(GetAdminViewerQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await reader.GetAsync(request.ViewerId, cancellationToken) ?? throw new NotFoundException("Viewer");
    }
}

/// <summary>Reads the players a viewer can open (<c>GET /admin/viewers/{id}/access</c>), for admins only.</summary>
/// <param name="ViewerId">The viewer.</param>
/// <param name="Paging">The page.</param>
public sealed record GetViewerAccessQuery(Guid ViewerId, PageRequest Paging) : IPagedQuery<ViewerAccessRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetViewerAccessQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetViewerAccessQueryValidator : AbstractValidator<GetViewerAccessQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetViewerAccessQueryValidator()
    {
        RuleFor(query => query.ViewerId).ValidId().OverridePropertyName("viewerId");
        RuleFor(query => query.Paging).ValidPaging();
    }
}

/// <summary>Handles <see cref="GetViewerAccessQuery"/>.</summary>
/// <param name="viewers">The viewers.</param>
/// <param name="reader">Reads the access.</param>
public sealed class GetViewerAccessQueryHandler(IRepository<Viewer, Guid> viewers, IViewerReader reader) : IQueryHandler<GetViewerAccessQuery, PagedResult<ViewerAccessRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="NotFoundException">There is no such viewer.</exception>
    public async Task<PagedResult<ViewerAccessRowDto>> Handle(GetViewerAccessQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await viewers.GetByIdAsync(request.ViewerId, cancellationToken) ?? throw new NotFoundException("Viewer");
        return await reader.AccessPageAsync(request.ViewerId, request.Paging, cancellationToken);
    }
}

/// <summary>Reads a viewer's watch log (<c>GET /admin/viewers/{id}/videos</c>), for admins only.</summary>
/// <param name="ViewerId">The viewer.</param>
/// <param name="PlayerId">Only this player's videos, or <see langword="null"/>.</param>
/// <param name="Paging">The page.</param>
public sealed record GetViewerVideosQuery(Guid ViewerId, Guid? PlayerId, PageRequest Paging) : IPagedQuery<ViewerVideoRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetViewerVideosQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetViewerVideosQueryValidator : AbstractValidator<GetViewerVideosQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetViewerVideosQueryValidator()
    {
        RuleFor(query => query.ViewerId).ValidId().OverridePropertyName("viewerId");
        RuleFor(query => query.Paging).ValidPaging();
    }
}

/// <summary>Handles <see cref="GetViewerVideosQuery"/>.</summary>
/// <param name="viewers">The viewers.</param>
/// <param name="reader">Reads the log.</param>
public sealed class GetViewerVideosQueryHandler(IRepository<Viewer, Guid> viewers, IViewerReader reader) : IQueryHandler<GetViewerVideosQuery, PagedResult<ViewerVideoRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="NotFoundException">There is no such viewer.</exception>
    public async Task<PagedResult<ViewerVideoRowDto>> Handle(GetViewerVideosQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewer = await viewers.GetByIdAsync(request.ViewerId, cancellationToken) ?? throw new NotFoundException("Viewer");
        return await reader.VideosPageAsync(viewer.Email, request.PlayerId, request.Paging, cancellationToken);
    }
}

/// <summary>Reads a viewer's replay heatmap of a video (<c>GET /admin/viewers/{id}/videos/{assetId}/heatmap</c>), for admins only.</summary>
/// <param name="ViewerId">The viewer.</param>
/// <param name="AssetId">The video.</param>
public sealed record GetViewerHeatmapQuery(Guid ViewerId, Guid AssetId) : IQuery<ViewerHeatmapDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetViewerHeatmapQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetViewerHeatmapQueryValidator : AbstractValidator<GetViewerHeatmapQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetViewerHeatmapQueryValidator()
    {
        RuleFor(query => query.ViewerId).ValidId().OverridePropertyName("viewerId");
        RuleFor(query => query.AssetId).ValidId().OverridePropertyName("assetId");
    }
}

/// <summary>Handles <see cref="GetViewerHeatmapQuery"/>.</summary>
/// <param name="viewers">The viewers.</param>
/// <param name="reader">Reads the heatmap.</param>
public sealed class GetViewerHeatmapQueryHandler(IRepository<Viewer, Guid> viewers, IViewerReader reader) : IQueryHandler<GetViewerHeatmapQuery, ViewerHeatmapDto>
{
    /// <summary>Reads the heatmap.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The heatmap.</returns>
    /// <exception cref="NotFoundException">There is no such viewer, or they never played the video.</exception>
    public async Task<ViewerHeatmapDto> Handle(GetViewerHeatmapQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewer = await viewers.GetByIdAsync(request.ViewerId, cancellationToken) ?? throw new NotFoundException("Viewer");
        return await reader.HeatmapAsync(viewer.Email, request.AssetId, cancellationToken) ?? throw new NotFoundException("Watch");
    }
}

/// <summary>Every watch of one email; the cursor orders them by when they started.</summary>
public sealed class WatchesOfEmailSpec : Specification<WatchSession>
{
    /// <summary>Selects the watches.</summary>
    /// <param name="email">The viewer's email.</param>
    public WatchesOfEmailSpec(EmailAddress email) => Query.Where(watch => watch.Email == email);
}

/// <summary>Reads a viewer's plays, newest first (<c>GET /admin/viewers/{id}/sessions</c>), for admins only.</summary>
/// <param name="ViewerId">The viewer.</param>
/// <param name="Paging">The cursor page.</param>
public sealed record GetViewerSessionsQuery(Guid ViewerId, CursorRequest Paging) : ICursorQuery<ViewerSessionRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetViewerSessionsQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetViewerSessionsQueryValidator : AbstractValidator<GetViewerSessionsQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetViewerSessionsQueryValidator()
    {
        RuleFor(query => query.ViewerId).ValidId().OverridePropertyName("viewerId");
        RuleFor(query => query.Paging).ValidCursorPaging();
    }
}

/// <summary>Handles <see cref="GetViewerSessionsQuery"/>.</summary>
/// <param name="viewers">The viewers.</param>
/// <param name="pages">The paged reader.</param>
/// <param name="lookup">Reads names for the page.</param>
public sealed class GetViewerSessionsQueryHandler(IRepository<Viewer, Guid> viewers, IPagedReader pages, IAdminLookup lookup) : IQueryHandler<GetViewerSessionsQuery, CursorResult<ViewerSessionRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page and the cursor for the next one.</returns>
    /// <exception cref="NotFoundException">There is no such viewer.</exception>
    public async Task<CursorResult<ViewerSessionRowDto>> Handle(GetViewerSessionsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewer = await viewers.GetByIdAsync(request.ViewerId, cancellationToken) ?? throw new NotFoundException("Viewer");
        var page = await pages.CursorPageAsync(new WatchesOfEmailSpec(viewer.Email), watch => watch.StartedAt, CursorDirection.Descending, request.Paging, watch => watch, cancellationToken);
        var players = await lookup.PlayerNamesAsync([.. page.Items.Select(watch => watch.PlayerId).Distinct()], cancellationToken);
        var titles = await lookup.AssetTitlesAsync([.. page.Items.Select(watch => watch.AssetId).Distinct()], cancellationToken);
        var rows = page.Items.Select(watch => new ViewerSessionRowDto
        {
            WatchId = watch.Id,
            PlayerName = players.GetValueOrDefault(watch.PlayerId, string.Empty),
            VideoTitle = titles.GetValueOrDefault(watch.AssetId, string.Empty),
            StartedAt = watch.StartedAt,
            LastSeenAt = watch.LastSeenAt,
            WatchSeconds = watch.WatchSeconds,
            Completion = watch.Completion,
        }).ToList();
        return new CursorResult<ViewerSessionRowDto>(rows, page.NextCursor);
    }
}
