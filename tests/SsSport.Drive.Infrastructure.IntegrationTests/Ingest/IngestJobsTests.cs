using Hangfire;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using SsSport.Drive.Application;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Infrastructure.BackgroundJobs;
using SsSport.Drive.Infrastructure.Media;
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
    [InlineData(IngestStep.ScanArchive, "ingest")]
    public async Task Each_step_goes_to_its_queue(IngestStep step, string queue)
    {
        var target = Guid.NewGuid();
        await _provider.GetRequiredService<IIngestScheduler>().EnqueueAsync(step, target, TestContext.Current.CancellationToken);

        IngestJobs.QueueFor(step).Should().Be(queue);
        (await QueueOfAsync(target)).Should().Be(queue);
    }

    /// <summary>
    /// M-4: an archive player's copy and processing go to their own queues (<c>ingest-archive</c>, <c>media-archive</c>), served
    /// by their own throttled workers; the team's files stay on <c>ingest</c> and <c>media</c>.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="queue">The expected queue.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(IngestStep.CopyOriginal, IngestLane.Live, "ingest")]
    [InlineData(IngestStep.ProcessAsset, IngestLane.Live, "media")]
    [InlineData(IngestStep.CopyOriginal, IngestLane.Archive, "ingest-archive")]
    [InlineData(IngestStep.ProcessAsset, IngestLane.Archive, "media-archive")]
    public async Task Asset_steps_go_to_their_lane(IngestStep step, IngestLane lane, string queue)
    {
        var target = Guid.NewGuid();
        await _provider.GetRequiredService<IIngestScheduler>().EnqueueAssetAsync(step, target, lane, TestContext.Current.CancellationToken);

        (await QueueOfAsync(target)).Should().Be(queue);
    }

    /// <summary>
    /// M-4, new uploads first: while a team file waits on the <c>media</c> queue, an archive media job does not run; it puts
    /// itself back a minute later, so the free worker takes the team's file.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_archive_job_yields_while_team_files_wait()
    {
        await _provider.GetRequiredService<IIngestScheduler>().EnqueueAssetAsync(IngestStep.ProcessAsset, Guid.NewGuid(), IngestLane.Live, TestContext.Current.CancellationToken);
        var sender = Substitute.For<ISender>();
        var archive = Guid.NewGuid();

        await Job(sender).RunArchiveMediaStepAsync(IngestStep.ProcessAsset, archive, TestContext.Current.CancellationToken);

        sender.ReceivedCalls().Should().BeEmpty();
        (await StateOfAsync(archive)).Should().Be("Scheduled");
    }

    /// <summary>With no team file waiting, an archive media job runs its step.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_archive_job_runs_when_no_team_file_waits()
    {
        var sender = Substitute.For<ISender>();
        var archive = Guid.NewGuid();

        await Job(sender).RunArchiveMediaStepAsync(IngestStep.ProcessAsset, archive, TestContext.Current.CancellationToken);

        await sender.Received(1).Send(new ProcessAssetCommand(archive), Arg.Any<CancellationToken>());
    }

    /// <summary>An archive copy job tells the copy it runs on the archive lane, so the processing follows it there.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_archive_copy_keeps_its_lane()
    {
        var sender = Substitute.For<ISender>();
        var archive = Guid.NewGuid();

        await Job(sender).RunArchiveDriveStepAsync(IngestStep.CopyOriginal, archive, TestContext.Current.CancellationToken);

        await sender.Received(1).Send(new CopyOriginalCommand(archive, IngestLane.Archive), Arg.Any<CancellationToken>());
    }

    /// <summary>The archive lane's media workers (<c>Media__ArchiveWorkers</c>) default to 1.</summary>
    [Fact]
    public void Archive_workers_default_to_one() => new MediaOptions().ArchiveWorkers.Should().Be(1);

    /// <summary>Builds the job over the real Hangfire storage, without the commit grace.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <returns>The job.</returns>
    private IngestJob Job(ISender sender) =>
        new(sender, _provider.GetRequiredService<IBackgroundJobClient>(), _provider.GetRequiredService<JobStorage>(), NullLogger<IngestJob>.Instance) { CommitGrace = TimeSpan.Zero };

    /// <summary>Reads the queue of the job whose arguments name a target.</summary>
    /// <param name="target">The target id.</param>
    /// <returns>The queue, or <see langword="null"/>.</returns>
    private async Task<string?> QueueOfAsync(Guid target)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT q.queue FROM hangfire.jobqueue q JOIN hangfire.job j ON j.id = q.jobid WHERE j.arguments::text LIKE '%' || @target || '%'", connection);
        command.Parameters.AddWithValue("target", target.ToString());
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Reads the state of the job whose arguments name a target.</summary>
    /// <param name="target">The target id.</param>
    /// <returns>The state name, or <see langword="null"/>.</returns>
    private async Task<string?> StateOfAsync(Guid target)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT statename FROM hangfire.job WHERE arguments::text LIKE '%' || @target || '%'", connection);
        command.Parameters.AddWithValue("target", target.ToString());
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
