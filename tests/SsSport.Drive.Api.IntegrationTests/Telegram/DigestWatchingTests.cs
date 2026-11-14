using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Application.Telegram.Digest;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Tracking;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// The digest's watching numbers and failed emails (L-7 part 7b, proposed 2026-11-14, see decisions §7):
/// <list type="bullet">
/// <item>a watch counts on the Tehran day it <b>started</b>: its plays and every second it played (watch time, not
/// unique seconds, the same as L-6) go to that day;</item>
/// <item>minutes are the day's seconds divided by 60 and rounded to the nearest minute, per day and per player;</item>
/// <item>the top players are the three with the most seconds that day, most first (ties by name), each with its own
/// rounded minutes;</item>
/// <item>failed emails are the ones whose last attempt, the one that failed them for good, fell on that day.</item>
/// </list>
/// Everything is read in SQL; no watch is loaded.
/// </summary>
public sealed class DigestWatchingTests : IAsyncLifetime
{
    /// <summary>The admin who gets the digest.</summary>
    private const long AdminTelegramId = 805;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Records what the bot sends.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>Tehran at +03:30, the same as the job's zone.</summary>
    private readonly TimeZoneInfo _tehran = TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromMinutes(210), "Tehran", "Tehran");

    /// <summary>The moment the digest runs: 09:00 Tehran on a fixed day.</summary>
    private readonly DateTimeOffset _now = new(2026, 11, 14, 5, 30, 0, TimeSpan.Zero);

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Gets the day the digest covers.</summary>
    private DigestWindow Window => DigestWindow.Yesterday(_now, _tehran);

    /// <summary>Starts PostgreSQL, boots the API and stores one admin.</summary>
    /// <returns>A task that completes when everything is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:BotToken"] = "123456:digest-watching-token",
                ["Telegram:Mode"] = "Webhook",
                ["Notifications:TimeZone"] = "Asia/Tehran",
            }));
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot)));
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
            .Add(User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), _now));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Plays and minutes add up the watches that started yesterday in Tehran; watches from the day before and from today
    /// are left out. A seek back that plays a stretch again counts both times.
    /// </summary>
    [Fact]
    public async Task Plays_and_minutes_count_the_watches_that_started_yesterday()
    {
        var arash = await AddPlayerAsync("Arash Tehrani");
        var milad = await AddPlayerAsync("Milad Ebadipour");
        var saber = await AddPlayerAsync("Saber Kazemi");
        var amir = await AddPlayerAsync("Amir Ghafour");
        var day = Window.Start;
        await SeedAsync(db =>
        {
            // Arash: 600 s played twice over (a second play after the end) + 300 s by another viewer = 900 s, 3 plays.
            var replayed = Watch(arash, "marco@lube.it", day.AddHours(2), (0, 600));
            replayed.RecordEvent(PlaybackEventType.Ended, 600, day.AddHours(2).AddMinutes(11));
            replayed.RecordEvent(PlaybackEventType.Play, 0, day.AddHours(2).AddMinutes(12));
            db.AddRange(replayed, Watch(arash, "scout@halkbank.com.tr", day.AddHours(5), (0, 300)));

            // Milad: 0–360 s, then a seek back and 0–360 s again = 720 s of watch time (360 unique).
            db.Add(Watch(milad, "marco@lube.it", day.AddHours(8), (0, 360), (0, 360)));

            // Saber 240 s, Amir 100 s (1.67 min, rounds to 2).
            db.AddRange(Watch(saber, "marco@lube.it", day.AddHours(20), (0, 240)), Watch(amir, "marco@lube.it", day.AddHours(23).AddMinutes(59), (0, 100)));

            // Left out: one the day before and one today.
            db.AddRange(Watch(arash, "marco@lube.it", day.AddMinutes(-1), (0, 1200)), Watch(milad, "marco@lube.it", Window.End, (0, 1200)));
            return Task.CompletedTask;
        });

        var facts = await ReadFactsAsync();

        // 900 + 720 + 240 + 100 = 1960 s = 32.67 min.
        facts.Plays.Should().Be(6);
        facts.WatchMinutes.Should().Be(33);
        facts.HasActivity.Should().BeTrue("plays alone are activity, even with no link opened");
    }

    /// <summary>The top three players by seconds watched yesterday, most first, each with their own rounded minutes.</summary>
    [Fact]
    public async Task The_top_three_players_are_the_most_watched_yesterday()
    {
        var arash = await AddPlayerAsync("Arash Tehrani");
        var milad = await AddPlayerAsync("Milad Ebadipour");
        var saber = await AddPlayerAsync("Saber Kazemi");
        var amir = await AddPlayerAsync("Amir Ghafour");
        var day = Window.Start;
        await SeedAsync(db =>
        {
            db.AddRange(
                Watch(amir, "marco@lube.it", day.AddHours(1), (0, 100)),
                Watch(saber, "marco@lube.it", day.AddHours(2), (0, 240)),
                Watch(milad, "marco@lube.it", day.AddHours(3), (0, 360), (0, 360)),
                Watch(arash, "marco@lube.it", day.AddHours(4), (0, 600)),
                Watch(arash, "scout@halkbank.com.tr", day.AddHours(5), (0, 300)),

                // Amir's big watch today does not lift him into yesterday's top three.
                Watch(amir, "marco@lube.it", Window.End.AddMinutes(5), (0, 3000)));
            return Task.CompletedTask;
        });

        var facts = await ReadFactsAsync();

        facts.TopPlayers.Should().Equal(
            new DigestTopPlayer("Arash Tehrani", 15),
            new DigestTopPlayer("Milad Ebadipour", 12),
            new DigestTopPlayer("Saber Kazemi", 4));
    }

    /// <summary>Two players with the same seconds are ordered by name, so the digest never changes between runs.</summary>
    [Fact]
    public async Task Players_with_equal_minutes_are_ordered_by_name()
    {
        var zahra = await AddPlayerAsync("Zahra Moradi");
        var bita = await AddPlayerAsync("Bita Karimi");
        var day = Window.Start;
        await SeedAsync(db =>
        {
            db.AddRange(Watch(zahra, "marco@lube.it", day.AddHours(1), (0, 120)), Watch(bita, "marco@lube.it", day.AddHours(2), (0, 120)));
            return Task.CompletedTask;
        });

        var facts = await ReadFactsAsync();

        facts.TopPlayers.Should().Equal(new DigestTopPlayer("Bita Karimi", 2), new DigestTopPlayer("Zahra Moradi", 2));
    }

    /// <summary>
    /// Failed emails are the ones that failed for good yesterday. A sent email, one still being retried, and one that
    /// failed today are not counted.
    /// </summary>
    [Fact]
    public async Task Failed_emails_count_the_ones_that_failed_for_good_yesterday()
    {
        var day = Window.Start;
        await SeedAsync(db =>
        {
            db.AddRange(
                Failed(day.AddHours(3)),
                Failed(day.AddHours(22)),
                Failed(Window.End.AddHours(1)),
                Sent(day.AddHours(4)),
                Retrying(day.AddHours(6)));
            return Task.CompletedTask;
        });

        var facts = await ReadFactsAsync();

        facts.FailedEmails.Should().Be(2);
    }

    /// <summary>End to end: the digest the admin reads carries the plays, minutes, top players and failed emails.</summary>
    [Fact]
    public async Task The_digest_shows_the_watching_lines()
    {
        var arash = await AddPlayerAsync("Arash Tehrani");
        var day = Window.Start;
        await SeedAsync(db =>
        {
            db.AddRange(Watch(arash, "marco@lube.it", day.AddHours(2), (0, 600)), Failed(day.AddHours(3)));
            return Task.CompletedTask;
        });

        using (var scope = _api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SendDailyDigestCommand(_now), TestContext.Current.CancellationToken);
        }

        var digest = _bot.ToChat(AdminTelegramId).Single(call => call.Text!.StartsWith("📊 Daily digest", StringComparison.Ordinal));
        digest.Text!.Split('\n').Should().Equal(
            "📊 Daily digest · Fri 13 Nov 2026",
            "Yesterday: 0 links opened · 0 viewers · 1 play · 10 min watched",
            "Top players: Arash Tehrani 10 min",
            "Waiting on you: 1 player on the waiting list",
            "⚠️ 1 email failed after every retry");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A watch started at <paramref name="startedAt"/> that played the given ranges, one heartbeat per 30 s or less.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="email">The viewer.</param>
    /// <param name="startedAt">When the watch started (UTC).</param>
    /// <param name="ranges">The ranges played, in order.</param>
    /// <returns>The watch.</returns>
    private static WatchSession Watch(Guid playerId, string email, DateTimeOffset startedAt, params (double From, double To)[] ranges)
    {
        var watch = WatchSession.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), playerId, Guid.CreateVersion7(), EmailAddress.Create(email), 6000, startedAt);
        var now = startedAt;
        foreach (var (from, to) in ranges)
        {
            for (var at = from; at < to; at += WatchSession.MaxHeartbeatSeconds)
            {
                now = now.AddSeconds(10);
                watch.Heartbeat(at, Math.Min(at + WatchSession.MaxHeartbeatSeconds, to), now).Should().BeTrue();
            }
        }

        return watch;
    }

    /// <summary>An email whose every attempt failed, the last at <paramref name="failedAt"/>.</summary>
    /// <param name="failedAt">When it failed for good (UTC).</param>
    /// <returns>The email.</returns>
    private static OutgoingEmail Failed(DateTimeOffset failedAt)
    {
        var email = OutgoingEmail.Queue(EmailKind.General, "forward.kept", EmailAddress.Create("marco@lube.it"), null, failedAt.AddMinutes(-30));
        for (var attempt = 0; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            email.RecordFailure("SmtpException", failedAt);
        }

        email.Status.Should().Be(EmailStatus.Failed);
        return email;
    }

    /// <summary>An email a provider took.</summary>
    /// <param name="sentAt">When it was sent (UTC).</param>
    /// <returns>The email.</returns>
    private static OutgoingEmail Sent(DateTimeOffset sentAt)
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", EmailAddress.Create("marco@lube.it"), null, sentAt.AddSeconds(-5));
        email.RecordSent("resend", sentAt);
        return email;
    }

    /// <summary>An email that failed once and waits for a retry.</summary>
    /// <param name="failedAt">When the attempt failed (UTC).</param>
    /// <returns>The email.</returns>
    private static OutgoingEmail Retrying(DateTimeOffset failedAt)
    {
        var email = OutgoingEmail.Queue(EmailKind.General, "forward.kept", EmailAddress.Create("marco@lube.it"), null, failedAt.AddSeconds(-5));
        email.RecordFailure("SmtpException", failedAt);
        return email;
    }

    /// <summary>Stores a player on the waiting list with a name.</summary>
    /// <param name="name">The player's name.</param>
    /// <returns>The player's id.</returns>
    private async Task<Guid> AddPlayerAsync(string name)
    {
        var player = Player.Create(PlayerCategory.Men, _now.AddDays(-30));
        var fullName = FullName.Create(name);
        player.Rename(fullName, Slug.FromName(fullName));
        await SeedAsync(db =>
        {
            db.Add(player);
            return Task.CompletedTask;
        });
        return player.Id;
    }

    /// <summary>Reads the digest's facts for yesterday the way the job does.</summary>
    /// <returns>The facts.</returns>
    private async Task<DigestFacts> ReadFactsAsync()
    {
        using var scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDigestFactsReader>().ReadAsync(Window, TestContext.Current.CancellationToken);
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
}
