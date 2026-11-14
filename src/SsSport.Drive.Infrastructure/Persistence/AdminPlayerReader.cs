using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads the console's players (A-3, A-4): one SQL projection with correlated counts, composed with the filters in LINQ and
/// paged in SQL. A recorded exception to "conditions in specifications" (decisions §2): a row spans players, files,
/// folders, watches and users, with no navigations between them.
/// </summary>
/// <param name="context">The database.</param>
public sealed class AdminPlayerReader(DbContext context) : IAdminPlayerReader
{
    /// <summary>The character that escapes a wildcard in a <c>LIKE</c> pattern.</summary>
    private const string LikeEscape = "\\";

    /// <inheritdoc />
    public async Task<PagedResult<AdminPlayerRowDto>> PageAsync(AdminPlayerCriteria criteria, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(request);
        var rows = Filter(Rows(), criteria);
        var totalCount = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return PagedResult<AdminPlayerRowDto>.Create([.. page.Select(ToDto)], request, totalCount);
    }

    /// <inheritdoc />
    public async Task<AdminPlayerRowDto?> GetRowAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var row = await Rows().Where(item => item.Id == playerId).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDto(row);
    }

    /// <inheritdoc />
    public async Task<AdminPlayerStats> GetStatsAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var viewers = await context.Database.SqlQuery<int>(
            $"""
            SELECT count(DISTINCT e.email)::int AS "Value"
            FROM (
                SELECT unnest(r.emails) AS email
                FROM share_links l JOIN recipients r ON r.id = l.recipient_id
                WHERE {playerId} = ANY (l.player_ids) AND l.status <> 'Revoked'
                UNION
                SELECT w.email FROM watch_sessions w WHERE w.player_id = {playerId}
            ) e
            """).SingleAsync(cancellationToken);
        var completion = await context.Set<WatchSession>().AsNoTracking().Where(watch => watch.PlayerId == playerId).AverageAsync(watch => (double?)watch.Completion, cancellationToken);
        return new AdminPlayerStats(viewers, completion);
    }

    /// <summary>Applies the list's filters.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="criteria">The filters.</param>
    /// <returns>The filtered rows.</returns>
    private static IQueryable<PlayerRow> Filter(IQueryable<PlayerRow> rows, AdminPlayerCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            var pattern = $"%{EscapeLike(search)}%";
            rows = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var volleyboxId)
                ? rows.Where(row => row.VolleyboxId == volleyboxId || EF.Functions.ILike(row.Name!, pattern, LikeEscape))
                : rows.Where(row => EF.Functions.ILike(row.Name!, pattern, LikeEscape));
        }

        if (criteria.Position is { } position)
        {
            rows = rows.Where(row => row.Positions!.Contains(position));
        }

        if (criteria.Status is { } status)
        {
            var name = char.ToUpperInvariant(status[0]) + status[1..];
            rows = rows.Where(row => row.Status == name);
        }

        return rows;
    }

    /// <summary>Escapes <c>LIKE</c> wildcards so a search for <c>50%</c> means just that.</summary>
    /// <param name="text">The search text.</param>
    /// <returns>The escaped text.</returns>
    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    /// <summary>Maps a row to its DTO.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The DTO.</returns>
    private static AdminPlayerRowDto ToDto(PlayerRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Positions = row.Positions ?? [],
        NationalityCode = row.NationalityCode,
        NationalityName = row.NationalityCode is null ? null : Nationality.Create(row.NationalityCode).CountryName,
        VideoCount = row.VideoCount,
        DocumentCount = row.DocumentCount,
        Viewers = row.Viewers,
        LastViewedAt = row.LastViewedAt,
        AnalystName = row.AnalystName,
        Status = row.Status,
        HasPhoto = row.HasPhoto,
        VolleyboxId = row.VolleyboxId,
        CreatedAt = row.CreatedAt,
    };

    /// <summary>
    /// Every player with its counts and console status: Waiting when unpublished; otherwise Attention with a failed file or a
    /// lost folder, Processing with files in progress, else Live.
    /// </summary>
    /// <returns>The composable query.</returns>
    private IQueryable<PlayerRow> Rows() => context.Database.SqlQuery<PlayerRow>(
        $"""
        SELECT
            p.id AS id,
            p.full_name AS name,
            p.positions AS positions,
            p.nationality AS nationality_code,
            (SELECT count(*)::int FROM assets a WHERE a.player_id = p.id AND a.kind = 'Video') AS video_count,
            (SELECT count(*)::int FROM assets a WHERE a.player_id = p.id AND a.kind = 'Document') AS document_count,
            (SELECT count(DISTINCT w.email)::int FROM watch_sessions w WHERE w.player_id = p.id) AS viewers,
            (SELECT max(w.last_seen_at) FROM watch_sessions w WHERE w.player_id = p.id) AS last_viewed_at,
            u.display_name AS analyst_name,
            CASE
                WHEN p.status <> 'Published' THEN 'Waiting'
                WHEN EXISTS (SELECT 1 FROM assets a WHERE a.player_id = p.id AND a.state = 'Failed')
                  OR EXISTS (SELECT 1 FROM drive_folders f WHERE f.player_id = p.id AND f.access = 'Lost') THEN 'Attention'
                WHEN EXISTS (SELECT 1 FROM assets a WHERE a.player_id = p.id AND a.state IN ('Queued', 'Copying', 'Processing')) THEN 'Processing'
                ELSE 'Live'
            END AS status,
            p.photo_key IS NOT NULL AS has_photo,
            p.volleybox_player_id AS volleybox_id,
            p.created_at AS created_at
        FROM players p
        LEFT JOIN users u ON u.id = p.analyst_id
        """);

    /// <summary>One player row of the projection.</summary>
    private sealed record PlayerRow
    {
        /// <summary>Gets the player id.</summary>
        public Guid Id { get; init; }

        /// <summary>Gets the name, or <see langword="null"/>.</summary>
        public string? Name { get; init; }

        /// <summary>Gets the position codes, or <see langword="null"/>.</summary>
        public string[]? Positions { get; init; }

        /// <summary>Gets the country code, or <see langword="null"/>.</summary>
        public string? NationalityCode { get; init; }

        /// <summary>Gets the number of videos.</summary>
        public int VideoCount { get; init; }

        /// <summary>Gets the number of documents.</summary>
        public int DocumentCount { get; init; }

        /// <summary>Gets the number of different watchers.</summary>
        public int Viewers { get; init; }

        /// <summary>Gets when the player was last watched, or <see langword="null"/>.</summary>
        public DateTimeOffset? LastViewedAt { get; init; }

        /// <summary>Gets the analyst's name, or <see langword="null"/>.</summary>
        public string? AnalystName { get; init; }

        /// <summary>Gets the console status.</summary>
        public string Status { get; init; } = string.Empty;

        /// <summary>Gets a value indicating whether a photo is stored.</summary>
        public bool HasPhoto { get; init; }

        /// <summary>Gets the Volleybox id, or <see langword="null"/>.</summary>
        public int? VolleyboxId { get; init; }

        /// <summary>Gets when the player was created.</summary>
        public DateTimeOffset CreatedAt { get; init; }
    }
}

