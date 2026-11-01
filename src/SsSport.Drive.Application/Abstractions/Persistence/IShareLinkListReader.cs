using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Read model of the share links list (L-1). A row needs its recipient's name and its players' names, which live in other
/// aggregates, so the page is read with a join in SQL; the criteria, the order and the page all run in the database.
/// </summary>
public interface IShareLinkListReader
{
    /// <summary>Reads one page of links, newest first (the id breaks ties).</summary>
    /// <param name="criteria">The status, player and recipient filters.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="now">The moment each link's access is judged at.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page and the total.</returns>
    Task<PagedResult<ShareLinkListItemDto>> PageAsync(ShareLinkListCriteria criteria, PageRequest request, DateTimeOffset now, CancellationToken cancellationToken = default);
}
