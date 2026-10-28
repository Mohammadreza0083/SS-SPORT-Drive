using System.Linq.Expressions;
using Ardalis.Specification;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Read side of every list (decisions §2b): reads one page in the database, untracked, and maps it to DTOs.
/// A list is never loaded whole. The id always breaks ties, so rows never repeat or vanish between pages.
/// </summary>
public interface IPagedReader
{
    /// <summary>Reads one offset page and the total count.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TItem">The item DTO.</typeparam>
    /// <param name="specification">Criteria, includes and the order; it must order and must not page.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="map">Maps an entity to its DTO.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidOperationException">The specification has no order, or pages itself.</exception>
    Task<PagedResult<TItem>> PageAsync<TEntity, TItem>(
        ISpecification<TEntity> specification,
        PageRequest request,
        Func<TEntity, TItem> map,
        CancellationToken cancellationToken = default)
        where TEntity : Entity<Guid>;

    /// <summary>Reads one cursor page, ordered by a key and then the id.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The sort key type (e.g. a timestamp).</typeparam>
    /// <typeparam name="TItem">The item DTO.</typeparam>
    /// <param name="specification">Criteria and includes; it must neither order nor page.</param>
    /// <param name="key">The sort key.</param>
    /// <param name="direction">The order to read in.</param>
    /// <param name="request">Where to continue from.</param>
    /// <param name="map">Maps an entity to its DTO.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page and the cursor for the next one.</returns>
    /// <exception cref="InvalidOperationException">The specification orders or pages itself.</exception>
    /// <exception cref="Exceptions.RequestValidationException">The cursor was made for a list with another key type.</exception>
    Task<CursorResult<TItem>> CursorPageAsync<TEntity, TKey, TItem>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TKey>> key,
        CursorDirection direction,
        CursorRequest request,
        Func<TEntity, TItem> map,
        CancellationToken cancellationToken = default)
        where TEntity : Entity<Guid>;
}
