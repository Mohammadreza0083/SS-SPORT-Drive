using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SsSport.Drive.Api.IntegrationTests.Security;

/// <summary>
/// Every API response carries the security headers (decisions §1b): no MIME sniffing, no framing, no referrer, no
/// powerful browser features, and a CSP that lets a JSON API load nothing. The Scalar docs keep the other headers but
/// not the strict CSP, so the reference page can run.
/// </summary>
public sealed class SecurityHeadersTests : IDisposable
{
    /// <summary>The API host in its default (Development) environment.</summary>
    private readonly WebApplicationFactory<Program> _factory = new();

    /// <summary>A normal response carries every security header.</summary>
    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldHaveBaseHeaders(response);
        var csp = Header(response, "Content-Security-Policy");
        csp.Should().Contain("default-src 'none'").And.Contain("frame-ancestors 'none'").And.Contain("base-uri 'none'");
    }

    /// <summary>Error responses (here a 404 and a 401) carry them too, since the headers are added before anything runs.</summary>
    /// <param name="path">A path that ends in an error.</param>
    [Theory]
    [InlineData("/no-such-route")]
    [InlineData("/miniapp/me")]
    public async Task Error_responses_carry_the_security_headers(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        ShouldHaveBaseHeaders(response);
        Header(response, "Content-Security-Policy").Should().Contain("default-src 'none'");
    }

    /// <summary>The Scalar reference keeps the base headers but is not given the API's script-blocking CSP.</summary>
    [Fact]
    public async Task Scalar_docs_are_not_blocked_by_the_api_csp()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/scalar", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldHaveBaseHeaders(response);
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
    }

    /// <summary>Disposes the in-memory API host.</summary>
    public void Dispose() => _factory.Dispose();

    /// <summary>Asserts the headers every response gets, whatever its path.</summary>
    /// <param name="response">The response.</param>
    private static void ShouldHaveBaseHeaders(HttpResponseMessage response)
    {
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Permissions-Policy").Should().Contain("camera=()").And.Contain("geolocation=()").And.Contain("microphone=()");
        Header(response, "Cross-Origin-Opener-Policy").Should().Be("same-origin");
    }

    /// <summary>Reads a single response header.</summary>
    /// <param name="response">The response.</param>
    /// <param name="name">The header name.</param>
    /// <returns>Its only value.</returns>
    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Should().ContainSingle().Subject : throw new Xunit.Sdk.XunitException($"Missing header {name}.");
}
