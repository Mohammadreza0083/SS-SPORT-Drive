using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// Bot notices leave the request (T-6c): the change is saved and answered at once, and the notice is sent afterwards by
/// a Hangfire job on PostgreSQL, run by the Hangfire server inside the API. This class uses the real queue end to end.
/// </summary>
public sealed class BotNoticeQueueTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:notice-queue-token";

    /// <summary>The analyst who creates the players.</summary>
    private const long OwnerTelegramId = 1001;

    /// <summary>Another analyst.</summary>
    private const long OtherTelegramId = 1002;

    /// <summary>How long a background notice may take to arrive in these tests.</summary>
    private static readonly TimeSpan _arrival = TimeSpan.FromSeconds(30);

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Stands in for Google Drive.</summary>
    private readonly FakeDriveFolderReader _drive = new();

    /// <summary>Records the bot's messages.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Starts PostgreSQL, boots the API (with its Hangfire server) and stores two analysts.</summary>
    /// <returns>A task that completes when everything is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:BotToken"] = BotToken,
                ["Telegram:Mode"] = "Webhook",
                // The test project turns the server off for every host; this class tests the real queue.
                ["BackgroundJobs:RunServer"] = "true",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.Replace(ServiceDescriptor.Singleton<IDriveFolderReader>(_drive));
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        foreach (var telegramId in new[] { OwnerTelegramId, OtherTelegramId })
        {
            var analyst = User.RequestAccess(telegramId, $"analyst_{telegramId}", $"Analyst {telegramId}", DateTimeOffset.UtcNow);
            analyst.Approve(Role.Analyst);
            users.Add(analyst);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>`migrate` installs Hangfire's tables next to ours; the API never changes the schema on start-up.</summary>
    [Fact]
    public async Task Migrate_installs_the_hangfire_schema()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('hangfire.job') IS NOT NULL", connection);

        ((bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().BeTrue();
    }

    /// <summary>`migrate` registers the recurring job that sends held notices once they are due (T-8).</summary>
    [Fact]
    public async Task Migrate_registers_the_due_notices_job()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM hangfire.set WHERE key = 'recurring-jobs' AND value = 'bot-notices-due'", connection);

        ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().Be(1);
    }

    /// <summary>
    /// With Telegram taking 5 s per message, creating a player still answers at once; the progress message arrives
    /// afterwards from the queue.
    /// </summary>
    [Fact]
    public async Task Request_does_not_wait_for_telegram()
    {
        _bot.SendDelay = TimeSpan.FromSeconds(5);

        var timer = Stopwatch.StartNew();
        var (_, name) = await CreatePlayerAsync(OwnerTelegramId, 1);
        timer.Stop();

        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "the request no longer waits for Telegram");
        var progress = await ArrivalAsync(call => call.Kind == "Send" && call.ChatId == OwnerTelegramId && call.Text!.Contains(name));
        progress.Text.Should().Contain("Links verified");
    }

    /// <summary>The queued notice still knows who caused it: the owner is told which analyst added a folder.</summary>
    [Fact]
    public async Task Queued_notice_keeps_the_actor()
    {
        var (id, name) = await CreatePlayerAsync(OwnerTelegramId, 2);

        using var response = await SendAsync(OtherTelegramId, HttpMethod.Post, $"/miniapp/players/{id}/folders", new { driveFolderUrl = "queuedFolder_0002" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var notice = await ArrivalAsync(call => call.ChatId == OwnerTelegramId && call.Text!.Contains("added a Drive folder"));
        notice.Text.Should().Contain(name).And.Contain($"Analyst {OtherTelegramId}");
    }

    /// <summary>When Telegram is down the change is still saved and answered; the notice failure is only logged.</summary>
    [Fact]
    public async Task Telegram_failure_never_fails_the_request()
    {
        _bot.FailSends = true;

        var (id, _) = await CreatePlayerAsync(OwnerTelegramId, 3);

        id.Should().NotBeEmpty();
    }

    /// <summary>
    /// Waits for a background notice. When it does not come, the failure lists Hangfire's jobs with their states and
    /// reasons, so a timing problem can be told apart from a failing or stuck job.
    /// </summary>
    /// <param name="match">The notice to wait for.</param>
    /// <returns>The notice.</returns>
    private async Task<BotCall> ArrivalAsync(Func<BotCall, bool> match)
    {
        try
        {
            return await _bot.WaitForAsync(match, _arrival);
        }
        catch (TimeoutException exception)
        {
            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT j.id, j.statename, j.createdat, string_agg(s.name || ': ' || coalesce(s.reason, '') || ' ' || coalesce(left(s.data::text, 400), ''), ' | ' ORDER BY s.id)
                FROM hangfire.job j LEFT JOIN hangfire.state s ON s.jobid = j.id
                GROUP BY j.id, j.statename, j.createdat ORDER BY j.id
                """,
                connection);
            var jobs = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    jobs.Add($"job {reader.GetInt64(0)} {reader.GetString(1)} at {reader.GetDateTime(2):O}: {reader.GetValue(3)}");
                }
            }

            await using var servers = new NpgsqlCommand("SELECT count(*) FROM hangfire.server", connection);
            var serverCount = await servers.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            var calls = string.Join(" | ", _bot.Calls.Select(call => $"{call.Kind} to {call.ChatId}"));
            throw new TimeoutException(
                $"{exception.Message} Hangfire servers: {serverCount}. Jobs: {(jobs.Count == 0 ? "none" : string.Join(" || ", jobs))}. Bot calls: {(calls.Length == 0 ? "none" : calls)}.",
                exception);
        }
    }

    /// <summary>Creates a player through the Mini App as an analyst.</summary>
    /// <param name="telegramId">The analyst.</param>
    /// <param name="n">A number unique in this class.</param>
    /// <returns>The player id and name.</returns>
    private async Task<(Guid Id, string Name)> CreatePlayerAsync(long telegramId, int n)
    {
        var name = $"Queued Player {(char)('A' + n)}";
        using var response = await SendAsync(telegramId, HttpMethod.Post, "/miniapp/players", new
        {
            category = "Men",
            name,
            nationalityCode = "IR",
            positions = new[] { "OH" },
            volleyboxUrl = $"https://volleybox.net/queued-player-p{70000 + n}",
            driveFolderUrl = $"https://drive.google.com/drive/folders/queuedPlayer_{n:D4}",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), name);
    }

    /// <summary>Calls the API as a team member.</summary>
    /// <param name="telegramId">Who calls.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> SendAsync(long telegramId, HttpMethod method, string path, object? body = null)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, telegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
