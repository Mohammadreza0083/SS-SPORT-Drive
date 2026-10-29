using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Api.ErrorHandling;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// Pipeline failures become ProblemDetails: validation 400 (with errors per field), sign-in required 401, missing role 403.
/// </summary>
public sealed class ApplicationExceptionHandlerTests
{
    /// <summary>A validation failure is a 400 ValidationProblemDetails listing the errors per property.</summary>
    [Fact]
    public async Task Validation_failure_becomes_400_with_errors()
    {
        var (status, problem) = await HandleAsync(new RequestValidationException(new Dictionary<string, string[]>
        {
            ["Name"] = ["'Name' must not be empty."],
        }));

        status.Should().Be(StatusCodes.Status400BadRequest);
        problem.RootElement.GetProperty("errors").GetProperty("Name")[0].GetString().Should().Be("'Name' must not be empty.");
    }

    /// <summary>A missing sign-in is a 401.</summary>
    [Fact]
    public async Task Unauthenticated_becomes_401()
    {
        var (status, problem) = await HandleAsync(new UnauthenticatedException());

        status.Should().Be(StatusCodes.Status401Unauthorized);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(401);
    }

    /// <summary>A missing role is a 403.</summary>
    [Fact]
    public async Task Forbidden_becomes_403()
    {
        var (status, problem) = await HandleAsync(new ForbiddenAccessException());

        status.Should().Be(StatusCodes.Status403Forbidden);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(403);
    }

    /// <summary>
    /// Two people changing the same record at once is a 409 with a stable code (D-13), so the client can reload and retry
    /// instead of seeing a server error.
    /// </summary>
    [Fact]
    public async Task Concurrency_conflict_becomes_409_with_a_code()
    {
        var (status, problem) = await HandleAsync(new ConcurrencyConflictException(new InvalidOperationException("stale row")));

        status.Should().Be(StatusCodes.Status409Conflict);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        problem.RootElement.GetProperty("code").GetString().Should().Be("concurrency.conflict");
        problem.RootElement.GetProperty("title").GetString().Should().NotContain("stale row", "internal details stay out of the response");
    }

    /// <summary>Other exceptions are left to the next handler.</summary>
    [Fact]
    public async Task Other_exceptions_are_not_handled()
    {
        await using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var handler = new ApplicationExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(new DefaultHttpContext { RequestServices = services }, new InvalidOperationException(), TestContext.Current.CancellationToken);

        handled.Should().BeFalse();
    }

    /// <summary>Runs the handler on a fresh request and parses the written ProblemDetails.</summary>
    /// <param name="exception">The exception to handle.</param>
    /// <returns>The status code and the parsed body; the caller disposes the document.</returns>
    private static async Task<(int Status, JsonDocument Problem)> HandleAsync(Exception exception)
    {
        await using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var handler = new ApplicationExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        handled.Should().BeTrue();
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken));
    }
}