/// <summary>Reads the waiting list (A-5) with SQL projections, paged in SQL.</summary>
/// <param name="context">The database.</param>
public sealed class WaitingReader(DbContext context) : IWaitingReader
{
    /// <inheritdoc />
    public async Task<PagedResult<WaitingPlayerRowDto>> PageAsync(string? missing, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rows = Rows();
        rows = missing switch
        {
            "name" => rows.Where(row => row.NoName),
            "nationality" => rows.Where(row => row.NoNationality),
            "positions" => rows.Where(row => row.NoPositions),
            "volleybox" => rows.Where(row => row.NoVolleybox),
            "photo" => rows.Where(row => row.NoPhoto),
            _ => rows,
        };
        var totalCount = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(row => row.WaitingSince)
            .ThenBy(row => row.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return PagedResult<WaitingPlayerRowDto>.Create([.. page.Select(ToDto)], request, totalCount);
    }

    /// <inheritdoc />
    public async Task<WaitingSummaryDto> SummaryAsync(DateTimeOffset todayFrom, DateTimeOffset todayTo, CancellationToken cancellationToken = default)
    {
        var rows = Rows();
        return new WaitingSummaryDto
        {
            Waiting = await rows.CountAsync(cancellationToken),
            NoVolleybox = await rows.CountAsync(row => row.NoVolleybox, cancellationToken),
            NoPhoto = await rows.CountAsync(row => row.NoPhoto, cancellationToken),
            PublishedToday = await context.Set<Player>().AsNoTracking().CountAsync(player => player.PublishedAt >= todayFrom && player.PublishedAt < todayTo, cancellationToken),
        };
    }

    /// <summary>Maps a row to its DTO, listing what is missing in the drawer's order.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The DTO.</returns>
    private static WaitingPlayerRowDto ToDto(WaitingRow row)
    {
        var missing = new List<string>(5);
        if (row.NoName)
        {
            missing.Add(nameof(PlayerDetail.Name));
        }

        if (row.NoNationality)
        {
            missing.Add(nameof(PlayerDetail.Nationality));
        }

        if (row.NoPositions)
        {
            missing.Add(nameof(PlayerDetail.Positions));
        }

        if (row.NoVolleybox)
        {
            missing.Add(nameof(PlayerDetail.Volleybox));
        }

        if (row.NoPhoto)
        {
            missing.Add(nameof(PlayerDetail.Photo));
        }

        return new WaitingPlayerRowDto
        {
            Id = row.Id,
            Name = row.Name,
            FolderName = row.FolderName,
            VideoCount = row.VideoCount,
            DocumentCount = row.DocumentCount,
            Missing = missing,
            WaitingSince = row.WaitingSince,
        };
    }

    /// <summary>The players that are not published, with what they lack.</summary>
    /// <returns>The composable query.</returns>
    private IQueryable<WaitingRow> Rows() => context.Database.SqlQuery<WaitingRow>(
        $"""
        SELECT
            p.id AS id,
            p.full_name AS name,
            (SELECT f.name FROM drive_folders f WHERE f.player_id = p.id ORDER BY f.linked_at DESC LIMIT 1) AS folder_name,
            (SELECT count(*)::int FROM assets a WHERE a.player_id = p.id AND a.kind = 'Video') AS video_count,
            (SELECT count(*)::int FROM assets a WHERE a.player_id = p.id AND a.kind = 'Document') AS document_count,
            p.full_name IS NULL AS no_name,
            p.nationality IS NULL AS no_nationality,
            p.positions IS NULL AS no_positions,
            p.volleybox_player_id IS NULL AS no_volleybox,
            p.photo_key IS NULL AS no_photo,
            p.created_at AS waiting_since
        FROM players p
        WHERE p.status <> 'Published'
        """);

    /// <summary>One waiting player of the projection.</summary>
    private sealed record WaitingRow
    {
        /// <summary>Gets the player id.</summary>
        public Guid Id { get; init; }

        /// <summary>Gets the name, or <see langword="null"/>.</summary>
        public string? Name { get; init; }

        /// <summary>Gets the newest folder's name, or <see langword="null"/>.</summary>
        public string? FolderName { get; init; }

        /// <summary>Gets the number of videos.</summary>
        public int VideoCount { get; init; }

        /// <summary>Gets the number of documents.</summary>
        public int DocumentCount { get; init; }

        /// <summary>Gets a value indicating whether the name is missing.</summary>
        public bool NoName { get; init; }

        /// <summary>Gets a value indicating whether the nationality is missing.</summary>
        public bool NoNationality { get; init; }

        /// <summary>Gets a value indicating whether the positions are missing.</summary>
        public bool NoPositions { get; init; }

        /// <summary>Gets a value indicating whether the Volleybox link is missing.</summary>
        public bool NoVolleybox { get; init; }

        /// <summary>Gets a value indicating whether the photo is missing.</summary>
        public bool NoPhoto { get; init; }

        /// <summary>Gets since when the player waits.</summary>
        public DateTimeOffset WaitingSince { get; init; }
    }
}
