using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads the facts a viewer page needs about a link's players (V-2b) in one parameterized, read-only SQL projection:
/// the card, the profile, whether a photo exists, and the counts and total length of ready, visible videos and documents.
/// </summary>
/// <param name="context">The database context.</param>
public sealed class LinkPlayerReader(DbContext context) : ILinkPlayerReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkPlayerFacts>> ReadAsync(IReadOnlyCollection<Guid> playerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        if (playerIds.Count == 0)
        {
            return [];
        }

        var ids = playerIds.Distinct().ToArray();
        var rows = await context.Database.SqlQuery<FactsRow>(
            $"""
            SELECT
                p.id AS id,
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
            WHERE p.id = ANY({ids}) AND p.status = 'Published'
            """)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new LinkPlayerFacts(
            row.Id,
            row.Name ?? string.Empty,
            row.NationalityCode ?? string.Empty,
            row.Positions ?? [],
            row.HasPhoto,
            row.VolleyboxUrl ?? string.Empty,
            row.VideoCount,
            row.VideoSeconds,
            row.DocumentCount))];
    }

    /// <summary>One row of the projection.</summary>
    private sealed record FactsRow
    {
        /// <summary>Gets the player id.</summary>
        public Guid Id { get; init; }

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
