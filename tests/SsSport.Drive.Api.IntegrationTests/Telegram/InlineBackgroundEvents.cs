using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// Runs bot notices at once, in their own scope, instead of queueing them in Hangfire (T-6c). Test classes that check
/// what a notice says right after a request use it; <see cref="BotNoticeQueueTests"/> covers the real queue.
/// </summary>
/// <param name="scopes">Creates the scope each notice runs in, as a Hangfire job would.</param>
internal sealed class InlineBackgroundEvents(IServiceScopeFactory scopes) : IBackgroundEventQueue
{
    /// <summary>Runs the background handlers of the event now.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    /// <returns>A task that completes when every handler ran.</returns>
    public async Task EnqueueAsync(IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBackgroundEventRunner>().RunAsync(domainEvent, context, cancellationToken);
    }
}

/// <summary>Registers <see cref="InlineBackgroundEvents"/> in a test host.</summary>
internal static class InlineBackgroundEventsExtensions
{
    /// <summary>Makes bot notices run at once, so a test can check them right after the request.</summary>
    /// <param name="services">The test host's services.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection UseInlineBotNotices(this IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IBackgroundEventQueue, InlineBackgroundEvents>());
}
