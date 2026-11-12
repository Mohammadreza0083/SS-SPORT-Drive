using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace SsSport.Drive.Api.IntegrationTests.Security;

/// <summary>
/// Every client address gets a per-minute request budget (decisions §1b). Going over it is answered 429 with
/// <c>Retry-After</c> and logged as suspicious. The Telegram webhook has its own, larger budget, and health probes are
/// never limited.
/// </summary>
public sealed class RateLimitingTests : IDisposable
{
    /// <summary>A limited path that answers 404 without touching the database, so the tests need none.</summary>
    private const string LimitedPath = "/no-such-route";

    /// <summary>A path under the link endpoints that answers 404 without touching the database.</summary>
    private const string LimitedLinkPath = "/links/Ab3xYz/no-such-route";

    /// <summary>A path under the viewer's content reads (V-2b) that answers 404 without touching the database.</summary>
    private const string ViewerContentPath = "/links/Ab3xYz/players/no-such-route";

    /// <summary>The client the tests act as.</summary>
    private const string ClientIp = "198.51.100.7";

    /// <summary>The API host with a tiny budget (3 per minute, 5 for the webhook, 2 for links, 4 for viewer content) so the limit is reached quickly.</summary>
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder
            .UseSetting("RateLimiting:PermitsPerMinute", "3")
            .UseSetting("RateLimiting:WebhookPermitsPerMinute", "5")
            .UseSetting("RateLimiting:LinkPermitsPerMinute", "2")
            .UseSetting("RateLimiting:ViewerPermitsPerMinute", "4")
            .ConfigureLogging(logging => logging.AddFakeLogging())
            .ConfigureTestServices(services => services.AddTransient<IStartupFilter, TestClientAddress>()));

    /// <summary>The request over the budget is refused with 429, a Retry-After and a ProblemDetails body.</summary>
    [Fact]
    public async Task Requests_over_the_budget_get_429_with_retry_after()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            using var allowed = await SendAsync(client, LimitedPath, ClientIp);
            allowed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using var response = await SendAsync(client, LimitedPath, ClientIp);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta.Should().BePositive();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>Each client address has its own budget.</summary>
    [Fact]
    public async Task Budgets_are_per_client_address()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            using var _ = await SendAsync(client, LimitedPath, ClientIp);
        }

        using var response = await SendAsync(client, LimitedPath, "198.51.100.8");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A refused request is logged as a warning with the path and client, so Loki can alert on floods.</summary>
    [Fact]
    public async Task Refused_requests_are_logged_as_suspicious()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            using var _ = await SendAsync(client, LimitedPath, ClientIp);
        }

        var collector = _factory.Services.GetRequiredService<FakeLogCollector>();
        collector.GetSnapshot().Should().Contain(record =>
            record.Level == LogLevel.Warning
            && record.Message.Contains("Rate limit exceeded", StringComparison.Ordinal)
            && record.Message.Contains(LimitedPath, StringComparison.Ordinal)
            && record.Message.Contains(ClientIp, StringComparison.Ordinal));
    }

    /// <summary>The webhook has its own larger budget, separate from the general one.</summary>
    [Fact]
    public async Task Webhook_has_its_own_budget()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            using var _ = await SendAsync(client, LimitedPath, ClientIp);
        }

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            using var webhook = await SendAsync(client, "/telegram/webhook", ClientIp, HttpMethod.Post);
            statuses.Add(webhook.StatusCode);
        }

        statuses.Take(5).Should().AllSatisfy(status => status.Should().Be(HttpStatusCode.Unauthorized));
        statuses[5].Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// The public link endpoints (<c>/links/*</c>: the gate, codes and sessions, L-2) have their own tighter budget, so
    /// codes and link codes cannot be guessed quickly; it does not use up the general budget.
    /// </summary>
    [Fact]
    public async Task Link_endpoints_have_a_tighter_budget()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await SendAsync(client, LimitedLinkPath, ClientIp);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(2).Should().AllSatisfy(status => status.Should().Be(HttpStatusCode.NotFound));
        statuses[2].Should().Be(HttpStatusCode.TooManyRequests);
        using var general = await SendAsync(client, LimitedPath, ClientIp);
        general.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A signed-in viewer's content reads (V-2b: <c>GET /links/{code}/content</c> and <c>/players/…</c>, photos included)
    /// have their own, larger budget, so browsing a roster does not run into the gate's tight one, and they do not use
    /// it up either. Writes to the same paths stay on the gate's budget.
    /// </summary>
    [Fact]
    public async Task Viewer_content_reads_have_their_own_budget()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await SendAsync(client, ViewerContentPath, ClientIp);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(4).Should().AllSatisfy(status => status.Should().Be(HttpStatusCode.NotFound));
        statuses[4].Should().Be(HttpStatusCode.TooManyRequests);
        using var gate = await SendAsync(client, LimitedLinkPath, ClientIp);
        gate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var content = await SendAsync(client, "/links/Ab3xYz/content/no-such-route", "198.51.100.9");
        content.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var write = await SendAsync(client, ViewerContentPath, "198.51.100.9", HttpMethod.Post);
        using var secondWrite = await SendAsync(client, ViewerContentPath, "198.51.100.9", HttpMethod.Post);
        using var thirdWrite = await SendAsync(client, ViewerContentPath, "198.51.100.9", HttpMethod.Post);
        thirdWrite.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>Health probes are never limited.</summary>
    [Fact]
    public async Task Health_probes_are_never_limited()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            using var response = await SendAsync(client, "/health/live", ClientIp);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>Disposes the in-memory API host.</summary>
    public void Dispose() => _factory.Dispose();

    /// <summary>Sends a request as the given client.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="path">The path.</param>
    /// <param name="from">The client address.</param>
    /// <param name="method">The method; GET when omitted.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string from, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add(TestClientAddress.Header, from);
        if (request.Method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
