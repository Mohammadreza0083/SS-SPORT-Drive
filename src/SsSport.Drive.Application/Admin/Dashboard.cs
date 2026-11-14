using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Application.Admin;

/// <summary>The numbers at the top of the console dashboard.</summary>
public sealed record DashboardKpisDto
{
    /// <summary>Gets how many players are published.</summary>
    public int LivePlayers { get; init; }

    /// <summary>Gets how many watches started in the period.</summary>
    public int Views { get; init; }

    /// <summary>Gets how many watches started in the period before it.</summary>
    public int PreviousViews { get; init; }

    /// <summary>Gets how many different emails watched in the period.</summary>
    public int UniqueViewers { get; init; }

    /// <summary>Gets the average share of a video watched (0 to 1), or <see langword="null"/> when nothing was watched.</summary>
    public double? AverageCompletion { get; init; }

    /// <summary>Gets how many access requests wait for an admin.</summary>
    public int PendingAccessRequests { get; init; }

    /// <summary>Gets how many players wait for details.</summary>
    public int WaitingPlayers { get; init; }
}

/// <summary>Views on one Tehran calendar day.</summary>
/// <param name="Date">The day as <c>yyyy-MM-dd</c>.</param>
/// <param name="Views">The watches that started that day.</param>
public sealed record DayViewsDto(string Date, int Views);

/// <summary>What waits on an admin.</summary>
public sealed record NeedsAttentionDto
{
    /// <summary>Gets the access requests waiting for a decision.</summary>
    public int AccessRequests { get; init; }

    /// <summary>Gets the players waiting for details.</summary>
    public int WaitingPlayers { get; init; }

    /// <summary>Gets the files whose processing failed.</summary>
    public int FailedFiles { get; init; }

    /// <summary>Gets the emails that used up every attempt.</summary>
    public int FailedEmails { get; init; }

    /// <summary>Gets the emails still queued after a failed attempt.</summary>
    public int RetryingEmails { get; init; }

    /// <summary>Gets the Drive folders that lost access.</summary>
    public int LostFolders { get; init; }
}

/// <summary>A player in the dashboard's top list.</summary>
public sealed record TopPlayerDto
{
    /// <summary>Gets the player id.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the player's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the watches that started in the period.</summary>
    public int Views { get; init; }

    /// <summary>Gets the average share of a video watched (0 to 1).</summary>
    public double AverageCompletion { get; init; }
}

/// <summary>The console dashboard (A-2).</summary>
public sealed record DashboardDto
{
    /// <summary>Gets the period: <c>7d</c>, <c>30d</c> or <c>90d</c>.</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>Gets the numbers at the top.</summary>
    public DashboardKpisDto Kpis { get; init; } = new();

    /// <summary>Gets the views per Tehran day, oldest first, ending today, with empty days as zero.</summary>
    public IReadOnlyList<DayViewsDto> ViewsByDay { get; init; } = [];

    /// <summary>Gets what waits on an admin.</summary>
    public NeedsAttentionDto NeedsAttention { get; init; } = new();

    /// <summary>Gets the five most watched players of the period.</summary>
    public IReadOnlyList<TopPlayerDto> TopPlayers { get; init; } = [];
}

/// <summary>The span a dashboard covers, in UTC instants that start on Tehran midnights.</summary>
/// <param name="Period">The period name.</param>
/// <param name="Days">How many days it covers.</param>
/// <param name="From">The first instant of the first day.</param>
/// <param name="To">The instant after the last day (the start of tomorrow in Tehran).</param>
/// <param name="PreviousFrom">The start of the period before.</param>
/// <param name="FirstDay">The first Tehran calendar day.</param>
public sealed record DashboardWindow(string Period, int Days, DateTimeOffset From, DateTimeOffset To, DateTimeOffset PreviousFrom, DateOnly FirstDay);

