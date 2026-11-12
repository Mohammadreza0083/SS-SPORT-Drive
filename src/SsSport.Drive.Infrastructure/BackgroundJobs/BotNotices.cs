using SsSport.Drive.Application.Telegram.Digest;
using System.Text.Json;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.BackgroundJobs;

/// <summary>Hangfire's storage settings: its own <c>hangfire</c> schema in our PostgreSQL database (T-6c).</summary>
public static class HangfireStorage
{
    /// <summary>The schema Hangfire's tables live in, next to ours.</summary>
    public const string Schema = "hangfire";

    /// <summary>
    /// Builds the storage options. The schema is never created on start-up (decisions §2): <c>migrate</c> installs it.
    /// The queue is polled every second, so a notice leaves within about a second of the change.
    /// </summary>
    /// <returns>The options.</returns>
    public static PostgreSqlStorageOptions Options() => new()
    {
        SchemaName = Schema,
        PrepareSchemaIfNecessary = false,
        QueuePollInterval = TimeSpan.FromSeconds(1),
    };

    /// <summary>
    /// Sets how Hangfire writes job arguments. These are process-wide, the same for every host, and setting them again
    /// changes nothing, so they are safe to share.
    /// </summary>
    public static void UseSerializerSettings() =>
        GlobalConfiguration.Configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings();

    /// <summary>
    /// Registers (or updates) the recurring jobs: the every-minute round that sends held bot notices (T-8), and the daily
    /// clean-up of the gate's expired rows at 00:00 UTC, 03:30 in Tehran (L-2b), and the admins' daily digest at 05:30 UTC, 09:00 in Tehran
    /// (L-7; Iran has had no daylight saving since 2022, so the UTC time is fixed). <c>migrate</c> calls it after installing
    /// the schema, so app instances never race to register them; safe to run on every deploy.
    /// </summary>
    /// <param name="storage">Hangfire's storage.</param>
    public static void RegisterRecurringJobs(JobStorage storage)
    {
        var manager = new RecurringJobManager(storage);
        manager.AddOrUpdate<NoticeFlushJob>(
            NoticeFlushJob.RecurringJobId,
            BotNoticeJob.Queue,
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());
        manager.AddOrUpdate<GatePurgeJob>(
            GatePurgeJob.RecurringJobId,
            BotNoticeJob.Queue,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(0, 0));
        manager.AddOrUpdate<DailyDigestJob>(
            DailyDigestJob.RecurringJobId,
            BotNoticeJob.Queue,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(5, 30));
    }

    /// <summary>Installs or upgrades Hangfire's schema; safe to run on every deploy.</summary>
    /// <param name="dataSource">The database.</param>
    /// <param name="cancellationToken">Cancels opening the connection.</param>
    /// <returns>A task that completes when the schema is current.</returns>
    public static async Task InstallAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        PostgreSqlObjectsInstaller.Install(connection, Schema);
    }
}

/// <summary>Hands Hangfire connections from the app's shared <see cref="NpgsqlDataSource"/>.</summary>
/// <param name="dataSource">The database.</param>
internal sealed class DataSourceConnectionFactory(NpgsqlDataSource dataSource) : IConnectionFactory
{
    /// <summary>Creates a new, unopened connection; Hangfire opens and disposes it.</summary>
    /// <returns>The connection.</returns>
    public NpgsqlConnection GetOrCreateConnection() => dataSource.CreateConnection();
}

/// <summary>
/// The Hangfire job that runs a queued event's background handlers (bot notices, T-6c). The event travels as its type
/// name and JSON; only domain event types from the Domain assembly are accepted back.
/// </summary>
/// <param name="runner">Runs the background handlers in the job's scope.</param>
/// <param name="logger">The logger: Hangfire's own logging is not wired to ours, so failures are logged here.</param>
public sealed partial class BotNoticeJob(IBackgroundEventRunner runner, ILogger<BotNoticeJob> logger)
{
    /// <summary>The queue bot notices run on; the API's Hangfire server serves only this queue.</summary>
    public const string Queue = "notices";

    /// <summary>Runs the background handlers of the event. A failure is retried up to 3 times by Hangfire.</summary>
    /// <param name="eventType">The event's full type name.</param>
    /// <param name="payload">The event as JSON.</param>
    /// <param name="actorUserId">Who caused it, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when every handler ran.</returns>
    [Queue(Queue)]
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(string eventType, string payload, Guid? actorUserId, CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(Deserialize(eventType, payload), new EventContext(actorUserId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, eventType);
            throw;
        }
    }

