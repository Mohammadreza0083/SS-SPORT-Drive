using Ardalis.Specification;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Write-side access to one kind of aggregate. A repository only stages changes;
/// <see cref="IUnitOfWork.SaveChangesAsync"/> writes them. Read-side queries do not use repositories.
/// Query conditions are always expressed as specifications (<see cref="ISpecification{T}"/>).
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate identifier type.</typeparam>
public interface IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    /// <summary>Loads an aggregate by its identifier, tracked for changes.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The aggregate, or <see langword="null"/> when it does not exist.</returns>
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>Loads the first aggregate matching a specification, in its order, tracked for changes.</summary>
    /// <param name="specification">The criteria, ordering and includes.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The first match, or <see langword="null"/> when nothing matches.</returns>
    Task<TAggregate?> FirstOrDefaultAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);

    /// <summary>Loads every aggregate matching a specification, with its ordering and paging, tracked for changes.</summary>
    /// <param name="specification">The criteria, ordering, paging and includes.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matches, possibly empty.</returns>
    Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);

    /// <summary>Counts the aggregates matching a specification's criteria; its paging is ignored.</summary>
    /// <param name="specification">The criteria.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of matches.</returns>
    Task<int> CountAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);

    /// <summary>Checks whether any aggregate matches a specification's criteria; its paging is ignored.</summary>
    /// <param name="specification">The criteria.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="true"/> when at least one aggregate matches.</returns>
    Task<bool> AnyAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);

    /// <summary>Stages a new aggregate to be inserted on the next save.</summary>
    /// <param name="aggregate">The aggregate to add.</param>
    void Add(TAggregate aggregate);

    /// <summary>Stages an aggregate to be deleted on the next save.</summary>
    /// <param name="aggregate">The aggregate to remove.</param>
    void Remove(TAggregate aggregate);

    /// <summary>
    /// Deletes the rows a specification selects straight in SQL, at once and without loading them, and returns how many
    /// went. Its criteria and <c>Take</c> apply, so large deletes run in batches. Domain events and the unit of work are
    /// bypassed: use it only for rows with no events or invariants left, such as expired ones.
    /// </summary>
    /// <param name="specification">The rows to delete.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many rows were deleted.</returns>
    Task<int> DeleteAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default);
}
