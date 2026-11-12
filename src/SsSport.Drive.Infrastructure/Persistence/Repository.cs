using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TAggregate, TId}"/> on top of the request's context.
/// Specifications are translated to SQL by the Ardalis EF Core evaluator.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate identifier type.</typeparam>
/// <param name="context">The request's EF Core context, shared with the unit of work.</param>
public sealed class Repository<TAggregate, TId>(DbContext context) : IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    /// <summary>Loads an aggregate by its primary key, tracked for changes.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The aggregate, or <see langword="null"/> when it does not exist.</returns>
    public async Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        await context.Set<TAggregate>().FindAsync([id], cancellationToken);

    /// <summary>Loads the first aggregate matching a specification, in its order, tracked for changes.</summary>
    /// <param name="specification">The criteria, ordering and includes.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The first match, or <see langword="null"/> when nothing matches.</returns>
    public Task<TAggregate?> FirstOrDefaultAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default) =>
        Apply(specification).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Loads every aggregate matching a specification, with its ordering and paging, tracked for changes.</summary>
    /// <param name="specification">The criteria, ordering, paging and includes.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matches, possibly empty.</returns>
    public async Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default) =>
        await Apply(specification).ToListAsync(cancellationToken);

    /// <summary>Counts the aggregates matching a specification's criteria; its paging is ignored.</summary>
    /// <param name="specification">The criteria.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of matches.</returns>
    public Task<int> CountAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default) =>
        Apply(specification, criteriaOnly: true).CountAsync(cancellationToken);

    /// <summary>Checks whether any aggregate matches a specification's criteria; its paging is ignored.</summary>
    /// <param name="specification">The criteria.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="true"/> when at least one aggregate matches.</returns>
    public Task<bool> AnyAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default) =>
        Apply(specification, criteriaOnly: true).AnyAsync(cancellationToken);

    /// <summary>Stages a new aggregate to be inserted on the next save.</summary>
    /// <param name="aggregate">The aggregate to add.</param>
    public void Add(TAggregate aggregate) => context.Set<TAggregate>().Add(aggregate);

    /// <summary>Stages an aggregate to be deleted on the next save.</summary>
    /// <param name="aggregate">The aggregate to remove.</param>
    public void Remove(TAggregate aggregate) => context.Set<TAggregate>().Remove(aggregate);

    /// <summary>Deletes the rows a specification selects with one SQL <c>DELETE</c>, without loading them.</summary>
    /// <param name="specification">The rows to delete; its criteria and <c>Take</c> apply.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many rows were deleted.</returns>
    public Task<int> DeleteAsync(ISpecification<TAggregate> specification, CancellationToken cancellationToken = default) =>
        Apply(specification).ExecuteDeleteAsync(cancellationToken);

    /// <summary>Builds the query for a specification.</summary>
    /// <param name="specification">The specification to evaluate.</param>
    /// <param name="criteriaOnly">When <see langword="true"/>, only the criteria are applied (no ordering, paging or includes).</param>
    /// <returns>The query, not yet executed.</returns>
    private IQueryable<TAggregate> Apply(ISpecification<TAggregate> specification, bool criteriaOnly = false)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return SpecificationEvaluator.Default.GetQuery(context.Set<TAggregate>(), specification, criteriaOnly);
    }
}