    /// <summary>Turns a queued event back into its domain event; anything but a Domain event type is refused.</summary>
    /// <param name="eventType">The event's full type name.</param>
    /// <param name="payload">The event as JSON.</param>
    /// <returns>The event.</returns>
    /// <exception cref="InvalidOperationException">The type is not a domain event, or the JSON is not one.</exception>
    public static IDomainEvent Deserialize(string eventType, string payload)
    {
        var type = Domain.AssemblyReference.Assembly.GetType(eventType, throwOnError: false);
        if (type is null || type.IsAbstract || !typeof(IDomainEvent).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"'{eventType}' is not a domain event.");
        }

        return JsonSerializer.Deserialize(payload, type) as IDomainEvent
            ?? throw new InvalidOperationException($"The payload is not a {type.Name}.");
    }

    /// <summary>Logs a notice that failed; Hangfire retries it.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="EventType">The event's type name.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Bot notice for {EventType} failed; Hangfire will retry it")]
    private static partial void LogFailed(ILogger logger, Exception exception, string EventType);
}

/// <summary>
/// The recurring Hangfire job that sends held bot notices once they are due (T-8): every minute, on the notices queue,
/// so it never runs at the same time as a notice job (one worker).
/// </summary>
/// <param name="sender">Sends the MediatR command.</param>
/// <param name="logger">The logger: Hangfire's own logging is not wired to ours, so failures are logged here.</param>
public sealed partial class NoticeFlushJob(ISender sender, ILogger<NoticeFlushJob> logger)
{
    /// <summary>The recurring job's id.</summary>
    public const string RecurringJobId = "bot-notices-due";

    /// <summary>Sends the due notices. A failure is not retried: the next minute's run picks them up.</summary>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when the round is done.</returns>
    [Queue(BotNoticeJob.Queue)]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new SendDueNoticesCommand(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
            throw;
        }
    }

    /// <summary>Logs a round that failed; the next one tries again.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending due bot notices failed; the next round tries again")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Queues events for their background handlers as Hangfire jobs on PostgreSQL (T-6c).</summary>
/// <param name="jobs">Hangfire's client.</param>
internal sealed class HangfireBackgroundEventQueue(IBackgroundJobClient jobs) : IBackgroundEventQueue
{
    /// <summary>Stores a job for the event; it runs after the request, on the notices queue.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it.</param>
    /// <param name="cancellationToken">Unused: Hangfire's client is synchronous.</param>
    /// <returns>A completed task once the job is stored.</returns>
    public Task EnqueueAsync(IDomainEvent domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(context);
        var type = domainEvent.GetType();
        var payload = JsonSerializer.Serialize(domainEvent, type);
        jobs.Enqueue<BotNoticeJob>(job => job.RunAsync(type.FullName!, payload, context.ActorUserId, CancellationToken.None));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The Hangfire server inside the API (owner, 2026-10-31): one worker on the notices queue, so a player's notices run
/// in the order they were raised. It also runs the every-minute job that sends held notices (T-8), which
/// <c>migrate</c> registers. It does not start without a database (a bare host), or when
/// <c>BackgroundJobs__RunServer</c> is <c>false</c> (the test suite, which runs many hosts in one process).
/// </summary>
/// <param name="services">The root service provider.</param>
/// <param name="configuration">The configuration, read at start so test overrides apply.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class BotNoticeServer(IServiceProvider services, IConfiguration configuration, ILogger<BotNoticeServer> logger) : IHostedService, IAsyncDisposable
{
    /// <summary>The setting that turns the server off (default on).</summary>
    public const string RunServerSetting = "BackgroundJobs:RunServer";

    /// <summary>The running server, or <see langword="null"/> when it did not start or has stopped.</summary>
    private BackgroundJobServer? _server;

    /// <summary>Starts the server when a database is configured and the server is not turned off.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue(RunServerSetting, defaultValue: true))
        {
            LogTurnedOff(logger);
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(DependencyInjection.ConnectionStringName)))
        {
            LogNotStarted(logger);
            return Task.CompletedTask;
        }

        var options = new BackgroundJobServerOptions
        {
            Queues = [BotNoticeJob.Queue],
            WorkerCount = 1,
            ServerName = $"api-notices:{Environment.MachineName}",
            // Hangfire.NetCore's activator: every job runs in its own DI scope.
            Activator = services.GetRequiredService<JobActivator>(),
        };
        _server = new BackgroundJobServer(options, services.GetRequiredService<JobStorage>());
        LogStarted(logger, BotNoticeJob.Queue);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks the server to stop, waits for the running notice to finish and disposes it. Safe to call more than once:
    /// a host may stop its services again while it is disposed.
    /// </summary>
    /// <param name="cancellationToken">Gives up waiting when the host stops forcefully.</param>
    /// <returns>A task that completes when the server has stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _server, null) is not { } server)
        {
            return;
        }

        try
        {
            server.SendStop();
            await server.WaitForShutdownAsync(cancellationToken);
        }
        finally
        {
            server.Dispose();
        }
    }

