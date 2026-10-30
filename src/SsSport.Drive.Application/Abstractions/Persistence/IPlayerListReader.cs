using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Read model of the player list (T-7, owner-approved 2026-10-30). A player row needs the state of its files and folders,
/// which are other aggregates, so one SQL query reads the page with correlated counts instead of a specification.
/// The criteria are the typed <see cref="PlayerListCriteria"/>; paging and the total run in the database.
/// </summary>
public interface IPlayerListReader
{
    /// <summary>Reads one page of players, newest first (the id breaks ties).</summary>
    /// <param name="criteria">Whose players, the search and the filter chip.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page and the total.</returns>
    Task<PagedResult<PlayerListItemDto>> PageAsync(PlayerListCriteria criteria, PageRequest request, CancellationToken cancellationToken = default);
}
