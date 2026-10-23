using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Events;

/// <summary>Raised in these tests when a team is renamed.</summary>
/// <param name="TeamId">The renamed team.</param>
public sealed record TeamRenamed(Guid TeamId) : IDomainEvent;

/// <summary>Raised in these tests for an event nobody listens to.</summary>
public sealed record NobodyCares : IDomainEvent;

/// <summary>Records the events it receives, tagged with its name, into a shared log.</summary>
/// <param name="name">The handler name written to the log.</param>
/// <param name="log">The shared log.</param>
public sealed class RecordingHandler(string name, List<string> log) : IDomainEventHandler<TeamRenamed>
{
    /// <summary>Appends the handler name and team id to the log.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task HandleAsync(TeamRenamed domainEvent, CancellationToken cancellationToken)
    {
        log.Add($"{name}:{domainEvent.TeamId}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Domain events are delivered to every handler registered for their concrete type, in raise order.
/// </summary>
public sealed class DomainEventDispatcherTests
{
    /// <summary>Each event reaches every handler of its type, events in the order they were raised.</summary>
    [Fact]
    public async Task Delivers_each_event_to_all_of_its_handlers_in_order()
    {
        var log = new List<string>();
        await using var services = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<TeamRenamed>>(new RecordingHandler("first", log))
            .AddSingleton<IDomainEventHandler<TeamRenamed>>(new RecordingHandler("second", log))
            .AddApplication()
            .BuildServiceProvider();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await services.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new TeamRenamed(a), new TeamRenamed(b)], TestContext.Current.CancellationToken);

        log.Should().Equal($"first:{a}", $"second:{a}", $"first:{b}", $"second:{b}");
    }

    /// <summary>An event without handlers is simply ignored.</summary>
    [Fact]
    public async Task Events_without_handlers_are_ignored()
    {
        await using var services = new ServiceCollection().AddApplication().BuildServiceProvider();

        var dispatch = () => services.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new NobodyCares()], TestContext.Current.CancellationToken);

        await dispatch.Should().NotThrowAsync();
    }
}
