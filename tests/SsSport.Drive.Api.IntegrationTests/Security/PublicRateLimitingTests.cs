using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SsSport.Drive.Api.IntegrationTests.Security;

/// <summary>
/// The public site's own per-address budgets (P-8, decisions §1b rule 10): reads under <c>/public</c> share
/// <c>RateLimiting__PublicPermitsPerMinute</c>; the two anti-spam writes (<c>POST /public/access-code</c> and
/// <c>POST /public/access-requests</c>) get the much tighter <c>RateLimiting__AccessPermitsPerMinute</c>. Neither uses up
/// the other, nor the general budget. Runs without a database: unmatched routes still count.
/// </summary>
public sealed class PublicRateLimitingTests : IDisposable
{
    /// <summary>The client address of every request.</summary>
    private const string ClientIp = "198.51.100.21";

    /// <summary>The API with small budgets.</summary>
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder
            .UseSetting("RateLimiting:PermitsPerMinute", "50")
            .UseSetting("RateLimiting:PublicPermitsPerMinute", "4")
            .UseSetting("RateLimiting:AccessPermitsPerMinute", "2")
            .ConfigureTestServices(services => services.AddTransient<IStartupFilter, TestClientAddress>()));

    /// <summary>The access writes are refused after their small budget; reads and the rest of the API still answer.</summary>
    [Fact]
    public async Task Access_writes_have_their_own_tight_budget()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        foreach (var path in new[] { "/public/access-code/x", "/public/access-requests/x", "/public/access-code/x" })
        {
            using var response = await SendAsync(client, HttpMethod.Post, path);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(2).Should().AllSatisfy(status => status.Should().NotBe(HttpStatusCode.TooManyRequests));
        statuses[2].Should().Be(HttpStatusCode.TooManyRequests);

        using var read = await SendAsync(client, HttpMethod.Get, "/public/no-such-route");
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var general = await SendAsync(client, HttpMethod.Get, "/no-such-route");
        general.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Public reads have their own budget, separate from the general one.</summary>
    [Fact]
    public async Task Public_reads_have_their_own_budget()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            using var allowed = await SendAsync(client, HttpMethod.Get, "/public/no-such-route");
            allowed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using var refused = await SendAsync(client, HttpMethod.Get, "/public/no-such-route");
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.Should().NotBeNull();

        using var general = await SendAsync(client, HttpMethod.Get, "/no-such-route");
        general.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Disposes the API.</summary>
    public void Dispose() => _factory.Dispose();

    /// <summary>Sends a request from <see cref="ClientIp"/>.</summary>
    /// <param name="client">The client.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add(TestClientAddress.Header, ClientIp);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
