using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram.Progress;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IPlayerBuildReader"/>: one SQL projection per player, with the rendition counts
/// as correlated subqueries. Nothing is tracked.
/// </summary>
/// <param name="context">The request's EF Core context.</param>
public sealed class PlayerBuildReader(DbContext context) : IPlayerBuildReader
{
    /// <summary>Reads the pipeline state of a player's files, leaving out files removed from Drive.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One row per file.</returns>
    public async Task<IReadOnlyList<AssetBuildRow>> ReadAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        await context.Set<Asset>()
            .AsNoTracking()
            .Where(asset => asset.PlayerId == playerId && asset.RemovedFromSourceAt == null)
            .Select(asset => new AssetBuildRow(
                asset.File.Kind,
                asset.State,
                asset.Renditions.Count,
                asset.Renditions.Count(rendition => rendition.Status == RenditionStatus.Ready)))
            .ToListAsync(cancellationToken);
}
