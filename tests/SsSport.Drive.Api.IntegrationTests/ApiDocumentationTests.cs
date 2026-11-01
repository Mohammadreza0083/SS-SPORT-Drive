using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// The API publishes an OpenAPI document and the Scalar reference UI outside production,
/// and every endpoint in it carries a summary, a description, a tag and its responses.
/// </summary>
/// <param name="factory">The in-memory API host (Development environment).</param>
public sealed class ApiDocumentationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The OpenAPI document names the API and its contact address.</summary>
    [Fact]
    public async Task OpenApi_document_describes_the_api()
    {
        using var document = await GetOpenApiDocumentAsync();
        var info = document.RootElement.GetProperty("info");

        info.GetProperty("title").GetString().Should().Be("SS-SPORT Drive API");
        info.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        info.GetProperty("contact").GetProperty("email").GetString().Should().Be("info@ss-sport.co");
    }

    /// <summary>Both health probes are documented with summary, description, tag and responses.</summary>
    [Fact]
    public async Task Health_endpoints_are_documented()
    {
        using var document = await GetOpenApiDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        var live = paths.GetProperty("/health/live").GetProperty("get");
        AssertDescribed(live, "Health");
        live.GetProperty("responses").TryGetProperty("200", out _).Should().BeTrue();

        var ready = paths.GetProperty("/health/ready").GetProperty("get");
        AssertDescribed(ready, "Health");
        ready.GetProperty("responses").TryGetProperty("200", out _).Should().BeTrue();
        ready.GetProperty("responses").GetProperty("503").GetProperty("content")
            .TryGetProperty("application/problem+json", out _).Should().BeTrue("failures are reported as ProblemDetails");
    }

    /// <summary>
    /// Every rate-limited operation documents its 429 ProblemDetails answer with the <c>Retry-After</c> header;
    /// the health probes, which are never limited, do not.
    /// </summary>
    [Fact]
    public async Task Rate_limited_operations_document_429()
    {
        using var document = await GetOpenApiDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var responses = operation.Value.GetProperty("responses");
                if (path.Name.StartsWith("/health", StringComparison.Ordinal))
                {
                    responses.TryGetProperty("429", out _).Should().BeFalse($"{path.Name} is never rate limited");
                    continue;
                }

                var tooMany = responses.GetProperty("429");
                tooMany.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
                tooMany.GetProperty("headers").TryGetProperty("Retry-After", out _).Should().BeTrue($"{operation.Name} {path.Name} documents Retry-After");
                tooMany.GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();
            }
        }
    }

    /// <summary>The Scalar API reference UI is served at /scalar outside production.</summary>
    [Fact]
    public async Task Scalar_reference_is_served_outside_production()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/scalar", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    /// <summary>In production the API docs are not exposed (admin-only access arrives with admin auth).</summary>
    [Fact]
    public async Task Docs_are_hidden_in_production()
    {
        await using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var openApi = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), cancellationToken);
        using var scalar = await client.GetAsync(new Uri("/scalar", UriKind.Relative), cancellationToken);

        openApi.StatusCode.Should().Be(HttpStatusCode.NotFound);
        scalar.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Checks that an operation has a summary, a description and the expected tag.</summary>
    /// <param name="operation">The OpenAPI operation object.</param>
    /// <param name="tag">The expected tag.</param>
    private static void AssertDescribed(JsonElement operation, string tag)
    {
        operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("operationId").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("tags").EnumerateArray().Select(element => element.GetString()).Should().Contain(tag);
    }

    /// <summary>Downloads and parses the OpenAPI document.</summary>
    /// <returns>The parsed document; the caller disposes it.</returns>
    private async Task<JsonDocument> GetOpenApiDocumentAsync()
    {
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json);
    }
}
