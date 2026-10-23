using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Events;

/// <summary>
/// Reacts to a domain event after the change that raised it has been saved
/// (for example: notify the analyst on Telegram when a player is published).
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the event.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the event is handled.</returns>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Delivers saved domain events to their handlers. Called by the unit of work after a successful save.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Delivers each event, in order, to every handler registered for its concrete type.</summary>
    /// <param name="domainEvents">The events, in the order they were raised.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
