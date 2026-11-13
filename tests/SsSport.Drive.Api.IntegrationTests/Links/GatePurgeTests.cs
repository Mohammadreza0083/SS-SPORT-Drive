using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Links;

/// <summary>
/// The gate's clean-up (L-2b): a daily job deletes email codes a day after they ran out, and viewer sessions and trusted
/// devices a week after they ran out, in batches, in SQL. Forward records are the audit trail and stay.
/// </summary>
public sealed class GatePurgeTests : IAsyncLifetime
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
                ["Telegram:BotToken"] = "123456:gate-purge-token",
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

    /// <summary>Rows past their retention go; recent rows and every forward record stay.</summary>
    [Fact]
    public async Task Only_rows_past_their_retention_are_deleted()
    {
        var link = Guid.CreateVersion7();
        var email = EmailAddress.Create("marco@lube.it");
        var oldCode = EmailChallenge.Request(link, email, false, _now.AddDays(-2));
        var newCode = EmailChallenge.Request(link, email, false, _now.AddHours(-12));
        var oldSession = ViewerSession.Start("old-session", link, email, false, null, _now.AddDays(-15));
        var newSession = ViewerSession.Start("new-session", link, email, false, null, _now.AddDays(-13));
        var oldDevice = TrustedDevice.Trust("old-device", link, email, false, _now.AddDays(-98));
        var newDevice = TrustedDevice.Trust("new-device", link, email, false, _now.AddDays(-96));
        var forward = LinkForward.Record(link, EmailAddress.Create("scout@other.com"), _now.AddDays(-400));
        await SeedAsync(db =>
        {
            db.AddRange(oldCode, newCode, oldSession, newSession, oldDevice, newDevice, forward);
            return Task.CompletedTask;
        });

        var result = await PurgeAsync();

        result.Should().Be(new GatePurgeResult(EmailCodes: 1, Sessions: 1, Devices: 1));
        (await QueryAsync(db => db.Set<EmailChallenge>().Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken))).Should().Equal(newCode.Id);
        (await QueryAsync(db => db.Set<ViewerSession>().Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken))).Should().Equal(newSession.Id);
        (await QueryAsync(db => db.Set<TrustedDevice>().Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken))).Should().Equal(newDevice.Id);
        (await QueryAsync(db => db.Set<LinkForward>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    /// <summary>A backlog bigger than one batch is cleared in one run.</summary>
    /// <summary>The public site's visitor codes and sign-in sessions (P-8) are cleaned up on the same schedule.</summary>
    [Fact]
    public async Task Public_access_rows_past_their_retention_are_deleted()
    {
        var email = EmailAddress.Create("j.demir@gmail.com");
        var oldCode = AccessCodeChallenge.Request(email, _now.AddDays(-2));
        var newCode = AccessCodeChallenge.Request(email, _now.AddHours(-12));
        var oldSession = AccountSession.Start(SignInAudience.Viewer, Guid.CreateVersion7(), email, "old-account", _now.AddDays(-15));
        var newSession = AccountSession.Start(SignInAudience.Viewer, Guid.CreateVersion7(), email, "new-account", _now.AddDays(-13));
        await SeedAsync(db =>
        {
            db.AddRange(oldCode, newCode, oldSession, newSession);
            return Task.CompletedTask;
        });

        var result = await PurgeAsync();

        result.Should().Be(new GatePurgeResult(EmailCodes: 0, Sessions: 0, Devices: 0, VisitorCodes: 1, AccountSessions: 1));
        (await QueryAsync(db => db.Set<AccessCodeChallenge>().Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken))).Should().Equal(newCode.Id);
        (await QueryAsync(db => db.Set<AccountSession>().Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken))).Should().Equal(newSession.Id);
    }

    /// <inheritdoc/>
    [Fact]
    public async Task A_large_backlog_is_cleared_in_batches()
    {
        var link = Guid.CreateVersion7();
        await SeedAsync(db =>
        {
            for (var i = 0; i < 2_500; i++)
            {
                db.Add(EmailChallenge.Request(link, EmailAddress.Create($"viewer{i}@club.com"), false, _now.AddDays(-3).AddSeconds(i)));
            }

            return Task.CompletedTask;
        });

        var result = await PurgeAsync();

        result.EmailCodes.Should().Be(2_500);
        (await QueryAsync(db => db.Set<EmailChallenge>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    /// <summary>`migrate` registers the daily clean-up job.</summary>
    [Fact]
    public async Task Migrate_registers_the_purge_job()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM hangfire.set WHERE key = 'recurring-jobs' AND value = 'gate-purge'", connection);

        ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().Be(1);
    }

    /// <summary>Runs the purge at <see cref="_now"/>.</summary>
    /// <returns>What it deleted.</returns>
    private async Task<GatePurgeResult> PurgeAsync()
    {
        using var scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PurgeGateRowsCommand(_now), TestContext.Current.CancellationToken);
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
