using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Public;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads the public site's player facts (P-8) in parameterized, read-only SQL: published players only, searched by name
/// (case-insensitive, wildcards escaped), filtered by primary position, sorted by name, paged with <c>OFFSET</c> and
/// <c>LIMIT</c>. Counts are of ready, visible videos and documents.
/// </summary>
/// <param name="context">The database context.</param>
public sealed class PublicPlayerReader(DbContext context) : IPublicPlayerReader
{
    /// <summary>The escape character for searched text, so <c>%</c> and <c>_</c> match themselves.</summary>
    private const string LikeEscape = "\\";

    /// <inheritdoc />
    public async Task<PagedResult<PublicPlayerFacts>> PageAsync(string? search, string? position, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pattern = string.IsNullOrWhiteSpace(search) ? "%" : $"%{EscapeLike(search.Trim())}%";
        var primary = position ?? string.Empty;

        var total = await context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM players p
            WHERE p.status = 'Published'
                AND p.full_name ILIKE {pattern} ESCAPE '\'
                AND ({primary} = '' OR p.positions[1] = {primary})
            """).SingleAsync(cancellationToken);

        var skip = request.Skip;
        var take = request.PageSize;
        var rows = await context.Database.SqlQuery<FactsRow>(
            $"""
            SELECT
                p.id AS id,
                p.slug AS slug,
                p.full_name AS name,
                p.nationality AS nationality_code,
                p.positions AS positions,
                p.photo_key IS NOT NULL AS has_photo,
                p.volleybox_url AS volleybox_url,
                (SELECT count(*)::int FROM assets a
                    WHERE a.player_id = p.id AND a.kind = 'Video' AND a.visibility = 'Visible' AND a.state = 'Ready') AS video_count,
                (SELECT coalesce(sum(a.duration_seconds), 0)::float8 FROM assets a
                    WHERE a.player_id = p.id AND a.kind = 'Video' AND a.visibility = 'Visible' AND a.state = 'Ready') AS video_seconds,
                (SELECT count(*)::int FROM assets a
                    WHERE a.player_id = p.id AND a.kind = 'Document' AND a.visibility = 'Visible' AND a.state = 'Ready') AS document_count
            FROM players p
            WHERE p.status = 'Published'
                AND p.full_name ILIKE {pattern} ESCAPE '\'
                AND ({primary} = '' OR p.positions[1] = {primary})
            ORDER BY p.full_name, p.id
            OFFSET {skip} LIMIT {take}
            """).ToListAsync(cancellationToken);

        return PagedResult<PublicPlayerFacts>.Create([.. rows.Select(ToFacts)], request, total);
    }

    /// <inheritdoc />
    public async Task<PublicPlayerFacts?> FindAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        var rows = await context.Database.SqlQuery<FactsRow>(
            $"""
            SELECT
                p.id AS id,
                p.slug AS slug,
                p.full_name AS name,
                p.nationality AS nationality_code,
                p.positions AS positions,
                p.photo_key IS NOT NULL AS has_photo,
                p.volleybox_url AS volleybox_url,
                (SELECT count(*)::int FROM assets a
                    WHERE a.player_id = p.id AND a.kind = 'Video' AND a.visibility = 'Visible' AND a.state = 'Ready') AS video_count,
                (SELECT coalesce(sum(a.duration_seconds), 0)::float8 FROM assets a
                    WHERE a.player_id = p.id AND a.kind = 'Video' AND a.visibility = 'Visible' AND a.state = 'Ready') AS video_seconds,
                (SELECT count(*)::int FROM assets a
                    WHERE a.player_id = p.id AND a.kind = 'Document' AND a.visibility = 'Visible' AND a.state = 'Ready') AS document_count
            FROM players p
            WHERE p.status = 'Published' AND p.slug = {slug}
            """).ToListAsync(cancellationToken);

        return rows.Select(ToFacts).SingleOrDefault();
    }

    /// <summary>Escapes the LIKE wildcards and the escape character itself.</summary>
    /// <param name="text">The searched text.</param>
    /// <returns>The text matched literally.</returns>
    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    /// <summary>Turns a row into the facts.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The facts.</returns>
    private static PublicPlayerFacts ToFacts(FactsRow row) => new(
        row.Id,
        row.Slug ?? string.Empty,
        row.Name ?? string.Empty,
        row.NationalityCode ?? string.Empty,
        row.Positions ?? [],
        row.HasPhoto,
        row.VolleyboxUrl ?? string.Empty,
        row.VideoCount,
        row.VideoSeconds,
        row.DocumentCount);

    /// <summary>One row of the projection.</summary>
    private sealed record FactsRow
    {
        /// <summary>Gets the player id.</summary>
        public Guid Id { get; init; }

        /// <summary>Gets the slug.</summary>
        public string? Slug { get; init; }

        /// <summary>Gets the full name.</summary>
        public string? Name { get; init; }

        /// <summary>Gets the nationality code.</summary>
        public string? NationalityCode { get; init; }

        /// <summary>Gets the position codes, primary first.</summary>
        public string[]? Positions { get; init; }

        /// <summary>Gets a value indicating whether a photo is stored.</summary>
        public bool HasPhoto { get; init; }

        /// <summary>Gets the Volleybox profile.</summary>
        public string? VolleyboxUrl { get; init; }

        /// <summary>Gets the number of ready, visible videos.</summary>
        public int VideoCount { get; init; }

        /// <summary>Gets their total length in seconds.</summary>
        public double VideoSeconds { get; init; }

        /// <summary>Gets the number of ready, visible documents.</summary>
        public int DocumentCount { get; init; }
    }
}
