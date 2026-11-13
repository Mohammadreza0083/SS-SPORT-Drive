using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SsSport.Drive.Application;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Infrastructure.BackgroundJobs;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Ingest;

/// <summary>
/// I-6 wiring: <c>migrate</c> registers the folder-sync round every 10 minutes, and the ingest scheduler puts each step
/// on the queue its worker listens to (light Drive work on <c>ingest</c>, ffmpeg and rendering on <c>media</c>, so a
/// long transcode never holds up a sync).
/// </summary>
public sealed class IngestJobsTests : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The services.</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Starts PostgreSQL and migrates it (which installs Hangfire and registers the recurring jobs).</summary>
    /// <returns>A task that completes when the database is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString() })
            .Build();
        _provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();
        await DatabaseMigrator.MigrateAsync(_provider, TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the services and the container.</summary>
    /// <returns>A task that completes when everything is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>The folder-sync round runs every 10 minutes (decisions §4, I-6).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_folder_sync_round_runs_every_ten_minutes()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT value FROM hangfire.hash WHERE key = 'recurring-job:' || @id AND field = 'Cron'", connection);
        command.Parameters.AddWithValue("id", IngestJobs.FolderSyncRecurringJobId);

        var cron = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        IngestJobs.FolderSyncRecurringJobId.Should().Be("ingest-folder-sync");
        cron.Should().Be("*/10 * * * *");
    }

    /// <summary>Drive steps go to the <c>ingest</c> queue; ffmpeg and image work to the <c>media</c> queue.</summary>
    /// <param name="step">The step.</param>
    /// <param name="queue">The expected queue.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(IngestStep.SyncFolder, "ingest")]
    [InlineData(IngestStep.CopyOriginal, "ingest")]
    [InlineData(IngestStep.ProcessAsset, "media")]
    [InlineData(IngestStep.RenderQrCode, "media")]
    [InlineData(IngestStep.CutOutPhoto, "media")]
    [InlineData(IngestStep.RenderCard, "media")]
    public async Task Each_step_goes_to_its_queue(IngestStep step, string queue)
    {
        var target = Guid.NewGuid();
        await _provider.GetRequiredService<IIngestScheduler>().EnqueueAsync(step, target, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT q.queue FROM hangfire.jobqueue q JOIN hangfire.job j ON j.id = q.jobid WHERE j.arguments::text LIKE '%' || @target || '%'", connection);
        command.Parameters.AddWithValue("target", target.ToString());

        var found = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        IngestJobs.QueueFor(step).Should().Be(queue);
        found.Should().Be(queue);
    }
}
