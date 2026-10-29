using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SsSport.Drive.Application.Behaviors;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Pipeline;

/// <summary>
/// Every request is logged by name and duration with structured properties, never with its data.
/// </summary>
public sealed class LoggingBehaviorTests
{
    /// <summary>The request the tests send; its email must never appear in the logs.</summary>
    private readonly CreateTeam _request = new("Lions", "coach@example.com");

    /// <summary>A handled request is logged with its name and elapsed time as structured properties.</summary>
    [Fact]
    public async Task Logs_the_request_name_and_duration()
    {
        var logger = new FakeLogger<LoggingBehavior<CreateTeam, Guid>>();
        var behavior = new LoggingBehavior<CreateTeam, Guid>(logger);

        await behavior.Handle(_request, _ => Task.FromResult(Guid.NewGuid()), TestContext.Current.CancellationToken);

        var record = logger.Collector.GetSnapshot().Should().ContainSingle(entry => entry.Level == LogLevel.Information).Subject;
        record.StructuredState.Should().Contain(property => property.Key == "RequestName" && property.Value == "CreateTeam");
        record.StructuredState.Should().Contain(property => property.Key == "ElapsedMilliseconds");
    }

    /// <summary>A failing request is logged as an error with the exception, and the exception still propagates.</summary>
    [Fact]
    public async Task Logs_and_rethrows_failures()
    {
        var logger = new FakeLogger<LoggingBehavior<CreateTeam, Guid>>();
        var behavior = new LoggingBehavior<CreateTeam, Guid>(logger);

        var send = () => behavior.Handle(_request, _ => throw new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<InvalidOperationException>();
        var record = logger.Collector.GetSnapshot().Should().ContainSingle(entry => entry.Level == LogLevel.Error).Subject;
        record.Exception.Should().BeOfType<InvalidOperationException>();
        record.StructuredState.Should().Contain(property => property.Key == "RequestName" && property.Value == "CreateTeam");
    }

    /// <summary>
    /// Refusals the API answers on purpose (401, 403, 400, 404, 409, 422) are expected outcomes, not failures (D-13):
    /// they are logged at Information with the reason, never at Error, and still propagate.
    /// </summary>
    /// <param name="kind">The refusal.</param>
    /// <param name="reason">The expected <c>Reason</c> property.</param>
    [Theory]
    [InlineData("unauthenticated", "UnauthenticatedException")]
    [InlineData("forbidden", "ForbiddenAccessException")]
    [InlineData("validation", "RequestValidationException")]
    [InlineData("not_found", "NotFoundException")]
    [InlineData("conflict", "ConcurrencyConflictException")]
    [InlineData("rule", "player.name_required")]
    public async Task Expected_refusals_are_not_errors(string kind, string reason)
    {
        var logger = new FakeLogger<LoggingBehavior<CreateTeam, Guid>>();
        var behavior = new LoggingBehavior<CreateTeam, Guid>(logger);
        Exception refusal = kind switch
        {
            "unauthenticated" => new UnauthenticatedException(),
            "forbidden" => new ForbiddenAccessException(),
            "validation" => new RequestValidationException(new Dictionary<string, string[]> { ["name"] = ["Required."] }),
            "not_found" => new NotFoundException("Player"),
            "conflict" => new ConcurrencyConflictException(new InvalidOperationException("stale")),
            _ => new DomainException("player.name_required", "Enter the name."),
        };

        var send = () => behavior.Handle(_request, _ => throw refusal, TestContext.Current.CancellationToken);

        (await send.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(refusal);
        var records = logger.Collector.GetSnapshot();
        records.Should().NotContain(entry => entry.Level >= LogLevel.Warning);
        var record = records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.StructuredState.Should().Contain(property => property.Key == "RequestName" && property.Value == "CreateTeam");
        record.StructuredState.Should().Contain(property => property.Key == "Reason" && property.Value == reason);
    }

    /// <summary>An outside service being down is worth a warning, but it is not a bug.</summary>
    [Fact]
    public async Task Unavailable_service_is_a_warning()
    {
        var logger = new FakeLogger<LoggingBehavior<CreateTeam, Guid>>();
        var behavior = new LoggingBehavior<CreateTeam, Guid>(logger);

        var send = () => behavior.Handle(_request, _ => throw new ServiceUnavailableException("drive.unavailable", "Drive is down."), TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<ServiceUnavailableException>();
        var record = logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.StructuredState.Should().Contain(property => property.Key == "Reason" && property.Value == "drive.unavailable");
    }

    /// <summary>Request data such as emails never reaches the logs.</summary>
    [Fact]
    public async Task Never_logs_request_data()
    {
        var logger = new FakeLogger<LoggingBehavior<CreateTeam, Guid>>();
        var behavior = new LoggingBehavior<CreateTeam, Guid>(logger);

        await behavior.Handle(_request, _ => Task.FromResult(Guid.NewGuid()), TestContext.Current.CancellationToken);

        foreach (var record in logger.Collector.GetSnapshot())
        {
            record.Message.Should().NotContain("coach@example.com");
            record.StructuredState!.Select(property => property.Value).Should().NotContain(value => value != null && value.Contains("coach@example.com"));
        }
    }
}
