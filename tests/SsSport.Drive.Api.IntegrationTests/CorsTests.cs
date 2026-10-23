using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// The API only answers cross-origin browser requests from the configured web front-end origins.
/// </summary>
public sealed class CorsTests : IDisposable
{
    /// <summary>The origin of the SS-SPORT web app in these tests.</summary>
    private const string WebOrigin = "http://localhost:3000";

    /// <summary>An origin that is not configured and must be refused.</summary>
    private const string ForeignOrigin = "https://evil.example";

    /// <summary>The API host allowing <see cref="WebOrigin"/> and the production site, in the comma-separated .env format.</summary>
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins", $"{WebOrigin}, https://drive.ss-sport.co"));

    /// <summary>A preflight request from the web app is allowed and echoes its origin.</summary>
    [Fact]
    public async Task Preflight_from_the_web_origin_is_allowed()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Preflight(WebOrigin), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(WebOrigin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().ContainSingle().Which.Should().Be("true");
    }

    /// <summary>A preflight request from any other site gets no CORS grant.</summary>
    [Fact]
    public async Task Preflight_from_a_foreign_origin_is_refused()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Preflight(ForeignOrigin), TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    /// <summary>A simple cross-origin GET from a foreign site gets no CORS grant, so the browser hides the response.</summary>
    [Fact]
    public async Task Simple_request_from_a_foreign_origin_gets_no_cors_header()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.Add("Origin", ForeignOrigin);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    /// <summary>Disposes the in-memory API host.</summary>
    public void Dispose() => _factory.Dispose();

    /// <summary>Builds a CORS preflight request for a POST from the given origin.</summary>
    /// <param name="origin">The origin the browser reports.</param>
    /// <returns>The preflight <c>OPTIONS</c> request.</returns>
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, new Uri("/health/live", UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }
}
