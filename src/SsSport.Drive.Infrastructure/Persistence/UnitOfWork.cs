using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>: one <c>SaveChanges</c> on the request's context,
/// which EF Core runs in a single transaction. After a successful save, the domain events raised by the
/// tracked aggregates are cleared and dispatched; a failed save dispatches nothing.
/// </summary>
/// <param name="context">The request's EF Core context, shared with the repositories.</param>
/// <param name="domainEvents">Delivers saved domain events to their handlers.</param>
public sealed class UnitOfWork(DbContext context, IDomainEventDispatcher domainEvents) : IUnitOfWork
{
    /// <summary>Writes all staged changes, then dispatches the domain events they raised.</summary>
    /// <param name="cancellationToken">Cancels the save and the dispatch.</param>
    /// <returns>The number of rows written.</returns>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = context.ChangeTracker.Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        var written = await context.SaveChangesAsync(cancellationToken);

        var raised = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());
        await domainEvents.DispatchAsync(raised, cancellationToken);

        return written;
    }
}
