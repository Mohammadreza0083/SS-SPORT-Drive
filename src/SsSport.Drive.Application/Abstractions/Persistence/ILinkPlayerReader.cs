using SsSport.Drive.Application.Links;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>Reads what a viewer page shows about a link's players in one read-only projection (V-2b).</summary>
public interface ILinkPlayerReader
{
    /// <summary>Reads the facts of the given published players. Players that are missing or no longer published are left out.</summary>
    /// <param name="playerIds">The players to read; at most one page of a link.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The facts, in no particular order.</returns>
    Task<IReadOnlyList<LinkPlayerFacts>> ReadAsync(IReadOnlyCollection<Guid> playerIds, CancellationToken cancellationToken = default);
}
