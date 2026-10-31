using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application.Abstractions.Authentication;
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

/// <summary>A bot notice in these tests: it must run in the background, after the request.</summary>
/// <param name="log">The shared log.</param>
public sealed class RecordingNotice(List<string> log) : IBackgroundEventHandler<TeamRenamed>
{
    /// <summary>Appends the team id and the actor to the log.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task HandleAsync(TeamRenamed domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        log.Add($"notice:{domainEvent.TeamId}:{context.ActorUserId}");
        return Task.CompletedTask;
    }
}

/// <summary>Records what is queued instead of running it.</summary>
public sealed class RecordingQueue : IBackgroundEventQueue
{
    /// <summary>Gets the queued events with their context.</summary>
    public List<(IDomainEvent Event, EventContext Context)> Queued { get; } = [];

    /// <summary>Records the event.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task EnqueueAsync(IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        Queued.Add((domainEvent, context));
        return Task.CompletedTask;
    }
}

/// <summary>A signed-in caller with a fixed id.</summary>
/// <param name="userId">The caller's user id.</param>
public sealed class FixedCurrentUser(Guid userId) : ICurrentUser
{
    /// <summary>Gets a value indicating whether the caller is signed in: always.</summary>
    public bool IsAuthenticated => true;

    /// <summary>Gets the caller's user id.</summary>
    public string? UserId => userId.ToString();

    /// <summary>Has no roles.</summary>
    /// <param name="role">The role.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool IsInRole(string role) => false;
}

/// <summary>
/// Domain events are delivered to every handler registered for their concrete type, in raise order. Bot notices
/// (background handlers, T-6c) are not run in the request: the event is queued with who caused it, and the runner
/// delivers it later.
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

    /// <summary>A background handler is not run in the request; the event is queued once, with the caller as the actor.</summary>
    [Fact]
    public async Task Background_handlers_are_queued_with_the_actor_not_run()
    {
        var log = new List<string>();
        var queue = new RecordingQueue();
        var actor = Guid.NewGuid();
        await using var services = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<TeamRenamed>>(new RecordingHandler("inline", log))
            .AddSingleton<IBackgroundEventHandler<TeamRenamed>>(new RecordingNotice(log))
            .AddSingleton<IBackgroundEventQueue>(queue)
            .AddScoped<ICurrentUser>(_ => new FixedCurrentUser(actor))
            .AddApplication()
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var team = Guid.NewGuid();

        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new TeamRenamed(team), new NobodyCares()], TestContext.Current.CancellationToken);

        log.Should().Equal($"inline:{team}");
        queue.Queued.Should().ContainSingle().Which.Should().Be(((IDomainEvent)new TeamRenamed(team), new EventContext(actor)));
    }

    /// <summary>The runner delivers a queued event to every background handler of its type, with its context.</summary>
    [Fact]
    public async Task Runner_delivers_queued_events_to_background_handlers()
    {
        var log = new List<string>();
        var actor = Guid.NewGuid();
        await using var services = new ServiceCollection()
            .AddSingleton<IBackgroundEventHandler<TeamRenamed>>(new RecordingNotice(log))
            .AddSingleton<IBackgroundEventQueue>(new RecordingQueue())
            .AddApplication()
            .BuildServiceProvider();
        var team = Guid.NewGuid();

        await services.GetRequiredService<IBackgroundEventRunner>()
            .RunAsync(new TeamRenamed(team), new EventContext(actor), TestContext.Current.CancellationToken);

        log.Should().Equal($"notice:{team}:{actor}");
    }

    /// <summary>Every bot notice in the application is a background handler (T-6c): none talks to Telegram in the request.</summary>
    [Fact]
    public void Bot_notices_are_background_handlers()
    {
        var notices = new[]
        {
            typeof(SsSport.Drive.Application.Telegram.Progress.PlayerProgressUpdater),
            typeof(SsSport.Drive.Application.MiniApp.NotifyOwnerOfAddedFolder),
            typeof(SsSport.Drive.Application.Telegram.Access.NotifyAdminsOfAccessRequest),
            typeof(SsSport.Drive.Application.Telegram.Access.WelcomeApprovedUser),
            typeof(SsSport.Drive.Application.Telegram.Access.TellDeclinedUser),
        };

        notices.Should().AllSatisfy(notice =>
        {
            notice.GetInterfaces().Should().Contain(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IBackgroundEventHandler<>));
            notice.GetInterfaces().Should().NotContain(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>));
        });
    }
}
