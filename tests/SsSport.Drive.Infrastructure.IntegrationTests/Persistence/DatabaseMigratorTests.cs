using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>The <c>migrate</c> step (run by the API image as a one-shot job) brings an empty database up to date, and is safe to run again.</summary>
public sealed class DatabaseMigratorTests : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts PostgreSQL with an empty database.</summary>
    /// <returns>A task that completes when the server is ready.</returns>
    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>Migrating applies every migration; a second run changes nothing and does not fail.</summary>
    [Fact]
    public async Task Migrate_applies_all_migrations_and_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString() })
            .Build();
        await using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();

        await DatabaseMigrator.MigrateAsync(provider, cancellationToken);
        await DatabaseMigrator.MigrateAsync(provider, cancellationToken);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
        (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Should().NotBeEmpty();
    }
}
