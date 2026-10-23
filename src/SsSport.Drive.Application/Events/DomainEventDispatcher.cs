using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Events;

/// <summary>
/// Resolves <see cref="IDomainEventHandler{TEvent}"/>s from the container by each event's concrete type.
/// </summary>
/// <param name="services">The request's service provider.</param>
internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    /// <summary>One compiled invoker per event type, so reflection runs once per type.</summary>
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> _invokers = new();

    /// <summary>Delivers each event, in order, to every handler registered for its concrete type.</summary>
    /// <param name="domainEvents">The events, in the order they were raised.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var invoke = _invokers.GetOrAdd(domainEvent.GetType(), CreateInvoker);
            await invoke(services, domainEvent, cancellationToken);
        }
    }

    /// <summary>Builds the invoker for one event type.</summary>
    /// <param name="eventType">The concrete event type.</param>
    /// <returns>A delegate that runs every handler of that type.</returns>
    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task> CreateInvoker(Type eventType) =>
        (Func<IServiceProvider, IDomainEvent, CancellationToken, Task>)typeof(DomainEventDispatcher)
            .GetMethod(nameof(InvokeHandlersAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .CreateDelegate(typeof(Func<IServiceProvider, IDomainEvent, CancellationToken, Task>));

    /// <summary>Runs every registered handler for one event, in registration order.</summary>
    /// <typeparam name="TEvent">The concrete event type.</typeparam>
    /// <param name="services">The service provider.</param>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    private static async Task InvokeHandlersAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }
}
