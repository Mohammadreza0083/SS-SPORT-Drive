using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Events;

/// <summary>
/// Resolves <see cref="IDomainEventHandler{TEvent}"/>s from the container by each event's concrete type and runs them in
/// the request. An event that also has <see cref="IBackgroundEventHandler{TEvent}"/>s (bot notices, T-6c) is queued
/// once, with the caller as the actor, so the request never waits for them.
/// </summary>
/// <param name="services">The request's service provider.</param>
internal sealed partial class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    /// <summary>The compiled inline invoker per event type.</summary>
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> _invokers = new();

    /// <summary>Delivers each event to its inline handlers, then queues it when it has background handlers.</summary>
    /// <param name="domainEvents">The events, in raise order.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <returns>A task that completes when every inline handler ran and every background event is queued.</returns>
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        var known = services.GetService<IServiceProviderIsService>();
        EventContext? context = null;
        foreach (var domainEvent in domainEvents)
        {
            var invoke = _invokers.GetOrAdd(domainEvent.GetType(), CreateInvoker);
            await invoke(services, domainEvent, cancellationToken);

            var background = typeof(IBackgroundEventHandler<>).MakeGenericType(domainEvent.GetType());
            if (known?.IsService(background) == true)
            {
                context ??= new EventContext(Guid.TryParse(services.GetService<ICurrentUser>()?.UserId, out var actor) ? actor : null);
                await QueueAsync(domainEvent, context, cancellationToken);
            }
        }
    }

    /// <summary>Builds the inline invoker for one event type.</summary>
    /// <param name="eventType">The concrete event type.</param>
    /// <returns>The invoker.</returns>
    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task> CreateInvoker(Type eventType) =>
        (Func<IServiceProvider, IDomainEvent, CancellationToken, Task>)typeof(DomainEventDispatcher)
            .GetMethod(nameof(InvokeHandlersAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .CreateDelegate(typeof(Func<IServiceProvider, IDomainEvent, CancellationToken, Task>));

    /// <summary>Runs every inline handler of one event type.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="services">The request's service provider.</param>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <returns>A task that completes when every handler ran.</returns>
    private static async Task InvokeHandlersAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }

    /// <summary>
    /// Queues the event for its background handlers. The change is already saved, so a queue that cannot be reached is
    /// logged as a Warning and never fails the request.
    /// </summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the enqueueing.</param>
    /// <returns>A task that completes when the event is queued, or the failure is logged.</returns>
    private async Task QueueAsync(IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        try
        {
            await services.GetRequiredService<IBackgroundEventQueue>().EnqueueAsync(domainEvent, context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Hosts register logging; a bare service collection (unit tests) logs nowhere.
            var logger = services.GetService<ILogger<DomainEventDispatcher>>() ?? NullLogger<DomainEventDispatcher>.Instance;
            LogQueueFailed(logger, exception, domainEvent.GetType().Name);
        }
    }

    /// <summary>Logs an event that could not be queued for its background handlers.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="EventType">The event type name.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Domain event {EventType} could not be queued for its background handlers")]
    private static partial void LogQueueFailed(ILogger logger, Exception exception, string EventType);
}