/// <summary>Reads the dashboard's numbers with a few aggregate SQL queries; nothing is loaded whole.</summary>
public interface IDashboardReader
{
    /// <summary>Reads the dashboard.</summary>
    /// <param name="window">The span.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The dashboard.</returns>
    Task<DashboardDto> ReadAsync(DashboardWindow window, CancellationToken cancellationToken = default);
}

/// <summary>The dashboard's periods.</summary>
public static class DashboardPeriods
{
    /// <summary>The period shown when none is asked for.</summary>
    public const string Default = "30d";

    /// <summary>Gets how many days each period covers.</summary>
    public static IReadOnlyDictionary<string, int> Days { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["7d"] = 7,
        ["30d"] = 30,
        ["90d"] = 90,
    };

    /// <summary>Builds the window that ends today in the given time zone.</summary>
    /// <param name="period">The period name.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="zone">The time zone of the days (Tehran).</param>
    /// <returns>The window.</returns>
    public static DashboardWindow WindowFor(string period, DateTimeOffset now, TimeZoneInfo zone)
    {
        var days = Days[period];
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var first = today.AddDays(-(days - 1));
        var from = StartOf(first, zone);
        var to = StartOf(today.AddDays(1), zone);
        return new DashboardWindow(period, days, from, to, StartOf(first.AddDays(-days), zone), first);
    }

    /// <summary>Gets the UTC instant a calendar day starts in a time zone.</summary>
    /// <param name="day">The day.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The instant, with a zero offset.</returns>
    private static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
}

/// <summary>Reads the dashboard (<c>GET /admin/dashboard</c>), for admins only.</summary>
/// <param name="Period">The period, or <see langword="null"/> for 30 days.</param>
public sealed record GetDashboardQuery(string? Period) : IQuery<DashboardDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetDashboardQuery"/> on the server (decisions §1b rule 3); an unknown period is a 400.</summary>
public sealed class GetDashboardQueryValidator : AbstractValidator<GetDashboardQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetDashboardQueryValidator() =>
        RuleFor(query => query.Period)
            .Must(period => period is null || DashboardPeriods.Days.ContainsKey(period))
            .WithMessage("Use 7d, 30d or 90d.")
            .OverridePropertyName("period");
}

/// <summary>Handles <see cref="GetDashboardQuery"/>.</summary>
/// <param name="reader">Reads the numbers.</param>
/// <param name="settings">The notification rules, for the Tehran time zone.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetDashboardQueryHandler(IDashboardReader reader, INotificationSettings settings, TimeProvider timeProvider) : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    /// <summary>Reads the dashboard.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The dashboard.</returns>
    public Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var window = DashboardPeriods.WindowFor(request.Period ?? DashboardPeriods.Default, timeProvider.GetUtcNow(), settings.Rules.TimeZone);
        return reader.ReadAsync(window, cancellationToken);
    }
}

/// <summary>One watch in the live activity feed.</summary>
public sealed record ActivityRowDto
{
    /// <summary>Gets the watch id.</summary>
    public Guid WatchId { get; init; }

    /// <summary>Gets the viewer's id, for the profile link, or <see cref="Guid.Empty"/> when the address was never registered.</summary>
    public Guid ViewerId { get; init; }

    /// <summary>Gets the viewer's email; admins see viewer identities.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets the organisation of the recipient the email belongs to, if known.</summary>
    public string? Organisation { get; init; }

    /// <summary>Gets the player id.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the player's name.</summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>Gets the video's title.</summary>
    public string VideoTitle { get; init; } = string.Empty;

    /// <summary>Gets the share of the video watched so far (0 to 1).</summary>
    public double Completion { get; init; }

    /// <summary>Gets when the watch was last heard from (UTC).</summary>
    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>Gets a value indicating whether the viewer was heard from in the last minute.</summary>
    public bool IsLive { get; init; }
}

/// <summary>Every watch; the cursor orders them by when they were last heard from.</summary>
public sealed class AllWatchesSpec : Specification<WatchSession>;

