using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Public;
using SsSport.Drive.Domain.AccessRequests;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Read model of the admin access-request inbox (P-4). A row needs its player's name, the deciding admin's name and the
/// link an approval made, which live in other aggregates, so the page is read with a join in SQL; the status filter,
/// the order and the page all run in the database.
/// </summary>
public interface IAccessRequestListReader
{
    /// <summary>Reads one page of requests with a status, newest first (the id breaks ties).</summary>
    /// <param name="status">The status to list.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page and the total.</returns>
    Task<PagedResult<AccessRequestDto>> PageAsync(AccessRequestStatus status, PageRequest request, CancellationToken cancellationToken = default);
}
