using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Events;

/// <summary>Who caused an event that is handled in the background, since no request is around by then.</summary>
/// <param name="ActorUserId">The team member whose request raised the event, or <see langword="null"/> for the system.</param>
public sealed record EventContext(Guid? ActorUserId);

/// <summary>
/// Handles a domain event after the request, in a background job (T-6c): bot notices, which must never make the caller
/// wait for Telegram. Runs in its own scope, after the change that raised the event is saved.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public interface IBackgroundEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the event.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the event is handled.</returns>
    Task HandleAsync(TEvent domainEvent, EventContext context, CancellationToken cancellationToken);
}

/// <summary>Queues an event for its background handlers; Infrastructure implements it with Hangfire.</summary>
public interface IBackgroundEventQueue
{
    /// <summary>Queues the event; it is handled after the request, at least once.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the enqueueing.</param>
    /// <returns>A task that completes when the event is queued.</returns>
    Task EnqueueAsync(IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken);
}

/// <summary>Runs the background handlers of a queued event; the background job calls it.</summary>
public interface IBackgroundEventRunner
{
    /// <summary>Delivers the event to every background handler of its type.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <returns>A task that completes when every handler ran.</returns>
    Task RunAsync(IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken);
}
