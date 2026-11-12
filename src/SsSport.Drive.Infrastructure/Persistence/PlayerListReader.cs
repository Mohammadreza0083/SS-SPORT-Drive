using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IPlayerListReader"/>. A fixed SQL projection gives every player row its file
/// and folder counts as correlated subqueries; the criteria, the order and the page are composed on it in LINQ, so
/// PostgreSQL filters, counts and pages in one round trip each. Nothing is tracked.
/// </summary>
/// <param name="context">The request's EF Core context.</param>
public sealed class PlayerListReader(DbContext context) : IPlayerListReader
{
    /// <summary>The escape character for searched text, so <c>%</c> and <c>_</c> match themselves.</summary>
    private const string LikeEscape = "\\";

    /// <summary>Reads one page of players, newest first (the id breaks ties), and the total.</summary>
    /// <param name="criteria">Whose players, the search and the filter chip.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<PagedResult<PlayerListItemDto>> PageAsync(PlayerListCriteria criteria, PageRequest request, CancellationToken cancellationToken = default)
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

        return PagedResult<PlayerListItemDto>.Create([.. page.Select(ToDto)], request, totalCount);
    }

    /// <summary>Applies the analyst, the search and the filter chip.</summary>
    /// <param name="rows">Every player row.</param>
    /// <param name="criteria">The criteria.</param>
    /// <returns>The matching rows, not yet ordered or paged.</returns>
    private static IQueryable<PlayerRow> Filter(IQueryable<PlayerRow> rows, PlayerListCriteria criteria)
    {
        if (criteria.PlayerId is { } playerId)
        {
            rows = rows.Where(row => row.Id == playerId);
        }

        if (criteria.AnalystId is { } analystId)
        {
            rows = rows.Where(row => row.AnalystId == analystId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            var pattern = $"%{EscapeLike(search)}%";
            rows = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var volleyboxId)
                ? rows.Where(row => row.VolleyboxId == volleyboxId || EF.Functions.ILike(row.Name!, pattern, LikeEscape))
                : rows.Where(row => EF.Functions.ILike(row.Name!, pattern, LikeEscape));
        }

        return criteria.Filter switch
        {
            PlayerListFilter.Live => rows.Where(row => row.Status == nameof(PlayerStatus.Published)),
            PlayerListFilter.Processing => rows.Where(row => row.ProcessingCount > 0),
            PlayerListFilter.NeedsAttention => rows.Where(row => !row.IsComplete || row.FailedCount > 0 || row.HasLostFolder),
            _ => rows,
        };
    }

    /// <summary>Escapes the LIKE wildcards and the escape character itself.</summary>
    /// <param name="text">The searched text.</param>
    /// <returns>The text matched literally.</returns>
    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    /// <summary>Turns a row into its DTO.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The DTO.</returns>
    private static PlayerListItemDto ToDto(PlayerRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Category = row.Category,
        NationalityCode = row.NationalityCode,
        NationalityName = row.NationalityCode is null ? null : Nationality.Create(row.NationalityCode).CountryName,
        Positions = row.Positions ?? [],
        HasPhoto = row.HasPhoto,
        Status = row.Status,
        IsComplete = row.IsComplete,
        FileCount = row.FileCount,
        ProcessingCount = row.ProcessingCount,
        FailedCount = row.FailedCount,
        HasLostFolder = row.HasLostFolder,
        CreatedAt = row.CreatedAt,
    };

    /// <summary>
    /// Every player with its counts. "Complete" mirrors <see cref="Player.MissingDetails"/> (name, nationality,
    /// positions, Volleybox and photo); the reader tests pin the two together.
    /// </summary>
    /// <returns>The composable query.</returns>
    private IQueryable<PlayerRow> Rows() => context.Database.SqlQuery<PlayerRow>(
        $"""
        SELECT
            p.id AS id,
            p.full_name AS name,
            p.category AS category,
            p.nationality AS nationality_code,
            p.positions AS positions,
            p.photo_key IS NOT NULL AS has_photo,
            p.status AS status,
            p.analyst_id AS analyst_id,
            p.volleybox_player_id AS volleybox_id,
            p.created_at AS created_at,
            (p.full_name IS NOT NULL AND p.nationality IS NOT NULL AND p.positions IS NOT NULL
                AND p.volleybox_player_id IS NOT NULL AND p.photo_key IS NOT NULL) AS is_complete,
            (SELECT count(*)::int FROM assets a WHERE a.player_id = p.id) AS file_count,
            (SELECT count(*)::int FROM assets a WHERE a.player_id = p.id AND a.state IN ('Queued', 'Copying', 'Processing')) AS processing_count,
            (SELECT count(*)::int FROM assets a WHERE a.player_id = p.id AND a.state = 'Failed') AS failed_count,
            EXISTS (SELECT 1 FROM drive_folders f WHERE f.player_id = p.id AND f.access = 'Lost') AS has_lost_folder
        FROM players p
        """);

    /// <summary>One player row of the projection.</summary>
    private sealed record PlayerRow
    {
        /// <summary>Gets the player id.</summary>
        public Guid Id { get; init; }

        /// <summary>Gets the full name, or <see langword="null"/>.</summary>
        public string? Name { get; init; }

        /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
        public string Category { get; init; } = string.Empty;

        /// <summary>Gets the country code, or <see langword="null"/>.</summary>
        public string? NationalityCode { get; init; }

        /// <summary>Gets the position codes, or <see langword="null"/>.</summary>
        public string[]? Positions { get; init; }

        /// <summary>Gets a value indicating whether a photo is stored.</summary>
        public bool HasPhoto { get; init; }

        /// <summary>Gets <c>Waiting</c> or <c>Published</c>.</summary>
        public string Status { get; init; } = string.Empty;

        /// <summary>Gets the owning analyst, or <see langword="null"/>.</summary>
        public Guid? AnalystId { get; init; }

        /// <summary>Gets the Volleybox player id, or <see langword="null"/>.</summary>
        public int? VolleyboxId { get; init; }

        /// <summary>Gets when the player was created.</summary>
        public DateTimeOffset CreatedAt { get; init; }

        /// <summary>Gets a value indicating whether every detail is set.</summary>
        public bool IsComplete { get; init; }

        /// <summary>Gets the number of files.</summary>
        public int FileCount { get; init; }

        /// <summary>Gets the number of files in progress.</summary>
        public int ProcessingCount { get; init; }

        /// <summary>Gets the number of failed files.</summary>
        public int FailedCount { get; init; }

        /// <summary>Gets a value indicating whether a folder lost access.</summary>
        public bool HasLostFolder { get; init; }
    }
}
