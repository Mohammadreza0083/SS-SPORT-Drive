using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// One PostgreSQL container with the test schema, shared by every test in a class.
/// Tests keep their data apart by using unique values (e.g. a per-test title prefix).
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts PostgreSQL and creates the test schema.</summary>
    /// <returns>A task that completes when the database is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>Creates a fresh context against the shared container.</summary>
    /// <returns>The context; the caller disposes it.</returns>
    internal TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
