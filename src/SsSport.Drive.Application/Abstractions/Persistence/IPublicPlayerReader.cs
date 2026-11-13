using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Public;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Reads what the public site shows about published players in read-only, parameterized SQL projections (P-8): one
/// page of the search, and one player by slug. Like <see cref="IPlayerListReader"/> this is a recorded exception to
/// "conditions in specifications", because each row spans players and their assets with no navigations.
/// </summary>
public interface IPublicPlayerReader
{
    /// <summary>Reads one page of published players, sorted by name, and the total.</summary>
    /// <param name="search">Part of the name, or <see langword="null"/> for everyone; wildcards match themselves.</param>
    /// <param name="position">A primary position code (S, OH, OPP, MB, L, DS), or <see langword="null"/> for all.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<PublicPlayerFacts>> PageAsync(string? search, string? position, PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads one published player.</summary>
    /// <param name="slug">The player's slug.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The facts, or <see langword="null"/> when there is no such published player.</returns>
    Task<PublicPlayerFacts?> FindAsync(string slug, CancellationToken cancellationToken = default);
}
