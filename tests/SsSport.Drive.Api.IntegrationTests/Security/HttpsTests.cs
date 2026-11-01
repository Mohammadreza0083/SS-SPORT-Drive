using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SsSport.Drive.Api.IntegrationTests.Security;

/// <summary>
/// In production the API is HTTPS only (decisions §1b): plain HTTP is redirected, HTTPS answers carry HSTS, and the
/// scheme the client really used is taken from <c>X-Forwarded-Proto</c> only when a trusted reverse proxy sends it.
/// Container health probes stay on plain HTTP.
/// </summary>
public sealed class HttpsTests : IDisposable
{
    /// <summary>The reverse proxy the tests trust.</summary>
    private const string TrustedProxy = "10.20.0.5";

    /// <summary>The API host in production, trusting <see cref="TrustedProxy"/>.</summary>
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("Security:TrustedProxies", $"{TrustedProxy}, 10.30.0.0/16")
            .ConfigureTestServices(services => services.AddTransient<IStartupFilter, TestClientAddress>()));

    /// <summary>A plain HTTP request is permanently redirected to HTTPS.</summary>
    [Fact]
    public async Task Http_is_redirected_to_https()
    {
        using var client = CreateClient("http://api.ss-sport.co");

        using var response = await client.GetAsync(new Uri("/miniapp/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PermanentRedirect);
        response.Headers.Location.Should().Be(new Uri("https://api.ss-sport.co/miniapp/me"));
    }

    /// <summary>HTTPS responses tell browsers to use HTTPS only, for a year, on every subdomain.</summary>
    [Fact]
    public async Task Https_responses_carry_hsts()
    {
        using var client = CreateClient("https://api.ss-sport.co");

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Strict-Transport-Security").Should().ContainSingle()
            .Which.Should().Contain("max-age=31536000").And.Contain("includeSubDomains");
    }

    /// <summary>Behind a trusted proxy that terminated TLS, the request counts as HTTPS: no redirect (the endpoint answers 401), and HSTS is sent.</summary>
    [Fact]
    public async Task Forwarded_https_from_a_trusted_proxy_is_honoured()
    {
        using var client = CreateClient("http://api.ss-sport.co");
        using var request = ForwardedRequest(TrustedProxy);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Strict-Transport-Security").Should().BeTrue();
    }

    /// <summary>A proxy inside a trusted network range is trusted as well.</summary>
    [Fact]
    public async Task Forwarded_https_from_a_trusted_network_is_honoured()
    {
        using var client = CreateClient("http://api.ss-sport.co");
        using var request = ForwardedRequest("10.30.4.7");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Anyone else claiming HTTPS in the header is ignored and still redirected.</summary>
    [Fact]
    public async Task Forwarded_https_from_an_untrusted_client_is_ignored()
    {
        using var client = CreateClient("http://api.ss-sport.co");
        using var request = ForwardedRequest("203.0.113.9");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PermanentRedirect);
    }

    /// <summary>The container health probes call over plain HTTP inside the network and are never redirected.</summary>
    /// <param name="path">A health endpoint.</param>
    [Theory]
    [InlineData("/health/live")]
    public async Task Health_probes_are_not_redirected(string path)
    {
        using var client = CreateClient("http://localhost");

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The API reference is not served in production at all.</summary>
    [Fact]
    public async Task Scalar_is_not_served_in_production()
    {
        using var client = CreateClient("https://api.ss-sport.co");

        using var response = await client.GetAsync(new Uri("/scalar", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Disposes the in-memory API host.</summary>
    public void Dispose() => _factory.Dispose();

    /// <summary>Creates a client that does not follow redirects.</summary>
    /// <param name="baseAddress">The scheme and host the client calls.</param>
    /// <returns>The client.</returns>
    private HttpClient CreateClient(string baseAddress) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(baseAddress), AllowAutoRedirect = false });

    /// <summary>Builds a plain-HTTP request that says, through the proxy headers, it arrived over HTTPS.</summary>
    /// <param name="from">The address the connection comes from.</param>
    /// <returns>The request.</returns>
    private static HttpRequestMessage ForwardedRequest(string from)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/miniapp/me", UriKind.Relative));
        request.Headers.Add(TestClientAddress.Header, from);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "198.51.100.20");
        return request;
    }
}
