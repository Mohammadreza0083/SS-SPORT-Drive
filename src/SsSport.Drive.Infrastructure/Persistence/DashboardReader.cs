using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads the console dashboard (A-2) with a few aggregate queries. A recorded exception to "conditions in
/// specifications" (decisions §2): the numbers span many aggregates and are only counts, sums and averages.
/// </summary>
/// <param name="context">The database.</param>
public sealed class DashboardReader(DbContext context) : IDashboardReader
{
    /// <summary>How many players the top list holds.</summary>
    private const int TopCount = 5;

    /// <inheritdoc />
    public async Task<DashboardDto> ReadAsync(DashboardWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        var watches = context.Set<WatchSession>().AsNoTracking();
        var inPeriod = watches.Where(watch => watch.StartedAt >= window.From && watch.StartedAt < window.To);

        var kpis = new DashboardKpisDto
        {
            LivePlayers = await context.Set<Player>().CountAsync(player => player.Status == PlayerStatus.Published, cancellationToken),
            Views = await inPeriod.CountAsync(cancellationToken),
            PreviousViews = await watches.CountAsync(watch => watch.StartedAt >= window.PreviousFrom && watch.StartedAt < window.From, cancellationToken),
            UniqueViewers = await inPeriod.Select(watch => watch.Email).Distinct().CountAsync(cancellationToken),
            AverageCompletion = await inPeriod.AverageAsync(watch => (double?)watch.Completion, cancellationToken),
            PendingAccessRequests = await context.Set<AccessRequest>().CountAsync(request => request.Status == AccessRequestStatus.Pending, cancellationToken),
            WaitingPlayers = await context.Set<Player>().CountAsync(player => player.Status == PlayerStatus.Waiting, cancellationToken),
        };

        var attention = new NeedsAttentionDto
        {
            AccessRequests = kpis.PendingAccessRequests,
            WaitingPlayers = kpis.WaitingPlayers,
            FailedFiles = await context.Set<Asset>().CountAsync(asset => asset.State == ProcessingState.Failed, cancellationToken),
            FailedEmails = await context.Set<OutgoingEmail>().CountAsync(email => email.Status == EmailStatus.Failed, cancellationToken),
            RetryingEmails = await context.Set<OutgoingEmail>().CountAsync(email => email.Status == EmailStatus.Queued && email.Attempts > 0, cancellationToken),
            LostFolders = await context.Set<DriveFolder>().CountAsync(folder => folder.Access == FolderAccess.Lost, cancellationToken),
        };

        var top = await inPeriod
            .GroupBy(watch => watch.PlayerId)
            .Select(group => new { PlayerId = group.Key, Views = group.Count(), Completion = group.Average(watch => watch.Completion) })
            .Join(context.Set<Player>().AsNoTracking(), group => group.PlayerId, player => player.Id, (group, player) => new { group.PlayerId, group.Views, group.Completion, player.Name })
            .OrderByDescending(row => row.Views)
            .ThenBy(row => row.Name)
            .Take(TopCount)
            .ToListAsync(cancellationToken);

        return new DashboardDto
        {
            Period = window.Period,
            Kpis = kpis,
            ViewsByDay = await ViewsByDayAsync(window, cancellationToken),
            NeedsAttention = attention,
            TopPlayers = [.. top.Select(row => new TopPlayerDto { PlayerId = row.PlayerId, Name = row.Name?.Value ?? string.Empty, Views = row.Views, AverageCompletion = row.Completion })],
        };
    }

    /// <summary>Counts the watches of each day of the window in SQL and fills the empty days with zero.</summary>
    /// <param name="window">The span.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One entry per day, oldest first.</returns>
    private async Task<IReadOnlyList<DayViewsDto>> ViewsByDayAsync(DashboardWindow window, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQuery<DayRow>(
            $"""
            SELECT floor(extract(epoch FROM (started_at - {window.From})) / 86400)::int AS day, count(*)::int AS views
            FROM watch_sessions
            WHERE started_at >= {window.From} AND started_at < {window.To}
            GROUP BY 1
            """).ToListAsync(cancellationToken);
        var byDay = rows.ToDictionary(row => row.Day, row => row.Views);
        return [.. Enumerable.Range(0, window.Days).Select(day => new DayViewsDto(
            window.FirstDay.AddDays(day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            byDay.GetValueOrDefault(day)))];
    }

    /// <summary>One day of the views query.</summary>
    private sealed record DayRow
    {
        /// <summary>Gets the day's index from the window's start.</summary>
        public int Day { get; init; }

        /// <summary>Gets the watches that started that day.</summary>
        public int Views { get; init; }
    }
}

/// <summary>Reads the names and viewer facts the console joins onto its lists, for one page at a time.</summary>
/// <param name="context">The database.</param>
public sealed class AdminLookup(DbContext context) : IAdminLookup
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> PlayerNamesAsync(IReadOnlyCollection<Guid> playerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        var rows = await context.Set<Player>().AsNoTracking().Where(player => playerIds.Contains(player.Id)).Select(player => new { player.Id, player.Name }).ToListAsync(cancellationToken);
        return rows.Where(row => row.Name is not null).ToDictionary(row => row.Id, row => row.Name!.Value);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> AssetTitlesAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        var rows = await context.Set<Asset>().AsNoTracking().Where(asset => assetIds.Contains(asset.Id)).Select(asset => new { asset.Id, asset.Title }).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => row.Title);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ViewerFacts>> ViewersByEmailAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emails);
        var addresses = emails.ToArray();
        var rows = await context.Database.SqlQuery<ViewerFactsRow>(
            $"""
            SELECT v.email AS email, v.id AS viewer_id,
                (SELECT r.organisation FROM recipients r WHERE v.email = ANY (r.emails) ORDER BY r.created_at DESC LIMIT 1) AS organisation
            FROM viewers v
            WHERE v.email = ANY ({addresses})
            """).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Email, row => new ViewerFacts(row.ViewerId, row.Organisation), StringComparer.Ordinal);
    }

    /// <summary>One row of the viewer facts query.</summary>
    private sealed record ViewerFactsRow
    {
        /// <summary>Gets the address.</summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>Gets the viewer's id.</summary>
        public Guid ViewerId { get; init; }

        /// <summary>Gets the newest recipient's organisation.</summary>
        public string? Organisation { get; init; }
    }
}
