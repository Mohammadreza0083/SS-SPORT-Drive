using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>A paged probe query: 45 fixed names.</summary>
/// <param name="Paging">The page to read.</param>
public sealed record ListProbeNames(PageRequest Paging) : IPagedQuery<string>;

/// <summary>Answers <see cref="ListProbeNames"/> from a fixed list of 45 names.</summary>
public sealed class ListProbeNamesHandler : IQueryHandler<ListProbeNames, PagedResult<string>>
{
    /// <summary>Returns the requested page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The page.</returns>
    public Task<PagedResult<string>> Handle(ListProbeNames request, CancellationToken cancellationToken)
    {
        var names = Enumerable.Range(1, 45).Select(index => $"Player {index}").ToList();
        return Task.FromResult(PagedResult<string>.Create([.. names.Skip(request.Paging.Skip).Take(request.Paging.PageSize)], request.Paging, names.Count));
    }
}

/// <summary>A cursor probe query.</summary>
/// <param name="Paging">Where to continue from.</param>
public sealed record ListProbeEvents(CursorRequest Paging) : ICursorQuery<string>;

/// <summary>Answers <see cref="ListProbeEvents"/> with a last page.</summary>
public sealed class ListProbeEventsHandler : IQueryHandler<ListProbeEvents, CursorResult<string>>
{
    /// <summary>Returns a last page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The page.</returns>
    public Task<CursorResult<string>> Handle(ListProbeEvents request, CancellationToken cancellationToken) =>
        Task.FromResult(new CursorResult<string>(["played"], null));
}

/// <summary>Maps probe list endpoints the way real list endpoints are written, for these tests only.</summary>
internal sealed class ProbeEndpointsFilter : IStartupFilter
{
    /// <summary>Adds the probe endpoints after the host's own pipeline.</summary>
    /// <param name="next">The host's pipeline.</param>
    /// <returns>The extended pipeline.</returns>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/probe/names", ListNamesAsync).WithName("ListProbeNames").WithPagedResponse<string>();
            endpoints.MapGet("/probe/events", ListEventsAsync).WithName("ListProbeEvents").WithCursorResponse<string>();
        });
    };

    /// <summary>Lists one page of names.</summary>
    /// <param name="paging">The page and size from the query string.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page.</returns>
    private static Task<PagedResult<string>> ListNamesAsync([AsParameters] PageParameters paging, ISender sender, CancellationToken cancellationToken) =>
        sender.Send(new ListProbeNames(paging.ToRequest()), cancellationToken);

    /// <summary>Lists one page of events.</summary>
    /// <param name="paging">The cursor and size from the query string.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page.</returns>
    private static Task<CursorResult<string>> ListEventsAsync([AsParameters] CursorParameters paging, ISender sender, CancellationToken cancellationToken) =>
        sender.Send(new ListProbeEvents(paging.ToRequest()), cancellationToken);
}

