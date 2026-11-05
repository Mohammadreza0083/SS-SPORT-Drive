using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;
using SsSport.Drive.Api.ErrorHandling;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// Database connections ran out under load (D-15). The API and the workers each get a fixed share of PostgreSQL's
/// connections, and a busy database is answered with 503 and <c>Retry-After</c> instead of a 500.
/// </summary>
/// <param name="factory">Boots the API with its real settings files.</param>
public sealed class DatabaseBusyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>PostgreSQL's default <c>max_connections</c>.</summary>
    private const int ServerConnections = 100;

    /// <summary>Connections kept free for <c>migrate</c>, a <c>psql</c> session and PostgreSQL's reserved superuser slots.</summary>
    private const int Headroom = 20;

    /// <summary>The API's pool is 40 connections (its Hangfire server shares them), set in its base settings.</summary>
    [Fact]
    public void Api_pool_is_40()
    {
        factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MaxPoolSize.Should().Be(40);
    }

    /// <summary>The API's and the workers' pools together leave headroom under PostgreSQL's limit.</summary>
    [Fact]
    public void Host_pools_fit_under_the_server_limit()
    {
        var api = MaxPoolSize("SsSport.Drive.Api");
        var workers = MaxPoolSize("SsSport.Drive.Workers");

        api.Should().Be(40);
        workers.Should().Be(20);
        (api + workers).Should().BeLessThanOrEqualTo(ServerConnections - Headroom);
    }

    /// <summary>A full pool or a full server is a 503 with a stable code and Retry-After, logged as a Warning.</summary>
    [Fact]
    public async Task Busy_database_becomes_503_with_retry_after()
    {
        var logger = new FakeLogger<DatabaseBusyExceptionHandler>();
        var busy = new InvalidOperationException("wrapped", new PostgresException("sorry, too many clients already", "FATAL", "FATAL", PostgresErrorCodes.TooManyConnections));

        var (handled, context) = await HandleAsync(busy, logger);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("1");
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        problem.RootElement.GetProperty("code").GetString().Should().Be(DatabaseBusyExceptionHandler.Code);
        problem.RootElement.GetProperty("title").GetString().Should().NotContain("clients", "internal details stay out of the response");
        logger.Collector.LatestRecord.Level.Should().Be(LogLevel.Warning);
    }

    /// <summary>The API answers a busy database through this handler.</summary>
    [Fact]
    public void Api_uses_the_busy_database_handler()
    {
        factory.Services.GetServices<IExceptionHandler>().Should().ContainSingle(handler => handler is DatabaseBusyExceptionHandler);
    }

    /// <summary>Other exceptions are left to the next handler.</summary>
    [Fact]
    public async Task Other_exceptions_are_not_handled()
    {
        var (handled, _) = await HandleAsync(new InvalidOperationException(), new FakeLogger<DatabaseBusyExceptionHandler>());

        handled.Should().BeFalse();
    }

    /// <summary>Reads <c>Database:MaxPoolSize</c> from a host's base settings file.</summary>
    /// <param name="host">The host project.</param>
    /// <returns>The pool size.</returns>
    private static int MaxPoolSize(string host)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", host, "appsettings.json")));
        return settings.RootElement.GetProperty("Database").GetProperty("MaxPoolSize").GetInt32();
    }

    /// <summary>Runs the handler on a fresh request.</summary>
    /// <param name="exception">The exception to handle.</param>
    /// <param name="logger">Records what the handler logs.</param>
    /// <returns>Whether it was handled, and the request.</returns>
    private static async Task<(bool Handled, DefaultHttpContext Context)> HandleAsync(Exception exception, FakeLogger<DatabaseBusyExceptionHandler> logger)
    {
        await using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var handler = new DatabaseBusyExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), logger);

        var handled = await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);
        return (handled, context);
    }

    /// <summary>Finds the repository root (the folder with the solution file) above the test output.</summary>
    /// <returns>The root folder.</returns>
    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "SsSport.Drive.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new DirectoryNotFoundException("The repository root (SsSport.Drive.slnx) was not found.");
    }
}
