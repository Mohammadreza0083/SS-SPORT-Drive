using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Events;

/// <summary>Delivers a queued event to its background handlers (T-6c); the background job runs it in its own scope.</summary>
/// <param name="services">The job's service provider.</param>
internal sealed class BackgroundEventRunner(IServiceProvider services) : IBackgroundEventRunner
{
    /// <summary>The compiled invoker per event type.</summary>
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, EventContext, CancellationToken, Task>> _invokers = new();

    /// <summary>Delivers the event to every background handler of its type.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <returns>A task that completes when every handler ran.</returns>
    public Task RunAsync(IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(context);
        return _invokers.GetOrAdd(domainEvent.GetType(), CreateInvoker)(services, domainEvent, context, cancellationToken);
    }

    /// <summary>Builds the invoker for one event type.</summary>
    /// <param name="eventType">The concrete event type.</param>
    /// <returns>The invoker.</returns>
    private static Func<IServiceProvider, IDomainEvent, EventContext, CancellationToken, Task> CreateInvoker(Type eventType) =>
        (Func<IServiceProvider, IDomainEvent, EventContext, CancellationToken, Task>)typeof(BackgroundEventRunner)
            .GetMethod(nameof(InvokeHandlersAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .CreateDelegate(typeof(Func<IServiceProvider, IDomainEvent, EventContext, CancellationToken, Task>));

    /// <summary>Runs every background handler of one event type.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="services">The job's service provider.</param>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <returns>A task that completes when every handler ran.</returns>
    private static async Task InvokeHandlersAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IBackgroundEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, context, cancellationToken);
        }
    }
}