/// <summary>
/// List endpoints read <c>page</c>/<c>pageSize</c> (or <c>cursor</c>/<c>pageSize</c>) from the query string, answer with one
/// page in the shared shape, reject invalid paging with a 400 keyed by parameter name, and document all of it in OpenAPI.
/// </summary>
/// <param name="factory">The in-memory API host.</param>
public sealed class PagingEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The host with the probe endpoints and their handlers.</summary>
    private readonly WebApplicationFactory<Program> _host = factory.WithWebHostBuilder(builder => builder
        // The pipeline builds the unit of work for every request; the probes never touch the database, so nothing connects.
        .UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=unused;Username=unused;Password=unused")
        .ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, ProbeEndpointsFilter>();
            services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<ProbeEndpointsFilter>());
        }));

    /// <summary>Without paging values, the first page of 20 is returned with the totals.</summary>
    [Fact]
    public async Task Defaults_to_the_first_page_of_twenty()
    {
        using var page = await GetJsonAsync("/probe/names", HttpStatusCode.OK);

        page.RootElement.GetProperty("items").GetArrayLength().Should().Be(20);
        page.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        page.RootElement.GetProperty("pageSize").GetInt32().Should().Be(20);
        page.RootElement.GetProperty("totalCount").GetInt32().Should().Be(45);
        page.RootElement.GetProperty("totalPages").GetInt32().Should().Be(3);
    }

    /// <summary>The requested page and size are honoured.</summary>
    [Fact]
    public async Task Reads_the_requested_page()
    {
        using var page = await GetJsonAsync("/probe/names?page=3&pageSize=20", HttpStatusCode.OK);

        page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("Player 41", "Player 42", "Player 43", "Player 44", "Player 45");
    }

    /// <summary>Invalid paging is a 400 whose errors are keyed by the query parameter names.</summary>
    [Fact]
    public async Task Invalid_paging_is_a_400_keyed_by_parameter()
    {
        using var problem = await GetJsonAsync("/probe/names?page=0&pageSize=101", HttpStatusCode.BadRequest);

        var errors = problem.RootElement.GetProperty("errors");
        errors.TryGetProperty("page", out _).Should().BeTrue();
        errors.TryGetProperty("pageSize", out _).Should().BeTrue();
    }

    /// <summary>A cursor list returns its items and the next cursor (null on the last page).</summary>
    [Fact]
    public async Task Cursor_list_returns_items_and_next_cursor()
    {
        using var page = await GetJsonAsync("/probe/events", HttpStatusCode.OK);

        page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetString()).Should().Equal("played");
        page.RootElement.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>A tampered cursor is a 400 under <c>cursor</c>.</summary>
    [Fact]
    public async Task Tampered_cursor_is_a_400()
    {
        using var problem = await GetJsonAsync("/probe/events?cursor=tampered!", HttpStatusCode.BadRequest);

        problem.RootElement.GetProperty("errors").TryGetProperty("cursor", out _).Should().BeTrue();
    }

    /// <summary>The paging parameters are documented with their defaults and limits, and invalid paging as a 400.</summary>
    [Fact]
    public async Task Paging_parameters_are_documented()
    {
        using var document = await GetJsonAsync("/openapi/v1.json", HttpStatusCode.OK);
        var operation = document.RootElement.GetProperty("paths").GetProperty("/probe/names").GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToDictionary(parameter => parameter.GetProperty("name").GetString()!);

        parameters.Keys.Should().BeEquivalentTo("page", "pageSize");
        parameters["page"].GetProperty("in").GetString().Should().Be("query");
        parameters["page"].GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        parameters["page"].GetProperty("schema").GetProperty("default").GetInt32().Should().Be(1);
        parameters["page"].GetProperty("schema").GetProperty("minimum").GetInt32().Should().Be(1);
        parameters["pageSize"].GetProperty("schema").GetProperty("default").GetInt32().Should().Be(20);
        parameters["pageSize"].GetProperty("schema").GetProperty("maximum").GetInt32().Should().Be(100);

        var responses = operation.GetProperty("responses");
        responses.GetProperty("200").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _)
            .Should().BeTrue("list responses carry a realistic example");
        responses.GetProperty("400").GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();
    }

    /// <summary>The page shape is a documented schema with every field.</summary>
    [Fact]
    public async Task Paged_result_schema_is_documented()
    {
        using var document = await GetJsonAsync("/openapi/v1.json", HttpStatusCode.OK);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var paged = schemas.EnumerateObject().Single(schema => schema.Name.StartsWith("PagedResultOf", StringComparison.Ordinal)).Value;
        paged.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("items", "page", "pageSize", "totalCount", "totalPages");

        var cursor = schemas.EnumerateObject().Single(schema => schema.Name.StartsWith("CursorResultOf", StringComparison.Ordinal)).Value;
        cursor.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("items", "nextCursor");
    }

    /// <summary>Sends a GET and parses the JSON body after checking the status.</summary>
    /// <param name="path">The relative path and query.</param>
    /// <param name="expected">The expected status code.</param>
    /// <returns>The parsed body; the caller disposes it.</returns>
    private async Task<JsonDocument> GetJsonAsync(string path, HttpStatusCode expected)
    {
        using var client = _host.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(expected, body);
        return JsonDocument.Parse(body);
    }
}
