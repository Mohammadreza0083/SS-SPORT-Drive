using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SsSport.Drive.Application;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Infrastructure.BackgroundJobs;

namespace SsSport.Drive.Infrastructure.IntegrationTests.BackgroundJobs;

/// <summary>
/// A queued bot notice (T-6c) travels as its event's type name and JSON. Only domain event types come back out, so a
/// tampered job row cannot make the worker build anything else.
/// </summary>
public sealed class BotNoticeJobTests
{
    /// <summary>A domain event survives the trip through the job's arguments.</summary>
    [Fact]
    public void Domain_events_round_trip()
    {
        var created = new PlayerCreated(Guid.CreateVersion7());

        var back = BotNoticeJob.Deserialize(typeof(PlayerCreated).FullName!, JsonSerializer.Serialize(created));

        back.Should().Be(created);
    }

    /// <summary>
    /// Each container has its own Hangfire storage and activator, not Hangfire's process-wide statics, so two hosts in one
    /// process (the test suite) never take each other's jobs.
    /// </summary>
    [Fact]
    public void Each_container_has_its_own_storage_and_activator()
    {
        using var first = Container("Host=127.0.0.1;Port=1;Database=first");
        using var second = Container("Host=127.0.0.1;Port=1;Database=second");

        first.GetRequiredService<Hangfire.JobStorage>().Should().NotBeSameAs(second.GetRequiredService<Hangfire.JobStorage>());
        first.GetRequiredService<Hangfire.JobActivator>().Should().NotBeSameAs(second.GetRequiredService<Hangfire.JobActivator>());
        first.GetRequiredService<Hangfire.JobActivator>().Should().NotBeSameAs(Hangfire.JobActivator.Current);
    }

    /// <summary>Unknown types and types that are not domain events are refused.</summary>
    /// <param name="typeName">The type name in the job row.</param>
    [Theory]
    [InlineData("System.IO.FileInfo")]
    [InlineData("SsSport.Drive.Domain.Players.Player")]
    [InlineData("SsSport.Drive.Domain.Players.NoSuchEvent")]
    public void Other_types_are_refused(string typeName)
    {
        var read = () => BotNoticeJob.Deserialize(typeName, "{}");

        read.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Builds an Infrastructure container for a database; nothing connects until a job runs.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The container; the caller disposes it.</returns>
    private static ServiceProvider Container(string connectionString) =>
        new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
                .Build())
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();
}
