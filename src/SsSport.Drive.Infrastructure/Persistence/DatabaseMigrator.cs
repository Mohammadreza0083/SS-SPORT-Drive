using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SsSport.Drive.Infrastructure.BackgroundJobs;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations, installs Hangfire's schema and recurring jobs, and keeps the least-privilege application role
/// (<see cref="DatabaseAppRole"/>). Run as its own step (<c>SsSport.Drive.Api migrate</c>: a one-shot container
/// locally and a pipeline step in CD), never on normal start-up, so app instances never race to change the schema.
/// </summary>
public static partial class DatabaseMigrator
{
    /// <summary>Applies every pending migration; does nothing when the database is up to date.</summary>
    /// <param name="services">The root service provider of a host that called <c>AddInfrastructure</c>.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>A task that completes when the database is up to date.</returns>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
        // Checked first, so bad settings change nothing.
        var appRole = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseAppRoleOptions>>().Value;
        var manageAppRole = DatabaseAppRole.Validate(appRole);
        if (manageAppRole)
        {
            await DatabaseAppRole.EnsureNotOwnerAsync(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(), appRole, cancellationToken);
        }

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            LogUpToDate(logger);
        }
        else
        {
            LogApplying(logger, pending.Count, pending[^1]);
            await context.Database.MigrateAsync(cancellationToken);
            LogApplied(logger, pending.Count);
        }

        // Hangfire keeps its own versioned scripts in its own schema; installing is a no-op when it is current (T-6c).
        await HangfireStorage.InstallAsync(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(), cancellationToken);
        HangfireStorage.RegisterRecurringJobs(scope.ServiceProvider.GetRequiredService<JobStorage>());
        LogHangfireReady(logger, HangfireStorage.Schema);

        if (manageAppRole)
        {
            await DatabaseAppRole.EnsureAsync(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(), appRole, cancellationToken);
            LogAppRoleReady(logger, appRole.AppUser!);
        }
    }

    /// <summary>Logs that there is nothing to apply.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date")]
    private static partial void LogUpToDate(ILogger logger);

    /// <summary>Logs the migrations about to be applied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Count">How many migrations are pending.</param>
    /// <param name="TargetMigration">The last pending migration.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} database migration(s) up to {TargetMigration}")]
    private static partial void LogApplying(ILogger logger, int Count, string TargetMigration);

    /// <summary>Logs that the migrations were applied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Count">How many migrations were applied.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} database migration(s)")]
    private static partial void LogApplied(ILogger logger, int Count);

    /// <summary>Logs that Hangfire's schema is current.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Schema">Hangfire's schema.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Hangfire schema {Schema} and recurring jobs are up to date")]
    private static partial void LogHangfireReady(ILogger logger, string Schema);

    /// <summary>Logs that the application role is current (its name only; never the password).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="AppRole">The role's name.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Database application role {AppRole} is up to date")]
    private static partial void LogAppRoleReady(ILogger logger, string AppRole);
}
