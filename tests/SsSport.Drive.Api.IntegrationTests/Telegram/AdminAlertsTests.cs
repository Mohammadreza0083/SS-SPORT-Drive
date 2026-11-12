using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Application.Telegram.Digest;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// The admins' bot alerts and daily digest (L-7, owner-approved 2026-11-12):
/// <list type="bullet">
/// <item>the first time an email opens a link that has "notify on open" on, every active admin gets one notice through
/// the notice rules (T-8); later sessions of that email never alert again;</item>
/// <item>at 09:00 Tehran every active admin gets the digest of yesterday, even when nothing happened.</item>
/// </list>
/// </summary>
public sealed class AdminAlertsTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    private const string BotToken = "123456:admin-alerts-test-token";

    /// <summary>The admin, who creates links and gets alerts.</summary>
    private const long AdminTelegramId = 804;

    /// <summary>The recipient's email.</summary>
    private const string RecipientEmail = "marco@lube.it";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Records what the bot sends.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own name and Volleybox id.</summary>
    private int _players;

    /// <summary>Starts PostgreSQL, boots the API with no quiet hours and stores the admin.</summary>
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
                ["Telegram:WebhookSecret"] = "admin-alerts-secret",
                ["Telegram:Mode"] = "Webhook",
                ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["RateLimiting:LinkPermitsPerMinute"] = "1000",
                ["RateLimiting:ViewerPermitsPerMinute"] = "1000",
                ["Notifications:QuietHoursStart"] = "00:00",
                ["Notifications:QuietHoursEnd"] = "00:00",
                ["Notifications:TimeZone"] = "Asia/Tehran",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.UseInlineBotNotices();
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
            .Add(User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), DateTimeOffset.UtcNow));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ---------------------------------------------------------------- first open

    /// <summary>
    /// A link with "notify on open" on: its first session alerts the admin once, with the link code, the recipient, the
    /// player and the email that opened it.
    /// </summary>
    [Fact]
    public async Task The_first_open_of_a_watched_link_alerts_the_admin()
    {
        var player = await AddPlayerAsync();
        var code = await CreateLinkAsync([player], notifyOnOpen: true);

        await OpenAsync(code);

        var alert = await _bot.WaitForAsync(call => call.ChatId == AdminTelegramId && call.Text!.Contains(code, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        alert.Text.Should().Contain("Marco Rossi").And.Contain("Arash Tehrani").And.Contain(RecipientEmail);
    }

    /// <summary>A second session of the same email on the same link records nothing new and sends nothing new.</summary>
    [Fact]
    public async Task A_later_open_by_the_same_email_does_not_alert_again()
    {
        var player = await AddPlayerAsync();
        var code = await CreateLinkAsync([player], notifyOnOpen: true);
        await OpenAsync(code);
        await _bot.WaitForAsync(call => call.Text!.Contains(code, StringComparison.Ordinal), TimeSpan.FromSeconds(5));

        await OpenAsync(code);
        await OpenAsync(code);

        (await CountAsync(db => db.Set<LinkOpen>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
        _bot.Calls.Count(call => call.Text?.Contains(code, StringComparison.Ordinal) == true).Should().Be(1);
    }

    /// <summary>A link with "notify on open" off (the default) still records the first open, but alerts nobody.</summary>
    [Fact]
    public async Task An_unwatched_link_records_the_open_silently()
    {
        var player = await AddPlayerAsync();
        var code = await CreateLinkAsync([player], notifyOnOpen: false);

        await OpenAsync(code);

        (await CountAsync(db => db.Set<LinkOpen>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
        _bot.Calls.Should().NotContain(call => call.Text != null && call.Text.Contains(code, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- daily digest

    /// <summary>
    /// The digest counts yesterday in Tehran only: links opened and distinct viewers from the sessions that started
    /// then, forwards still waiting for a decision, and players on the waiting list. Sessions from today are not
    /// counted, and published players are not waiting.
    /// </summary>
    [Fact]
    public async Task The_digest_counts_yesterday_and_what_is_waiting()
    {
        var now = DateTimeOffset.UtcNow;
        var window = DigestWindow.Yesterday(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"));
        var player = await AddPlayerAsync();
        var first = await CreateLinkAsync([player], notifyOnOpen: false);
        var second = await CreateLinkAsync([player], notifyOnOpen: false);
        await AddSessionAsync(first, RecipientEmail, window.Start.AddHours(1));
        await AddSessionAsync(first, RecipientEmail, window.Start.AddHours(2));
        await AddSessionAsync(second, "scout@other.com", window.End.AddMinutes(-1));
        await AddSessionAsync(second, "late@other.com", window.End.AddMinutes(1));
        await AddForwardAsync(first, "pending@other.com", keep: null);
        await AddForwardAsync(first, "kept@other.com", keep: true);
        await AddWaitingPlayerAsync();
        await AddWaitingPlayerAsync();

        var sent = await SendDigestAsync(now);

        sent.Should().Be(1, "one active admin");
        var digest = _bot.ToChat(AdminTelegramId).Single(call => call.Text!.StartsWith("📊 Daily digest", StringComparison.Ordinal));
        digest.Text!.Split('\n').Should().Equal(
            $"📊 Daily digest · {window.Day.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}",
            "Yesterday: 2 links opened · 2 viewers · 0 plays · 0 min watched",
            "Waiting on you: 1 forward to keep or block · 2 players on the waiting list");
    }

    /// <summary>A quiet day still sends the digest (owner).</summary>
    [Fact]
    public async Task A_quiet_day_still_sends_the_digest()
    {
        var sent = await SendDigestAsync(DateTimeOffset.UtcNow);

        sent.Should().Be(1);
        var digest = _bot.ToChat(AdminTelegramId).Single(call => call.Text!.StartsWith("📊 Daily digest", StringComparison.Ordinal));
        digest.Text.Should().Contain("Yesterday: no activity.").And.Contain("Nothing is waiting on you.");
    }

    /// <summary>The digest is not held by quiet hours or the throttle: it goes out at once, and has no buttons.</summary>
    [Fact]
    public async Task The_digest_is_sent_at_once_without_buttons()
    {
        await SendDigestAsync(DateTimeOffset.UtcNow);

        var digest = _bot.ToChat(AdminTelegramId).Single(call => call.Text!.StartsWith("📊 Daily digest", StringComparison.Ordinal));
        digest.Kind.Should().Be("Send");
        digest.Keyboard.Should().BeNull();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Runs the digest command as the 09:00 job does.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>How many admins got it.</returns>
    private async Task<int> SendDigestAsync(DateTimeOffset now)
    {
        using var scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SendDailyDigestCommand(now), TestContext.Current.CancellationToken);
    }

    /// <summary>Opens a link that skips the gate, as a browser does on <c>/r/{code}</c>.</summary>
    /// <param name="code">The link code.</param>
    /// <returns>A task that completes when the session is started.</returns>
    private async Task OpenAsync(string code)
    {
        using var client = _api.CreateClient();
        using var response = await client.PostAsync(new Uri($"/links/{code}/session", UriKind.Relative), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Stores a viewer session on a link at a given time.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="at">When it started.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task AddSessionAsync(string code, string email, DateTimeOffset at)
    {
        using var scope = _api.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<IViewerSecrets>();
        var link = await LinkAsync(scope, code);
        scope.ServiceProvider.GetRequiredService<IRepository<ViewerSession, Guid>>()
            .Add(ViewerSession.Start(secrets.HashToken(secrets.NewToken()), link.Id, EmailAddress.Create(email), false, null, at));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stores a forward on a link, waiting or decided.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="email">The forwarded email.</param>
    /// <param name="keep">Kept, blocked, or <see langword="null"/> for waiting.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task AddForwardAsync(string code, string email, bool? keep)
    {
        using var scope = _api.Services.CreateScope();
        var link = await LinkAsync(scope, code);
        var forward = LinkForward.Record(link.Id, EmailAddress.Create(email), DateTimeOffset.UtcNow);
        if (keep is { } decision)
        {
            forward.Decide(decision, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        }

        forward.ClearDomainEvents();
        scope.ServiceProvider.GetRequiredService<IRepository<LinkForward, Guid>>().Add(forward);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stores a player still on the waiting list.</summary>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task AddWaitingPlayerAsync()
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().Add(Player.Create(PlayerCategory.Women, DateTimeOffset.UtcNow));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Finds a link by its code.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="code">The code.</param>
    /// <returns>The link.</returns>
    private static async Task<ShareLink> LinkAsync(IServiceScope scope, string code) =>
        (await scope.ServiceProvider.GetRequiredService<IRepository<ShareLink, Guid>>()
            .FirstOrDefaultAsync(new ShareLinkByCodeSpec(ShareLinkCode.Create(code)), TestContext.Current.CancellationToken))!;

    /// <summary>Stores a published player; the first is Arash Tehrani.</summary>
    /// <returns>The player id.</returns>
    private async Task<Guid> AddPlayerAsync()
    {
        var n = Interlocked.Increment(ref _players);
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        var name = FullName.Create(n == 1 ? "Arash Tehrani" : $"Reza Moradi {(char)('A' + n)}{(char)('a' + n)}");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Opposite]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/arash-tehrani-p{83000 + n}"));
        player.Publish(DateTimeOffset.UtcNow);
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().Add(player);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return player.Id;
    }

    /// <summary>Creates a link that opens without the gate, as the recipient's first email, and returns its code.</summary>
    /// <param name="players">The player ids.</param>
    /// <param name="notifyOnOpen">Whether admins are told when it is opened.</param>
    /// <returns>The code.</returns>
    private async Task<string> CreateLinkAsync(IReadOnlyList<Guid> players, bool notifyOnOpen)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/admin/links", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                recipient = new { name = "Marco Rossi", organisation = "Lube Volley", emails = new[] { RecipientEmail } },
                playerIds = players,
                expiresInDays = 30,
                verifyOnNewDevice = false,
                notifyOnOpen,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, AdminTelegramId, DateTimeOffset.UtcNow));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Counts rows in the database.</summary>
    /// <param name="query">The count.</param>
    /// <returns>The number.</returns>
    private async Task<int> CountAsync(Func<AppDbContext, Task<int>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
