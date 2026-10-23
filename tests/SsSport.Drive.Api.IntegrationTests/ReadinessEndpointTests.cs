using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// The readiness probe reports healthy only when the API can reach its PostgreSQL database
/// through <c>ConnectionStrings:DefaultConnection</c>.
/// </summary>
public sealed class ReadinessEndpointTests : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts the PostgreSQL container before the tests run.</summary>
    /// <returns>A task that completes when the container is ready.</returns>
    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>With a reachable database the readiness probe answers 200 OK.</summary>
    [Fact]
    public async Task Ready_when_the_database_is_reachable()
    {
        await using var factory = CreateApi(_postgres.GetConnectionString());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>With an unreachable database the readiness probe answers 503 Service Unavailable.</summary>
    [Fact]
    public async Task Not_ready_when_the_database_is_unreachable()
    {
        await using var factory = CreateApi("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Creates an in-memory API host that uses the given database connection string.</summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The API host.</returns>
    private static WebApplicationFactory<Program> CreateApi(string connectionString) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString));
}
