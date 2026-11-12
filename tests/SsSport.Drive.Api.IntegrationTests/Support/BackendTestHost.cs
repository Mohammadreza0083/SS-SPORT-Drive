using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Support;

/// <summary>
/// One API on a throwaway PostgreSQL for the email queue (L-4), sign-in (A-1, P-5) and tracking (L-5, L-6) tests: an
/// active admin and an analyst, recording email providers instead of real ones, background events run inline, and
/// helpers to add players, videos, links and viewer sessions.
/// </summary>
public sealed partial class BackendTestHost : IAsyncDisposable
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    public const string BotToken = "123456:backend-test-token";

    /// <summary>The active admin's Telegram id.</summary>
    public const long AdminTelegramId = 802;

    /// <summary>The active admin's email (console sign-in).</summary>
    public const string AdminEmail = "owner@ss-sport.co";

    /// <summary>An active analyst's Telegram id.</summary>
    public const long AnalystTelegramId = 702;

    /// <summary>An active analyst's email; analysts never sign in to the console.</summary>
    public const string AnalystEmail = "analyst@ss-sport.co";

    /// <summary>The recipient of every link.</summary>
    public const string RecipientEmail = "marco@lube.it";

    /// <summary>The web app's public address, used in emailed links.</summary>
    public const string PublicBaseUrl = "https://drive.test";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Counts created players so each gets its own name and Volleybox id.</summary>
    private int _players;

    /// <summary>Counts created assets so each gets its own checksum and Drive id.</summary>
    private int _assets;

    /// <summary>Gets the API under test.</summary>
    public WebApplicationFactory<Program> Api { get; private set; } = null!;

    /// <summary>Gets the stand-in for Resend.</summary>
    public RecordingEmailTransport Resend { get; } = new("Resend");

    /// <summary>Gets the stand-in for Brevo.</summary>
    public RecordingEmailTransport Brevo { get; } = new("Brevo");

    /// <summary>Gets the stand-in for Google SMTP.</summary>
    public RecordingEmailTransport GoogleSmtp { get; } = new("GoogleSmtp");

    /// <summary>Gets the admin's user id.</summary>
    public Guid AdminId { get; private set; }

    /// <summary>Gets the analyst's user id.</summary>
    public Guid AnalystId { get; private set; }

    /// <summary>Starts PostgreSQL, boots the API and stores the team.</summary>
    /// <param name="settings">Extra configuration, or <see langword="null"/>.</param>
    /// <returns>The host.</returns>
    public static async Task<BackendTestHost> StartAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var host = new BackendTestHost();
        await host.InitializeAsync(settings);
        return host;
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Stores a published player with a photo.</summary>
    /// <returns>The player id.</returns>
    public async Task<Guid> AddPlayerAsync()
    {
        var n = Interlocked.Increment(ref _players);
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        var name = FullName.Create(n == 1 ? "Arash Tehrani" : $"Arash Tehrani {(char)('A' + n)}{(char)('a' + n)}");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Opposite, Position.OutsideHitter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/arash-tehrani-p{81000 + n}"));
        player.Publish(DateTimeOffset.UtcNow);
        await SaveAsync(player);
        return player.Id;
    }

    /// <summary>Stores a video for a player, ready with all renditions and visible unless told otherwise.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="seconds">The length.</param>
    /// <param name="hidden">Whether an admin hid it.</param>
    /// <param name="ready">Whether processing finished.</param>
    /// <returns>The asset id.</returns>
    public async Task<Guid> AddVideoAsync(Guid player, double seconds = 100, bool hidden = false, bool ready = true)
    {
        var n = Interlocked.Increment(ref _assets);
        var asset = Asset.Add(
            player,
            null,
            AssetSource.Drive,
            $"drive-{n}",
            AssetFile.Create($"Video {n}.mp4", n.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), 1024 + n),
            $"Video {n}",
            AssetTags.Highlight,
            n,
            DateTimeOffset.UtcNow);
        if (ready)
        {
            asset.StartCopying();
            asset.AttachOriginal(StorageKey.Create($"assets/{asset.Id}/original.mp4"));
            asset.RecordVideoInfo(1080, seconds);
            foreach (var quality in new[] { RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080 })
            {
                asset.MarkRenditionReady(quality, StorageKey.Create($"assets/{asset.Id}/{(int)quality}p/index.m3u8"));
            }
        }

        if (hidden)
        {
            asset.Hide();
        }

        await SaveAsync(asset);
        return asset.Id;
    }

    /// <summary>Creates a link to the players for the recipient (as the admin) and returns its code.</summary>
    /// <param name="players">The player ids.</param>
    /// <returns>The code.</returns>
    public async Task<string> CreateLinkAsync(params Guid[] players)
    {
        using var response = await SendAsync(HttpMethod.Post, "/admin/links", new
        {
            recipient = new { name = "Marco Rossi", organisation = "Lube Volley", emails = new[] { RecipientEmail } },
            playerIds = players,
            expiresInDays = 30,
        }, telegramId: AdminTelegramId);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Finds a link's id by its code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The id.</returns>
    public async Task<Guid> LinkIdAsync(string code)
    {
        using var scope = Api.Services.CreateScope();
        var link = await scope.ServiceProvider.GetRequiredService<IRepository<ShareLink, Guid>>()
            .FirstOrDefaultAsync(new ShareLinkByCodeSpec(ShareLinkCode.Create(code)), TestContext.Current.CancellationToken);
        return link!.Id;
    }

    /// <summary>Starts a viewer session on a link directly, as the gate would after the right code.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="email">The viewer's email.</param>
    /// <returns>The session token.</returns>
    public async Task<string> StartViewerSessionAsync(string code, string email = RecipientEmail)
    {
        var linkId = await LinkIdAsync(code);
        using var scope = Api.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<IViewerSecrets>();
        var token = secrets.NewToken();
        scope.ServiceProvider.GetRequiredService<IRepository<ViewerSession, Guid>>()
            .Add(ViewerSession.Start(secrets.HashToken(token), linkId, EmailAddress.Create(email), false, null, DateTimeOffset.UtcNow));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return token;
    }

    /// <summary>Stores an aggregate.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="item">The aggregate.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public async Task SaveAsync<T>(T item)
        where T : AggregateRoot<Guid>
    {
        using var scope = Api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<T, Guid>>().Add(item);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Runs work against the database.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="query">The work.</param>
    /// <returns>The result.</returns>
    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Sends a request with an optional JSON body, Mini App signature and extra headers.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <param name="telegramId">Signs the request as this Telegram user, or <see langword="null"/>.</param>
    /// <param name="headers">Extra headers (session tokens), or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, long? telegramId = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var client = Api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        if (telegramId is { } id)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, id, DateTimeOffset.UtcNow));
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Parses a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON document.</returns>
    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>Reads the <c>code</c> of a ProblemDetails answer.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The code.</returns>
    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = await ReadJsonAsync(response);
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Boots everything.</summary>
    /// <param name="settings">Extra configuration, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the API is ready.</returns>
    private async Task InitializeAsync(IReadOnlyDictionary<string, string?>? settings)
    {
        await _postgres.StartAsync();
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
            ["Telegram:BotToken"] = BotToken,
            ["Telegram:WebhookSecret"] = "backend-test-secret",
            ["Telegram:Mode"] = "Webhook",
            ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            ["Web:PublicBaseUrl"] = PublicBaseUrl,
            ["RateLimiting:LinkPermitsPerMinute"] = "1000",
            ["RateLimiting:ViewerPermitsPerMinute"] = "1000",
            ["RateLimiting:AuthPermitsPerMinute"] = "1000",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            configuration[key] = value;
        }

        Api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(new InMemoryObjectStorage()));
                services.UseInlineBotNotices();
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(Resend);
                services.AddSingleton<IEmailTransport>(Brevo);
                services.AddSingleton<IEmailTransport>(GoogleSmtp);
            });
        });
        await DatabaseMigrator.MigrateAsync(Api.Services, TestContext.Current.CancellationToken);

        using var scope = Api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var admin = User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create(AdminEmail), DateTimeOffset.UtcNow);
        var analyst = User.RequestAccess(AnalystTelegramId, "analyst", "Analyst", DateTimeOffset.UtcNow);
        analyst.Approve(Role.Analyst);
        analyst.ChangeEmail(EmailAddress.Create(AnalystEmail));
        users.Add(admin);
        users.Add(analyst);
        (AdminId, AnalystId) = (admin.Id, analyst.Id);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>An email provider stand-in: records each message, or throws while <see cref="Fails"/> is set.</summary>
/// <param name="name">The provider name the dispatcher routes by.</param>
public sealed partial class RecordingEmailTransport(string name) : IEmailTransport
{
    /// <summary>The messages, in order.</summary>
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <summary>Gets or sets a value indicating whether every send throws (a provider outage).</summary>
    public bool Fails { get; set; }

    /// <summary>Gets the messages, in order.</summary>
    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    /// <summary>Finds the 6-digit code in a gate-code email.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The code.</returns>
    public static string CodeIn(EmailMessage message) => CodePattern().Match(message.Text).Value;

    /// <summary>Finds the sign-in token in a magic-link email (<c>…?token=…</c>).</summary>
    /// <param name="message">The message.</param>
    /// <returns>The token.</returns>
    public static string TokenIn(EmailMessage message) => TokenPattern().Match(message.Text).Groups[1].Value;

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fails)
        {
            throw new HttpRequestException("provider down");
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Six digits on their own.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();

    /// <summary>The token query parameter.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"[?&]token=([A-Za-z0-9_\-]+)")]
    private static partial Regex TokenPattern();
}