    /// <summary>Disposes a server that was never stopped.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _server, null)?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Logs that the server runs.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Queue">The queue it serves.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Hangfire server started on queue {Queue}")]
    private static partial void LogStarted(ILogger logger, string Queue);

    /// <summary>Logs that the server did not start.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Hangfire server not started: no database is configured")]
    private static partial void LogNotStarted(ILogger logger);

    /// <summary>Logs that the server is turned off by configuration.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Hangfire server not started: BackgroundJobs__RunServer is false")]
    private static partial void LogTurnedOff(ILogger logger);
}

/// <summary>
/// The daily Hangfire job that deletes the gate's expired rows (L-2b): email codes, viewer sessions and trusted devices
/// past their retention. It runs on the notices queue, the one the API's Hangfire server works.
/// </summary>
/// <param name="sender">Sends the MediatR command.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger: Hangfire's own logging is not wired to ours.</param>
public sealed partial class GatePurgeJob(ISender sender, TimeProvider timeProvider, ILogger<GatePurgeJob> logger)
{
    /// <summary>The recurring job's id.</summary>
    public const string RecurringJobId = "gate-purge";

    /// <summary>Deletes the expired rows. A failure is not retried: the next day's run picks them up.</summary>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when the clean-up is done.</returns>
    [Queue(BotNoticeJob.Queue)]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.Send(new PurgeGateRowsCommand(timeProvider.GetUtcNow()), cancellationToken);
            LogPurged(logger, result.EmailCodes, result.Sessions, result.Devices);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
            throw;
        }
    }

    /// <summary>Logs what a run deleted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="EmailCodes">Email codes deleted.</param>
    /// <param name="Sessions">Viewer sessions deleted.</param>
    /// <param name="Devices">Trusted devices deleted.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Gate clean-up deleted {EmailCodes} email codes, {Sessions} viewer sessions and {Devices} trusted devices")]
    private static partial void LogPurged(ILogger logger, int EmailCodes, int Sessions, int Devices);

    /// <summary>Logs a run that failed; the next one tries again.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gate clean-up failed; the next daily run tries again")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>
/// The daily Hangfire job that sends the admins' digest of yesterday (L-7) at 09:00 Tehran. It runs on the notices queue,
/// the one the API's Hangfire server works.
/// </summary>
/// <param name="sender">Sends the MediatR command.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger: Hangfire's own logging is not wired to ours.</param>
public sealed partial class DailyDigestJob(ISender sender, TimeProvider timeProvider, ILogger<DailyDigestJob> logger)
{
    /// <summary>The recurring job's id.</summary>
    public const string RecurringJobId = "daily-digest";

    /// <summary>Sends the digest. A failure is not retried: a late digest of yesterday is worse than none, and tomorrow's covers its own day.</summary>
    /// <param name="cancellationToken">Cancelled when the server shuts down.</param>
    /// <returns>A task that completes when the digest has been sent.</returns>
    [Queue(BotNoticeJob.Queue)]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var admins = await sender.Send(new SendDailyDigestCommand(timeProvider.GetUtcNow()), cancellationToken);
            LogSent(logger, admins);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
            throw;
        }
    }

    /// <summary>Logs a digest that went out.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Admins">How many admins got it.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "The daily digest was sent to {Admins} admins")]
    private static partial void LogSent(ILogger logger, int Admins);

    /// <summary>Logs a digest that failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "The daily digest failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
