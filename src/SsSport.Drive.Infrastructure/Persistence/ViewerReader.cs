using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads the console's viewers (A-7, A-11): SQL projections composed with the filters in LINQ and paged in SQL. A recorded
/// exception to "conditions in specifications" (decisions §2): a row spans viewers, recipients, links, watches and sessions
/// with no navigations between them. The per-video metrics merge the watched ranges in memory, for one page's videos only.
/// </summary>
/// <param name="context">The database.</param>
public sealed class ViewerReader(DbContext context) : IViewerReader
{
    /// <summary>The character that escapes a wildcard in a <c>LIKE</c> pattern.</summary>
    private const string LikeEscape = "\\";

    /// <inheritdoc />
    public async Task<PagedResult<AdminViewerRowDto>> PageAsync(AdminViewerCriteria criteria, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(request);
        var rows = Rows(criteria.PlayerId);
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var pattern = $"%{EscapeLike(criteria.Search.Trim())}%";
            rows = rows.Where(row => EF.Functions.ILike(row.Email, pattern, LikeEscape) || EF.Functions.ILike(row.Organisation!, pattern, LikeEscape));
        }

        rows = criteria.Activity switch
        {
            "active" => rows.Where(row => row.Plays > 0),
            "never" => rows.Where(row => row.Plays == 0),
            _ => rows,
        };
        var totalCount = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(row => row.LastSeenAt == null)
            .ThenByDescending(row => row.LastSeenAt)
            .ThenBy(row => row.Email)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return PagedResult<AdminViewerRowDto>.Create([.. page.Select(row => new AdminViewerRowDto
        {
            Id = row.Id,
            Email = row.Email,
            Organisation = row.Organisation,
            Players = row.PlayerNames ?? [],
            PlayerCount = row.PlayerCount,
            Plays = row.Plays,
            WatchSeconds = row.WatchSeconds,
            LastSeenAt = row.LastSeenAt,
            AccessUntil = row.AccessUntil,
            Account = row.Account,
        })], request, totalCount);
    }

    /// <inheritdoc />
    public async Task<AdminViewerDto?> GetAsync(Guid viewerId, CancellationToken cancellationToken = default)
    {
        var row = await Rows(null).Where(item => item.Id == viewerId).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var email = EmailAddress.Create(row.Email);
        var firstSeen = await context.Set<Domain.Viewers.Viewer>().AsNoTracking().Where(viewer => viewer.Id == viewerId).Select(viewer => viewer.FirstSeenAt).SingleAsync(cancellationToken);
        var completion = await context.Set<WatchSession>().AsNoTracking().Where(watch => watch.Email == email).AverageAsync(watch => (double?)watch.Completion, cancellationToken);
        return new AdminViewerDto
        {
            Id = row.Id,
            Email = row.Email,
            Organisation = row.Organisation,
            FirstSeenAt = firstSeen,
            LastSeenAt = row.LastSeenAt,
            Account = row.Account,
            Plays = row.Plays,
            WatchSeconds = row.WatchSeconds,
            PlayersAccessible = row.PlayerCount,
            AverageCompletion = completion,
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<ViewerAccessRowDto>> AccessPageAsync(Guid viewerId, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rows = context.Database.SqlQuery<AccessRow>(
            $"""
            SELECT
                p.id AS player_id,
                p.full_name AS player_name,
                l.id AS link_id,
                l.code AS code,
                CASE
                    WHEN l.status = 'Revoked' THEN 'Revoked'
                    WHEN l.status = 'Paused' THEN 'Paused'
                    WHEN l.expires_at IS NOT NULL AND l.expires_at <= now() THEN 'Expired'
                    ELSE 'Active'
                END AS status,
                l.created_at AS granted_at,
                l.expires_at AS expires_at,
                COALESCE((SELECT sum(w.plays) FROM watch_sessions w WHERE w.share_link_id = l.id AND w.player_id = p.id AND w.email = v.email), 0)::int AS plays,
                COALESCE((SELECT sum((r2->>'to')::float8 - (r2->>'from')::float8)
                          FROM watch_sessions w, jsonb_array_elements(w.ranges) r2
                          WHERE w.share_link_id = l.id AND w.player_id = p.id AND w.email = v.email), 0) AS watch_seconds
            FROM viewers v
            JOIN recipients r ON v.email = ANY (r.emails)
            JOIN share_links l ON l.recipient_id = r.id
            CROSS JOIN LATERAL unnest(l.player_ids) AS pid
            JOIN players p ON p.id = pid
            WHERE v.id = {viewerId}
            """);
        var totalCount = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(row => row.GrantedAt)
            .ThenBy(row => row.PlayerId)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return PagedResult<ViewerAccessRowDto>.Create([.. page.Select(row => new ViewerAccessRowDto
        {
            PlayerId = row.PlayerId,
            PlayerName = row.PlayerName ?? string.Empty,
            LinkId = row.LinkId,
            Code = row.Code,
            Status = row.Status,
            GrantedAt = row.GrantedAt,
            ExpiresAt = row.ExpiresAt,
            Plays = row.Plays,
            WatchSeconds = row.WatchSeconds,
        })], request, totalCount);
    }

    /// <inheritdoc />
    public async Task<PagedResult<ViewerVideoRowDto>> VideosPageAsync(EmailAddress email, Guid? playerId, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(request);
        var watches = context.Set<WatchSession>().AsNoTracking().Where(watch => watch.Email == email);
        if (playerId is { } player)
        {
            watches = watches.Where(watch => watch.PlayerId == player);
        }

        var groups = watches
            .GroupBy(watch => new { watch.AssetId, watch.PlayerId })
            .Select(group => new { group.Key.AssetId, group.Key.PlayerId, LastWatchedAt = group.Max(watch => watch.LastSeenAt) });
        var totalCount = await groups.CountAsync(cancellationToken);
        var page = await groups
            .OrderByDescending(group => group.LastWatchedAt)
            .ThenBy(group => group.AssetId)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var assetIds = page.Select(group => group.AssetId).ToList();
        var playerIds = page.Select(group => group.PlayerId).Distinct().ToList();
        var own = await watches.Where(watch => assetIds.Contains(watch.AssetId)).ToListAsync(cancellationToken);
        var titles = await context.Set<Asset>().AsNoTracking().Where(asset => assetIds.Contains(asset.Id)).Select(asset => new { asset.Id, asset.Title }).ToDictionaryAsync(asset => asset.Id, asset => asset.Title, cancellationToken);
        var names = (await context.Set<Domain.Players.Player>().AsNoTracking().Where(item => playerIds.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
            .ToDictionary(item => item.Id, item => item.Name?.Value ?? string.Empty);

        var items = page.Select(group =>
        {
            var mine = own.Where(watch => watch.AssetId == group.AssetId && watch.PlayerId == group.PlayerId).ToList();
            var ranges = mine.SelectMany(watch => watch.Ranges).ToList();
            var duration = mine.Max(watch => watch.DurationSeconds);
            return new ViewerVideoRowDto
            {
                PlayerId = group.PlayerId,
                PlayerName = names.GetValueOrDefault(group.PlayerId, string.Empty),
                AssetId = group.AssetId,
                Title = titles.GetValueOrDefault(group.AssetId, string.Empty),
                Plays = mine.Sum(watch => watch.Plays),
                WatchSeconds = WatchMetrics.WatchSeconds(ranges),
                UniqueSeconds = WatchMetrics.UniqueSeconds(ranges),
                Completion = WatchMetrics.Completion(ranges, duration),
                LastWatchedAt = group.LastWatchedAt,
            };
        }).ToList();
        return PagedResult<ViewerVideoRowDto>.Create(items, request, totalCount);
    }

    /// <inheritdoc />
    public async Task<ViewerHeatmapDto?> HeatmapAsync(EmailAddress email, Guid assetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        var watches = await context.Set<WatchSession>().AsNoTracking().Where(watch => watch.Email == email && watch.AssetId == assetId).ToListAsync(cancellationToken);
        if (watches.Count == 0)
        {
            return null;
        }

        var title = await context.Set<Asset>().AsNoTracking().Where(asset => asset.Id == assetId).Select(asset => asset.Title).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var duration = watches.Max(watch => watch.DurationSeconds);
        var bucketSeconds = ViewerAccountKind.BucketSeconds(duration);
        return new ViewerHeatmapDto
        {
            AssetId = assetId,
            Title = title,
            DurationSeconds = duration,
            BucketSeconds = bucketSeconds,
            Buckets = WatchMetrics.Hotspots(watches.SelectMany(watch => watch.Ranges), duration, bucketSeconds),
        };
    }

    /// <summary>Escapes <c>LIKE</c> wildcards so a search for <c>50%</c> means just that.</summary>
    /// <param name="text">The search text.</param>
    /// <returns>The escaped text.</returns>
    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    /// <summary>Every viewer with what they can open and how they watched.</summary>
    /// <param name="playerId">Only viewers of links to this player, or <see langword="null"/>.</param>
    /// <returns>The composable query.</returns>
    private IQueryable<ViewerRow> Rows(Guid? playerId) => context.Database.SqlQuery<ViewerRow>(
        $"""
        SELECT
            v.id AS id,
            v.email AS email,
            (SELECT r.organisation FROM recipients r WHERE v.email = ANY (r.emails) ORDER BY r.created_at DESC LIMIT 1) AS organisation,
            ARRAY(
                SELECT DISTINCT p.full_name
                FROM recipients r
                JOIN share_links l ON l.recipient_id = r.id AND l.status <> 'Revoked'
                CROSS JOIN LATERAL unnest(l.player_ids) AS pid
                JOIN players p ON p.id = pid
                WHERE v.email = ANY (r.emails) AND p.full_name IS NOT NULL
                ORDER BY 1
                LIMIT 3) AS player_names,
            (SELECT count(DISTINCT pid)::int
                FROM recipients r
                JOIN share_links l ON l.recipient_id = r.id AND l.status <> 'Revoked'
                CROSS JOIN LATERAL unnest(l.player_ids) AS pid
                WHERE v.email = ANY (r.emails)) AS player_count,
            COALESCE((SELECT sum(w.plays) FROM watch_sessions w WHERE w.email = v.email), 0)::int AS plays,
            COALESCE((SELECT sum((r2->>'to')::float8 - (r2->>'from')::float8)
                      FROM watch_sessions w, jsonb_array_elements(w.ranges) r2 WHERE w.email = v.email), 0) AS watch_seconds,
            (SELECT max(w.last_seen_at) FROM watch_sessions w WHERE w.email = v.email) AS last_seen_at,
            (SELECT max(l.expires_at) FROM recipients r JOIN share_links l ON l.recipient_id = r.id AND l.status <> 'Revoked' WHERE v.email = ANY (r.emails)) AS access_until,
            CASE
                WHEN EXISTS (SELECT 1 FROM viewer_accounts a WHERE a.email = v.email) THEN 'SignedIn'
                WHEN EXISTS (SELECT 1 FROM viewer_sessions s WHERE s.email = v.email) THEN 'LinkOnly'
                ELSE 'NotYet'
            END AS account
        FROM viewers v
        WHERE {playerId}::uuid IS NULL OR EXISTS (
            SELECT 1 FROM recipients r JOIN share_links l ON l.recipient_id = r.id AND l.status <> 'Revoked'
            WHERE v.email = ANY (r.emails) AND {playerId}::uuid = ANY (l.player_ids))
        """);

    /// <summary>One viewer of the projection.</summary>
    private sealed record ViewerRow
    {
        /// <summary>Gets the viewer id.</summary>
        public Guid Id { get; init; }

        /// <summary>Gets the email.</summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>Gets the organisation, or <see langword="null"/>.</summary>
        public string? Organisation { get; init; }

        /// <summary>Gets up to three player names.</summary>
        public string[]? PlayerNames { get; init; }

        /// <summary>Gets how many players the viewer can open.</summary>
        public int PlayerCount { get; init; }

        /// <summary>Gets the plays.</summary>
        public int Plays { get; init; }

        /// <summary>Gets the seconds played.</summary>
        public double WatchSeconds { get; init; }

        /// <summary>Gets when the viewer was last heard from, or <see langword="null"/>.</summary>
        public DateTimeOffset? LastSeenAt { get; init; }

        /// <summary>Gets when the latest open link expires, or <see langword="null"/>.</summary>
        public DateTimeOffset? AccessUntil { get; init; }

        /// <summary>Gets how the viewer signs in.</summary>
        public string Account { get; init; } = string.Empty;
    }

    /// <summary>One link and player of a viewer's access.</summary>
    private sealed record AccessRow
    {
        /// <summary>Gets the player id.</summary>
        public Guid PlayerId { get; init; }

        /// <summary>Gets the player's name.</summary>
        public string? PlayerName { get; init; }

        /// <summary>Gets the link id.</summary>
        public Guid LinkId { get; init; }

        /// <summary>Gets the link's code.</summary>
        public string Code { get; init; } = string.Empty;

        /// <summary>Gets the link's access now.</summary>
        public string Status { get; init; } = string.Empty;

        /// <summary>Gets when the link was made.</summary>
        public DateTimeOffset GrantedAt { get; init; }

        /// <summary>Gets when the link expires, or <see langword="null"/>.</summary>
        public DateTimeOffset? ExpiresAt { get; init; }

        /// <summary>Gets the plays through this link and player.</summary>
        public int Plays { get; init; }

        /// <summary>Gets the seconds played through this link and player.</summary>
        public double WatchSeconds { get; init; }
    }
}
