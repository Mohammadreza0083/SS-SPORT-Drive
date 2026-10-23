using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// Boots the real API host in memory and checks the liveness probe used by Docker health checks.
/// </summary>
/// <param name="factory">The in-memory API host.</param>
public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The liveness endpoint answers 200 OK without authentication.</summary>
    [Fact]
    public async Task Liveness_endpoint_returns_ok()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
