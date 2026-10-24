using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// One clean PostgreSQL container per test class, migrated with the real migrations (not EnsureCreated),
/// so every test also proves the migrations apply on an empty database.
/// </summary>
public sealed class AppDbContextFixture : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts PostgreSQL and applies every migration.</summary>
    /// <returns>A task that completes when the database is migrated.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>Creates a fresh application context against the container.</summary>
    /// <returns>The context; the caller disposes it.</returns>
    internal AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(_postgres.GetConnectionString()).Options);
}
