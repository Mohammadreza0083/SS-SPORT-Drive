using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Api.ErrorHandling;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// A broken domain rule becomes a 422 ProblemDetails response carrying the rule's stable code,
/// without leaking the developer-facing exception message.
/// </summary>
public sealed class DomainExceptionHandlerTests
{
    /// <summary>A <see cref="DomainException"/> is written as application/problem+json with status 422 and its code.</summary>
    [Fact]
    public async Task Domain_exception_becomes_a_422_problem_with_its_code()
    {
        await using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var handler = new DomainExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(
            context,
            new DomainException("player.too_many_positions", "Internal detail that must not reach clients."),
            TestContext.Current.CancellationToken);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(422);
        problem.RootElement.GetProperty("code").GetString().Should().Be("player.too_many_positions");
        problem.RootElement.GetRawText().Should().NotContain("Internal detail");
    }

    /// <summary>Any other exception is left to the default handler (a generic 500 ProblemDetails).</summary>
    [Fact]
    public async Task Other_exceptions_are_not_handled()
    {
        await using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var handler = new DomainExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

        handled.Should().BeFalse();
    }
}
