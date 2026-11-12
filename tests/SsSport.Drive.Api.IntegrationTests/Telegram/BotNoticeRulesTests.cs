using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// Notification rules (T-8): a recipient gets at most one bot notice message every 10 minutes, nothing between 23:00
/// and 08:00 Tehran time, and the notices held meanwhile arrive together in one grouped message.
/// </summary>
public sealed class BotNoticeRulesTests : IAsyncLifetime
{
    /// <summary>The analyst who owns the player.</summary>
    private const long OwnerTelegramId = 1101;

    /// <summary>Another analyst, who adds folders to the owner's player.</summary>
    private const long OtherTelegramId = 1102;

    /// <summary>An admin, who publishes.</summary>
    private const long AdminTelegramId = 1103;

    /// <summary>Text that only the folder notice contains.</summary>
    private const string FolderNotice = "added a Drive folder";

    /// <summary>Tehran is UTC+3:30.</summary>
    private static readonly TimeSpan _tehran = TimeSpan.FromHours(3.5);

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Records the bot's messages.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>The clock the API reads.</summary>
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, _tehran));

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>The other analyst's user id.</summary>
    private Guid _otherId;

    /// <summary>The admin's user id.</summary>
    private Guid _adminId;

    /// <summary>The owner's user id.</summary>
    private Guid _ownerId;

    /// <summary>The owner's player.</summary>
    private Guid _playerId;

    /// <summary>Starts PostgreSQL, boots the API and stores the team and a player.</summary>
    /// <returns>A task that completes when everything is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:BotToken"] = "123456:notice-rules-token",
                ["Telegram:Mode"] = "Webhook",
                // The test process turns quiet hours off; this class tests the owner's rules.
                ["Notifications:QuietHoursStart"] = "23:00",
                ["Notifications:QuietHoursEnd"] = "08:00",
                ["Notifications:TimeZone"] = "Asia/Tehran",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.UseInlineBotNotices();
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var owner = Analyst(OwnerTelegramId);
        var other = Analyst(OtherTelegramId);
        var admin = User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), _clock.GetUtcNow());
        users.Add(owner);
        users.Add(other);
        users.Add(admin);
        (_ownerId, _otherId, _adminId) = (owner.Id, other.Id, admin.Id);

        var player = Player.Create(PlayerCategory.Men, _clock.GetUtcNow());
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));
        player.AssignAnalyst(owner.Id);
        scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().Add(player);
        _playerId = player.Id;
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        _bot.Calls.Clear();
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>The first notice goes out at once; the next ones wait 10 minutes and arrive as one grouped message.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Notices_within_ten_minutes_are_grouped()
    {
        await AddFolderByOtherAsync("SS-SPORT first");
        FolderNotices().Should().ContainSingle().Which.Text.Should().Contain("SS-SPORT first");

        _clock.Advance(TimeSpan.FromMinutes(2));
        await AddFolderByOtherAsync("SS-SPORT second");
        await AddFolderByOtherAsync("SS-SPORT third");
        FolderNotices().Should().HaveCount(1, "the 10 minutes are still running");

        _clock.Advance(TimeSpan.FromMinutes(3));
        await SendDueAsync();
        FolderNotices().Should().HaveCount(1, "5 minutes have passed, not 10");

        _clock.Advance(TimeSpan.FromMinutes(5));
        await SendDueAsync();
        var grouped = FolderNotices().Should().HaveCount(2).And.Subject.Last();
        grouped.Text.Should().Contain("SS-SPORT second").And.Contain("SS-SPORT third").And.Contain("2");

        await SendDueAsync();
        FolderNotices().Should().HaveCount(2, "nothing is sent twice");
    }

    /// <summary>Notices in the quiet hours wait until 08:00 Tehran time.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Quiet_hours_hold_notices_until_eight()
    {
        _clock.Set(new DateTimeOffset(2026, 11, 12, 23, 30, 0, _tehran));
        await AddFolderByOtherAsync("SS-SPORT night");
        FolderNotices().Should().BeEmpty();

        _clock.Set(new DateTimeOffset(2026, 11, 13, 7, 59, 0, _tehran));
        await SendDueAsync();
        FolderNotices().Should().BeEmpty();

        _clock.Set(new DateTimeOffset(2026, 11, 13, 8, 0, 0, _tehran));
        await SendDueAsync();
        FolderNotices().Should().ContainSingle().Which.Text.Should().Contain("SS-SPORT night");
    }

    /// <summary>Publishing a player tells its analyst (through the same rules).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Publishing_notifies_the_analyst()
    {
        await RunAsync(new PlayerPublished(_playerId), _adminId);

        _bot.ToChat(OwnerTelegramId).Should().ContainSingle(call => call.Kind == "Send")
            .Which.Text.Should().Contain("Arash Tehrani").And.Contain("published");
    }

    /// <summary>Held notices of someone who left the team are dropped.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Held_notices_of_a_removed_member_are_dropped()
    {
        await AddFolderByOtherAsync("SS-SPORT first");
        _clock.Advance(TimeSpan.FromMinutes(1));
        await AddFolderByOtherAsync("SS-SPORT held");

        using (var scope = _api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
            var owner = await users.GetByIdAsync(_ownerId, TestContext.Current.CancellationToken);
            owner!.Remove();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _clock.Advance(TimeSpan.FromMinutes(10));
        await SendDueAsync();

        FolderNotices().Should().ContainSingle("only the first one went out");
        (await ScalarAsync("SELECT count(*) FROM pending_notices")).Should().Be(0);
    }

    /// <summary>When Telegram is down the notices are kept and sent on a later round.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Telegram_outage_keeps_the_notices()
    {
        _bot.FailSends = true;
        await AddFolderByOtherAsync("SS-SPORT kept");
        _bot.FailSends = false;
        FolderNotices().Should().BeEmpty();

        _clock.Advance(TimeSpan.FromMinutes(10));
        await SendDueAsync();

        FolderNotices().Should().ContainSingle().Which.Text.Should().Contain("SS-SPORT kept");
    }

    /// <summary>Creates an approved analyst.</summary>
    /// <param name="telegramId">Their Telegram id.</param>
    /// <returns>The user.</returns>
    private User Analyst(long telegramId)
    {
        var analyst = User.RequestAccess(telegramId, $"analyst_{telegramId}", $"Analyst {telegramId}", _clock.GetUtcNow());
        analyst.Approve(Role.Analyst);
        return analyst;
    }

    /// <summary>Links a folder to the owner's player and runs its notices as the other analyst.</summary>
    /// <param name="name">The folder name.</param>
    /// <returns>A task that completes when the notices ran.</returns>
    private async Task AddFolderByOtherAsync(string name)
    {
        Guid folderId;
        using (var scope = _api.Services.CreateScope())
        {
            var folder = DriveFolder.Link(_playerId, $"drive_{Guid.NewGuid():N}", name, _clock.GetUtcNow());
            scope.ServiceProvider.GetRequiredService<IRepository<DriveFolder, Guid>>().Add(folder);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
            folderId = folder.Id;
        }

        await RunAsync(new DriveFolderLinked(folderId, _playerId), _otherId);
    }

    /// <summary>Runs an event's background handlers, as the notice job does.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="actorId">Who caused it.</param>
    /// <returns>A task that completes when the handlers ran.</returns>
    private async Task RunAsync(IDomainEvent domainEvent, Guid actorId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBackgroundEventRunner>()
            .RunAsync(domainEvent, new EventContext(actorId), TestContext.Current.CancellationToken);
    }

    /// <summary>Runs one round of the recurring job that sends the due notices.</summary>
    /// <returns>A task that completes when the round is done.</returns>
    private async Task SendDueAsync()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SendDueNoticesCommand(), TestContext.Current.CancellationToken);
    }

    /// <summary>Gets the messages in the owner's chat that carry folder notices.</summary>
    /// <returns>The messages, in order.</returns>
    private List<BotCall> FolderNotices() =>
        _bot.ToChat(OwnerTelegramId).Where(call => call.Kind == "Send" && call.Text!.Contains(FolderNotice, StringComparison.Ordinal)).ToList();

    /// <summary>Runs a count query.</summary>
    /// <param name="sql">The query.</param>
    /// <returns>The count.</returns>
    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

/// <summary>A clock the test moves by hand.</summary>
/// <param name="start">The time it starts at.</param>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    /// <summary>The current time.</summary>
    private DateTimeOffset _now = start.ToUniversalTime();

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;

    /// <summary>Sets the clock.</summary>
    /// <param name="at">The new time.</param>
    public void Set(DateTimeOffset at) => _now = at.ToUniversalTime();
}
