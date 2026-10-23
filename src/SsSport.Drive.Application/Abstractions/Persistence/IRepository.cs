using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Write-side access to one kind of aggregate. A repository only stages changes;
/// <see cref="IUnitOfWork.SaveChangesAsync"/> writes them. Read-side queries do not use repositories.
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

    /// <summary>Stages a new aggregate to be inserted on the next save.</summary>
    /// <param name="aggregate">The aggregate to add.</param>
    void Add(TAggregate aggregate);

    /// <summary>Stages an aggregate to be deleted on the next save.</summary>
    /// <param name="aggregate">The aggregate to remove.</param>
    void Remove(TAggregate aggregate);
}
