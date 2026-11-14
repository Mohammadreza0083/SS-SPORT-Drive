using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Retention;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Tracking;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Links;

/// <summary>
/// Retention of the email queue, the raw player events and the sign-in links (W-3, owner-approved 2026-11-14). A daily
/// Hangfire job (<c>retention-purge</c>, 00:30 UTC) deletes, in SQL batches of 1,000 (at most 100,000 rows per table per
/// run):
/// <list type="bullet">
/// <item>sent and skipped emails 30 days after they were queued;</item>
/// <item>failed emails 90 days after they were queued; an email still queued is never deleted;</item>
/// <item>player events (<c>tracking_events</c>) one year after they happened; the watches themselves
/// (<c>watch_sessions</c>, where every metric comes from) are never deleted;</item>
/// <item>sign-in links one day after they expire.</item>
/// </list>
/// Signed-in sessions (<c>account_sessions</c>) are already cleaned up by <c>gate-purge</c> (P-8).
/// </summary>
public sealed class RetentionPurgeTests : IAsyncLifetime
{
    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The moment the purge runs.</summary>
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Starts PostgreSQL and boots the API.</summary>
    /// <returns>A task that completes when everything is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:BotToken"] = "123456:retention-purge-token",
                ["Telegram:Mode"] = "Webhook",
            }));
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger())));
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>The retention periods are the owner's (2026-11-14).</summary>
    [Fact]
    public void The_periods_are_the_owners()
    {
        RetentionPeriods.SentEmails.Should().Be(TimeSpan.FromDays(30));
        RetentionPeriods.FailedEmails.Should().Be(TimeSpan.FromDays(90));
        RetentionPeriods.TrackingEvents.Should().Be(TimeSpan.FromDays(365));
        RetentionPeriods.SignInLinks.Should().Be(TimeSpan.FromDays(1));
    }

    /// <summary>
    /// Sent and skipped emails go 30 days after they were queued, failed ones after 90 days. An email still queued
    /// stays however old it is: it has not been sent yet.
    /// </summary>
    [Fact]
    public async Task Emails_past_their_retention_are_deleted()
    {
        var oldSent = Sent(_now.AddDays(-31));
        var newSent = Sent(_now.AddDays(-29));
        var oldSkipped = Skipped(_now.AddDays(-31));
        var oldFailed = Failed(_now.AddDays(-91));
        var newFailed = Failed(_now.AddDays(-60));
        var oldQueued = Email(_now.AddDays(-200));
        await SeedAsync(db =>
        {
            db.AddRange(oldSent, newSent, oldSkipped, oldFailed, newFailed, oldQueued);
            return Task.CompletedTask;
        });

        var result = await PurgeAsync();

        result.Should().Be(new RetentionPurgeResult(SentEmails: 2, FailedEmails: 1, TrackingEvents: 0, SignInLinks: 0));
        (await QueryAsync(db => db.Set<OutgoingEmail>().OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken)))
            .Should().BeEquivalentTo([newSent.Id, newFailed.Id, oldQueued.Id]);
    }

    /// <summary>Player events go after a year; the watch they belong to, and its metrics, stay.</summary>
    [Fact]
    public async Task Player_events_past_a_year_are_deleted_and_the_watch_stays()
    {
        var watch = WatchSession.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), EmailAddress.Create("marco@lube.it"), 600, _now.AddDays(-400));
        watch.Heartbeat(0, 10, _now.AddDays(-400)).Should().BeTrue();
        var oldEvent = TrackingEvent.Record(watch.Id, PlaybackEventType.Pause, 10, _now.AddDays(-366));
        var newEvent = TrackingEvent.Record(watch.Id, PlaybackEventType.Play, 10, _now.AddDays(-364));
        await SeedAsync(db =>
        {
            db.AddRange(watch, oldEvent, newEvent);
            return Task.CompletedTask;
        });

        var result = await PurgeAsync();

        result.TrackingEvents.Should().Be(1);
        (await QueryAsync(db => db.Set<TrackingEvent>().Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken))).Should().Equal(newEvent.Id);
        (await QueryAsync(db => db.Set<WatchSession>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    /// <summary>Sign-in links go a day after they expire, used or not.</summary>
    [Fact]
    public async Task Sign_in_links_past_their_retention_are_deleted()
    {
        var email = EmailAddress.Create("owner@ss-sport.co");

        // A link lives 15 minutes, so one issued 25 hours ago expired more than a day ago.
        var oldLink = Link(SignInAudience.Admin, email, "old-hash", _now.AddHours(-25));
        var usedOldLink = Link(SignInAudience.Viewer, email, "used-hash", _now.AddHours(-26));
        usedOldLink.Redeem(_now.AddHours(-26).AddMinutes(1)).Should().BeTrue();
        var newLink = Link(SignInAudience.Admin, email, "new-hash", _now.AddHours(-23));
        await SeedAsync(db =>
        {
            db.AddRange(oldLink, usedOldLink, newLink);
            return Task.CompletedTask;
        });

        var result = await PurgeAsync();

        result.SignInLinks.Should().Be(2);
        (await QueryAsync(db => db.Set<SignInLink>().Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken))).Should().Equal(newLink.Id);
    }

    /// <summary>A backlog bigger than one batch is cleared in one run.</summary>
    [Fact]
    public async Task A_large_backlog_is_cleared_in_batches()
    {
        var watchId = Guid.CreateVersion7();
        await SeedAsync(db =>
        {
            for (var i = 0; i < 2_500; i++)
            {
                db.Add(TrackingEvent.Record(watchId, PlaybackEventType.Seek, i, _now.AddDays(-400).AddSeconds(i)));
            }

            return Task.CompletedTask;
        });

        var result = await PurgeAsync();

        result.TrackingEvents.Should().Be(2_500);
        (await QueryAsync(db => db.Set<TrackingEvent>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    /// <summary>`migrate` registers the daily retention job.</summary>
    [Fact]
    public async Task Migrate_registers_the_retention_job()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM hangfire.set WHERE key = 'recurring-jobs' AND value = 'retention-purge'", connection);

        ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().Be(1);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>An email queued at <paramref name="queuedAt"/> and still waiting.</summary>
    /// <param name="queuedAt">When it was queued (UTC).</param>
    /// <returns>The email.</returns>
    private static OutgoingEmail Email(DateTimeOffset queuedAt) =>
        OutgoingEmail.Queue(EmailKind.General, "forward.kept", EmailAddress.Create("marco@lube.it"), null, queuedAt);

    /// <summary>An email a provider took a minute after it was queued.</summary>
    /// <param name="queuedAt">When it was queued (UTC).</param>
    /// <returns>The email.</returns>
    private static OutgoingEmail Sent(DateTimeOffset queuedAt)
    {
        var email = Email(queuedAt);
        email.RecordSent("smtp", queuedAt.AddMinutes(1));
        return email;
    }

    /// <summary>An email closed with nothing to send.</summary>
    /// <param name="queuedAt">When it was queued (UTC).</param>
    /// <returns>The email.</returns>
    private static OutgoingEmail Skipped(DateTimeOffset queuedAt)
    {
        var email = Email(queuedAt);
        email.Skip(queuedAt.AddMinutes(1));
        return email;
    }

    /// <summary>An email whose every attempt failed within the hour after it was queued.</summary>
    /// <param name="queuedAt">When it was queued (UTC).</param>
    /// <returns>The email.</returns>
    private static OutgoingEmail Failed(DateTimeOffset queuedAt)
    {
        var email = Email(queuedAt);
        for (var attempt = 1; attempt <= OutgoingEmail.MaxAttempts; attempt++)
        {
            email.RecordFailure("SmtpException", queuedAt.AddMinutes(attempt * 10));
        }

        return email;
    }

    /// <summary>A sign-in link issued and armed (its 15 minutes started) at <paramref name="issuedAt"/>.</summary>
    /// <param name="audience">Admin or viewer.</param>
    /// <param name="email">The address it was sent to.</param>
    /// <param name="tokenHash">A distinct stand-in for the token's keyed hash.</param>
    /// <param name="issuedAt">When it was issued (UTC).</param>
    /// <returns>The link.</returns>
    private static SignInLink Link(SignInAudience audience, EmailAddress email, string tokenHash, DateTimeOffset issuedAt)
    {
        var link = SignInLink.Issue(audience, email, issuedAt);
        link.Arm(tokenHash, issuedAt).Should().BeTrue();
        return link;
    }

    /// <summary>Runs the purge as the daily job does.</summary>
    /// <returns>What it deleted.</returns>
    private async Task<RetentionPurgeResult> PurgeAsync()
    {
        using var scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PurgeRetainedRowsCommand(_now), TestContext.Current.CancellationToken);
    }

    /// <summary>Adds rows straight to the database.</summary>
    /// <param name="seed">Adds the rows.</param>
    /// <returns>A task that completes when they are saved.</returns>
    private async Task SeedAsync(Func<AppDbContext, Task> seed)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Runs a read against the database.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="query">The query.</param>
    /// <returns>The result.</returns>
    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
