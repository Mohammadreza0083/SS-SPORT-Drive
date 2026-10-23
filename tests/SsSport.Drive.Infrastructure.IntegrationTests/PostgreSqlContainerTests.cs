using Npgsql;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests;

/// <summary>
/// Proves the Testcontainers pipeline works: a real PostgreSQL starts in Docker and accepts queries.
/// Persistence tests (Unit of Work, repositories, migrations) build on this fixture.
/// </summary>
public sealed class PostgreSqlContainerTests : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts the PostgreSQL container before the test runs.</summary>
    /// <returns>A task that completes when the container is ready.</returns>
    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>A query against the containerised database returns the expected value.</summary>
    [Fact]
    public async Task Container_accepts_queries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        result.Should().Be(1);
    }
}