/// <summary>Names and viewer facts the console joins onto its lists, read for one page at a time.</summary>
public interface IAdminLookup
{
    /// <summary>Reads player names.</summary>
    /// <param name="playerIds">The players.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The names by player id; players without a name are left out.</returns>
    Task<IReadOnlyDictionary<Guid, string>> PlayerNamesAsync(IReadOnlyCollection<Guid> playerIds, CancellationToken cancellationToken = default);

    /// <summary>Reads asset titles.</summary>
    /// <param name="assetIds">The assets.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The titles by asset id.</returns>
    Task<IReadOnlyDictionary<Guid, string>> AssetTitlesAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);

    /// <summary>Reads each email's viewer id and the organisation of its newest recipient.</summary>
    /// <param name="emails">The addresses.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The facts by address; addresses that were never registered are left out.</returns>
    Task<IReadOnlyDictionary<string, ViewerFacts>> ViewersByEmailAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default);
}

/// <summary>What the console knows about an email beyond the address itself.</summary>
/// <param name="ViewerId">The viewer's id.</param>
/// <param name="Organisation">The organisation of the newest recipient with this email, or <see langword="null"/>.</param>
public sealed record ViewerFacts(Guid ViewerId, string? Organisation);

/// <summary>Reads the live activity feed (<c>GET /admin/activity</c>), newest first, for admins only.</summary>
/// <param name="Paging">The cursor page.</param>
public sealed record GetActivityQuery(CursorRequest Paging) : ICursorQuery<ActivityRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetActivityQuery"/> on the server (decisions §1b rule 3); malformed paging is a 400.</summary>
public sealed class GetActivityQueryValidator : AbstractValidator<GetActivityQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetActivityQueryValidator() => RuleFor(query => query.Paging).ValidCursorPaging();
}

/// <summary>Handles <see cref="GetActivityQuery"/>: one cursor page of watches, then the names for that page only.</summary>
/// <param name="pages">The paged reader.</param>
/// <param name="lookup">Reads names and viewer facts.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetActivityQueryHandler(IPagedReader pages, IAdminLookup lookup, TimeProvider timeProvider) : IQueryHandler<GetActivityQuery, CursorResult<ActivityRowDto>>
{
    /// <summary>How recently a watch must have been heard from to count as live.</summary>
    private static readonly TimeSpan _liveWindow = TimeSpan.FromSeconds(60);

    /// <summary>Reads one page of the feed.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page and the cursor for the next one.</returns>
    public async Task<CursorResult<ActivityRowDto>> Handle(GetActivityQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await pages.CursorPageAsync(new AllWatchesSpec(), watch => watch.LastSeenAt, CursorDirection.Descending, request.Paging, watch => watch, cancellationToken);
        var players = await lookup.PlayerNamesAsync([.. page.Items.Select(watch => watch.PlayerId).Distinct()], cancellationToken);
        var titles = await lookup.AssetTitlesAsync([.. page.Items.Select(watch => watch.AssetId).Distinct()], cancellationToken);
        var viewers = await lookup.ViewersByEmailAsync([.. page.Items.Select(watch => watch.Email.Value).Distinct()], cancellationToken);
        var now = timeProvider.GetUtcNow();
        var rows = page.Items.Select(watch =>
        {
            viewers.TryGetValue(watch.Email.Value, out var facts);
            return new ActivityRowDto
            {
                WatchId = watch.Id,
                ViewerId = facts?.ViewerId ?? Guid.Empty,
                Email = watch.Email.Value,
                Organisation = facts?.Organisation,
                PlayerId = watch.PlayerId,
                PlayerName = players.GetValueOrDefault(watch.PlayerId, string.Empty),
                VideoTitle = titles.GetValueOrDefault(watch.AssetId, string.Empty),
                Completion = watch.Completion,
                LastSeenAt = watch.LastSeenAt,
                IsLive = now - watch.LastSeenAt <= _liveWindow,
            };
        }).ToList();
        return new CursorResult<ActivityRowDto>(rows, page.NextCursor);
    }
}
