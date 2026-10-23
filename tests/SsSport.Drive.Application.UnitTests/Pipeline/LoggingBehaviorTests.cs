using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SsSport.Drive.Application.Behaviors;

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
