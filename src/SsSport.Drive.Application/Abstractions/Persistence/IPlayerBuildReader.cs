using SsSport.Drive.Application.Telegram.Progress;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Read model of a player's build (T-6b): where each of its files is in the pipeline. The rows are an
/// <c>AsNoTracking</c> projection of one player's files (never entities) and are only counted, never sent to a user, so
/// no paging applies.
/// </summary>
public interface IPlayerBuildReader
{
    /// <summary>Reads the pipeline state of a player's files, leaving out files removed from Drive.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One row per file.</returns>
    Task<IReadOnlyList<AssetBuildRow>> ReadAsync(Guid playerId, CancellationToken cancellationToken = default);
}
