using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Telegram;
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

namespace SsSport.Drive.Api.IntegrationTests.Links;

/// <summary>
/// What a signed-in viewer sees behind <c>/r/{code}</c> (V-2b): the link's overview, its players in page order, one
/// player's card and stats, their ready and visible videos and documents (paged), and their photo. Every read needs a
/// live session for this very link; players outside the link are 404, and no storage key ever leaves the API.
/// </summary>
public sealed class ViewerContentEndpointTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    private const string BotToken = "123456:viewer-content-test-token";

    /// <summary>An admin, who creates the links.</summary>
    private const long AdminTelegramId = 802;

    /// <summary>The recipient's email.</summary>
    private const string RecipientEmail = "marco@lube.it";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Where photos are kept.</summary>
    private readonly InMemoryObjectStorage _storage = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own name and Volleybox id.</summary>
    private int _players;

    /// <summary>Counts created assets so each gets its own checksum and Drive id.</summary>
    private int _assets;

    /// <summary>Starts PostgreSQL, boots the API and stores an admin.</summary>
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
                ["Telegram:WebhookSecret"] = "viewer-content-secret",
                ["Telegram:Mode"] = "Webhook",
                ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["RateLimiting:LinkPermitsPerMinute"] = "1000",
                ["RateLimiting:ViewerPermitsPerMinute"] = "1000",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(_storage));
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

    /// <summary>Every content read needs a session: none, an unknown one, or one for another link is 401.</summary>
    [Fact]
    public async Task Content_needs_a_session_for_this_link()
    {
        var player = await AddPlayerAsync();
        var code = await CreateLinkAsync([player]);
        var otherCode = await CreateLinkAsync([player]);
        var otherSession = await StartSessionAsync(otherCode);

        foreach (var path in new[] { "content", "players", $"players/{player}", $"players/{player}/videos", $"players/{player}/documents", $"players/{player}/photo" })
        {
            using var none = await GetAsync($"/links/{code}/{path}", null);
            none.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
            using var body = await ReadJsonAsync(none);
            body.RootElement.GetProperty("code").GetString().Should().Be("viewer.verification_required");

            using var unknown = await GetAsync($"/links/{code}/{path}", "not-a-session-token-at-all-aaaaaaaaaaaaaaaa");
            unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);

            using var other = await GetAsync($"/links/{code}/{path}", otherSession);
            other.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }
    }

    /// <summary>An unknown or revoked link stays the same 404 as at the gate, even with a session.</summary>
    [Fact]
    public async Task A_closed_link_is_unavailable()
    {
        var code = await CreateLinkAsync([await AddPlayerAsync()]);
        var session = await StartSessionAsync(code);

        using var unknown = await GetAsync("/links/Zz9Yy8/content", session);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var revoked = await AdminPostAsync($"/admin/links/{await LinkIdAsync(code)}/revoke"))
        {
            revoked.IsSuccessStatusCode.Should().BeTrue();
        }

        using var response = await GetAsync($"/links/{code}/content", session);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("share_link.unavailable");
    }

    /// <summary>The overview: the link's type and size, who it was prepared for, the note, and the viewer's email.</summary>
    [Fact]
    public async Task The_overview_shows_the_recipient_note_and_viewer()
    {
        var code = await CreateLinkAsync([await AddPlayerAsync(), await AddPlayerAsync()], note: "Hi Marco, as discussed.");
        var session = await StartSessionAsync(code, "scout@lube.it", isForward: true);

        using var response = await GetAsync($"/links/{code}/content", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.GetProperty("type").GetString().Should().Be("Roster");
        root.GetProperty("playerCount").GetInt32().Should().Be(2);
        root.GetProperty("recipient").GetProperty("name").GetString().Should().Be("Marco Rossi");
        root.GetProperty("recipient").GetProperty("organisation").GetString().Should().Be("Lube Volley");
        root.GetProperty("note").GetString().Should().Be("Hi Marco, as discussed.");
        root.GetProperty("viewerEmail").GetString().Should().Be("scout@lube.it");
    }

    /// <summary>Players come in the link's page order, paged, with their card facts and how many videos can be watched.</summary>
    [Fact]
    public async Task Players_are_listed_in_page_order_with_their_video_count()
    {
        var first = await AddPlayerAsync(position: Position.Setter);
        var second = await AddPlayerAsync(photo: false);
        var third = await AddPlayerAsync();
        await AddVideoAsync(first, "Highlights", AssetTags.Highlight, 84);
        await AddVideoAsync(first, "Spikes", AssetTags.Spike, 120);
        await AddVideoAsync(first, "Hidden", AssetTags.Serve, 60, hidden: true);
        await AddVideoAsync(first, "Still processing", AssetTags.Serve, 60, ready: false);
        await AddDocumentAsync(first, "Stats", 2);
        var code = await CreateLinkAsync([second, first, third]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players?page=1&pageSize=2", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.GetProperty("totalCount").GetInt32().Should().Be(3);
        root.GetProperty("totalPages").GetInt32().Should().Be(2);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(second, first);
        items[0].GetProperty("hasPhoto").GetBoolean().Should().BeFalse();
        items[1].GetProperty("hasPhoto").GetBoolean().Should().BeTrue();
        items[1].GetProperty("firstName").GetString().Should().Be("Arash");
        items[1].GetProperty("position").GetProperty("code").GetString().Should().Be("S");
        items[1].GetProperty("position").GetProperty("label").GetString().Should().Be("Setter");
        items[1].GetProperty("nationality").GetProperty("name").GetString().Should().Be("Iran");
        items[1].GetProperty("videoCount").GetInt32().Should().Be(2);

        using var next = await GetAsync($"/links/{code}/players?page=2&pageSize=2", session);
        using var nextBody = await ReadJsonAsync(next);
        nextBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Should().Equal(third);
    }

    /// <summary>A bad page request is a 400, like every other list.</summary>
    [Fact]
    public async Task A_page_size_over_the_maximum_is_refused()
    {
        var code = await CreateLinkAsync([await AddPlayerAsync()]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players?pageSize=101", session);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>One player: the card, the Volleybox profile and the stats row, counting only what can be watched or read.</summary>
    [Fact]
    public async Task A_player_shows_the_card_and_stats()
    {
        var player = await AddPlayerAsync();
        await AddVideoAsync(player, "Highlights", AssetTags.Highlight, 84);
        await AddVideoAsync(player, "Full match", AssetTags.FullMatch, 6490);
        await AddVideoAsync(player, "Hidden", AssetTags.Serve, 600, hidden: true);
        await AddDocumentAsync(player, "Season stats", 2);
        await AddDocumentAsync(player, "Processing", 2, ready: false);
        await AddScoutAsync(player);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.GetProperty("name").GetString().Should().Be("Arash Tehrani");
        root.GetProperty("firstName").GetString().Should().Be("Arash");
        root.GetProperty("position").GetProperty("code").GetString().Should().Be("OPP");
        root.GetProperty("position").GetProperty("label").GetString().Should().Be("Opposite");
        root.GetProperty("nationality").GetProperty("code").GetString().Should().Be("IR");
        root.GetProperty("volleyboxUrl").GetString().Should().StartWith("https://volleybox.net/");
        root.GetProperty("hasPhoto").GetBoolean().Should().BeTrue();
        root.GetProperty("stats").GetProperty("videos").GetInt32().Should().Be(2);
        root.GetProperty("stats").GetProperty("minutes").GetInt32().Should().Be(110);
        root.GetProperty("stats").GetProperty("documents").GetInt32().Should().Be(1);
    }

    /// <summary>A player who is not on the link is 404 for every read, so ids cannot be walked.</summary>
    [Fact]
    public async Task A_player_outside_the_link_is_not_found()
    {
        var inside = await AddPlayerAsync();
        var outside = await AddPlayerAsync();
        var code = await CreateLinkAsync([inside]);
        var session = await StartSessionAsync(code);

        foreach (var path in new[] { string.Empty, "/videos", "/documents", "/photo" })
        {
            using var response = await GetAsync($"/links/{code}/players/{outside}{path}", session);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        }
    }

    /// <summary>Videos: ready and visible only, the hero first and then the page order, with their tag and length; paged.</summary>
    [Fact]
    public async Task Videos_are_ready_visible_and_hero_first()
    {
        var player = await AddPlayerAsync();
        var spikes = await AddVideoAsync(player, "Spikes", AssetTags.Spike | AssetTags.Highlight, 192);
        var match = await AddVideoAsync(player, "Full match vs Paykan", AssetTags.FullMatch, 6490);
        var untagged = await AddVideoAsync(player, "Warm-up", AssetTags.Untagged, 30);
        await AddVideoAsync(player, "Hidden", AssetTags.Serve, 60, hidden: true);
        await AddVideoAsync(player, "Processing", AssetTags.Serve, 60, ready: false);
        await AddDocumentAsync(player, "Stats", 1);
        await SetHeroAsync(player, match);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/videos?pageSize=2", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.GetProperty("totalCount").GetInt32().Should().Be(3);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(match, spikes);
        items[0].GetProperty("title").GetString().Should().Be("Full match vs Paykan");
        items[0].GetProperty("tag").GetString().Should().Be("Full match");
        items[0].GetProperty("durationSeconds").GetInt32().Should().Be(6490);
        items[1].GetProperty("tag").GetString().Should().Be("Highlight");

        using var next = await GetAsync($"/links/{code}/players/{player}/videos?page=2&pageSize=2", session);
        using var nextBody = await ReadJsonAsync(next);
        var last = nextBody.RootElement.GetProperty("items").EnumerateArray().Single();
        last.GetProperty("id").GetGuid().Should().Be(untagged);
        last.GetProperty("tag").GetString().Should().Be("Video");
    }

    /// <summary>Documents: ready and visible PDFs and images with their page count; DataVolley files wait for V-8.</summary>
    [Fact]
    public async Task Documents_are_ready_and_visible()
    {
        var player = await AddPlayerAsync();
        var stats = await AddDocumentAsync(player, "Season stats 2026", 2);
        var cv = await AddDocumentAsync(player, "Player CV", null);
        await AddDocumentAsync(player, "Hidden", 3, hidden: true);
        await AddScoutAsync(player);
        await AddVideoAsync(player, "Highlights", AssetTags.Highlight, 84);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{player}/documents", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(stats, cv);
        items[0].GetProperty("title").GetString().Should().Be("Season stats 2026");
        items[0].GetProperty("pageCount").GetInt32().Should().Be(2);
        items[1].GetProperty("pageCount").GetInt32().Should().Be(1);
    }

    /// <summary>The photo streams from storage with a private cache header; a player without one is 404.</summary>
    [Fact]
    public async Task The_photo_is_streamed_privately()
    {
        var withPhoto = await AddPlayerAsync();
        var withoutPhoto = await AddPlayerAsync(photo: false);
        var code = await CreateLinkAsync([withPhoto, withoutPhoto]);
        var session = await StartSessionAsync(code);

        using var response = await GetAsync($"/links/{code}/players/{withPhoto}/photo", session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        response.Headers.CacheControl!.Private.Should().BeTrue();
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(PhotoBytes(withPhoto));

        using var missing = await GetAsync($"/links/{code}/players/{withoutPhoto}/photo", session);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>No answer carries a storage key, md5 or Drive id: media leave the API only through the photo stream and, later, signed edge URLs.</summary>
    [Fact]
    public async Task No_storage_details_leak()
    {
        var player = await AddPlayerAsync();
        await AddVideoAsync(player, "Highlights", AssetTags.Highlight, 84);
        await AddDocumentAsync(player, "Stats", 1);
        var code = await CreateLinkAsync([player]);
        var session = await StartSessionAsync(code);

        foreach (var path in new[] { "content", "players", $"players/{player}", $"players/{player}/videos", $"players/{player}/documents" })
        {
            using var response = await GetAsync($"/links/{code}/{path}", session);
            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            text.Should().NotContain("players/").And.NotContain("assets/").And.NotContain("drive-").And.NotContain("md5", path);
        }
    }

    /// <summary>The photo bytes stored for a player.</summary>
    /// <param name="player">The player id.</param>
    /// <returns>The bytes.</returns>
    private static byte[] PhotoBytes(Guid player) => [.. player.ToByteArray(), 0x57, 0x45, 0x42, 0x50];

    /// <summary>Stores a published player from Iran; the first is Arash Tehrani, the others get their own names and Volleybox ids.</summary>
    /// <param name="position">The primary position.</param>
    /// <param name="photo">Whether the player has a photo in storage.</param>
    /// <returns>The player id.</returns>
    private async Task<Guid> AddPlayerAsync(Position position = Position.Opposite, bool photo = true)
    {
        var n = Interlocked.Increment(ref _players);
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        var name = FullName.Create(n == 1 ? "Arash Tehrani" : $"Arash Tehrani {(char)('A' + n)}{(char)('a' + n)}");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create(position == Position.OutsideHitter ? [position] : [position, Position.OutsideHitter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/arash-tehrani-p{81000 + n}"));
        player.Publish(DateTimeOffset.UtcNow);
        if (photo)
        {
            await _storage.PutAsync($"players/{player.Id}/photo.webp", new MemoryStream(PhotoBytes(player.Id)), "image/webp", TestContext.Current.CancellationToken);
        }

        await SaveAsync<Player>(player);
        if (!photo)
        {
            await QueryAsync(async db =>
            {
                var stored = await db.Set<Player>().FindAsync([player.Id], TestContext.Current.CancellationToken);
                db.Entry(stored!).Property(item => item.Photo).CurrentValue = null;
                return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            });
        }

        return player.Id;
    }

    /// <summary>Stores a video for a player, ready with all renditions unless told otherwise.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="title">The title.</param>
    /// <param name="tags">The tags.</param>
    /// <param name="seconds">The length.</param>
    /// <param name="hidden">Whether an admin hid it.</param>
    /// <param name="ready">Whether processing finished.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddVideoAsync(Guid player, string title, AssetTags tags, double seconds, bool hidden = false, bool ready = true)
    {
        var asset = NewAsset(player, $"{title}.mp4", title, tags);
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

        await SaveAsync<Asset>(asset);
        return asset.Id;
    }

    /// <summary>Stores a document for a player, ready unless told otherwise.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="title">The title.</param>
    /// <param name="pages">The page count, or <see langword="null"/> when unknown.</param>
    /// <param name="hidden">Whether an admin hid it.</param>
    /// <param name="ready">Whether processing finished.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddDocumentAsync(Guid player, string title, int? pages, bool hidden = false, bool ready = true)
    {
        var asset = NewAsset(player, $"{title}.pdf", title, AssetTags.Stats);
        if (ready)
        {
            asset.AttachOriginal(StorageKey.Create($"assets/{asset.Id}/original.pdf"));
            asset.CompleteDocument(pages);
        }

        if (hidden)
        {
            asset.Hide();
        }

        await SaveAsync<Asset>(asset);
        return asset.Id;
    }

    /// <summary>Stores a ready DataVolley scout file for a player.</summary>
    /// <param name="player">The player id.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task AddScoutAsync(Guid player)
    {
        var asset = NewAsset(player, "Match vs Paykan.dvw", "Match vs Paykan", AssetTags.Stats);
        asset.AttachOriginal(StorageKey.Create($"assets/{asset.Id}/original.dvw"));
        asset.CompleteDocument(null);
        await SaveAsync<Asset>(asset);
    }

    /// <summary>Builds a new asset in the next page position.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="title">The title.</param>
    /// <param name="tags">The tags.</param>
    /// <returns>The asset.</returns>
    private Asset NewAsset(Guid player, string fileName, string title, AssetTags tags)
    {
        var n = Interlocked.Increment(ref _assets);
        return Asset.Add(
            player,
            null,
            AssetSource.Drive,
            $"drive-{n}",
            AssetFile.Create(fileName, n.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), 1024 + n),
            title,
            tags,
            n,
            DateTimeOffset.UtcNow);
    }

    /// <summary>Makes a video the player's hero.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="video">The video id.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task SetHeroAsync(Guid player, Guid video)
    {
        using var scope = _api.Services.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>();
        var assets = scope.ServiceProvider.GetRequiredService<IRepository<Asset, Guid>>();
        var stored = await players.GetByIdAsync(player, TestContext.Current.CancellationToken);
        stored!.SetHero((await assets.GetByIdAsync(video, TestContext.Current.CancellationToken))!);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stores an aggregate.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="item">The aggregate.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task SaveAsync<T>(T item)
        where T : AggregateRoot<Guid>
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<T, Guid>>().Add(item);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a link to the players, in that order, for the recipient, and returns its code.</summary>
    /// <param name="players">The player ids.</param>
    /// <param name="note">The note, if any.</param>
    /// <returns>The code.</returns>
    private async Task<string> CreateLinkAsync(IReadOnlyList<Guid> players, string? note = null)
    {
        using var response = await AdminPostAsync("/admin/links", new
        {
            recipient = new { name = "Marco Rossi", organisation = "Lube Volley", emails = new[] { RecipientEmail } },
            playerIds = players,
            note,
            expiresInDays = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Finds a link's id by its code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The id.</returns>
    private async Task<Guid> LinkIdAsync(string code)
    {
        using var scope = _api.Services.CreateScope();
        var link = await scope.ServiceProvider.GetRequiredService<IRepository<ShareLink, Guid>>()
            .FirstOrDefaultAsync(new ShareLinkByCodeSpec(ShareLinkCode.Create(code)), TestContext.Current.CancellationToken);
        return link!.Id;
    }

    /// <summary>Starts a viewer session on a link directly, as the gate would after the right code.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="isForward">Whether it is a forward.</param>
    /// <returns>The session token.</returns>
    private async Task<string> StartSessionAsync(string code, string email = RecipientEmail, bool isForward = false)
    {
        var linkId = await LinkIdAsync(code);
        using var scope = _api.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<IViewerSecrets>();
        var token = secrets.NewToken();
        scope.ServiceProvider.GetRequiredService<IRepository<ViewerSession, Guid>>()
            .Add(ViewerSession.Start(secrets.HashToken(token), linkId, EmailAddress.Create(email), isForward, null, DateTimeOffset.UtcNow));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return token;
    }

    /// <summary>Runs work against the database.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="query">The work.</param>
    /// <returns>The result.</returns>
    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Reads a viewer endpoint, with the session header when given.</summary>
    /// <param name="path">The path.</param>
    /// <param name="sessionToken">The session token, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> GetAsync(string path, string? sessionToken)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (sessionToken is not null)
        {
            request.Headers.Add("X-Viewer-Session", sessionToken);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Posts as the admin.</summary>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> AdminPostAsync(string path, object? body = null)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, AdminTelegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Parses a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON document.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
