using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IRepository{TAggregate, TId}"/> on top of the request's context.</summary>
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

    /// <summary>Stages a new aggregate to be inserted on the next save.</summary>
    /// <param name="aggregate">The aggregate to add.</param>
    public void Add(TAggregate aggregate) => context.Set<TAggregate>().Add(aggregate);

    /// <summary>Stages an aggregate to be deleted on the next save.</summary>
    /// <param name="aggregate">The aggregate to remove.</param>
    public void Remove(TAggregate aggregate) => context.Set<TAggregate>().Remove(aggregate);
}
