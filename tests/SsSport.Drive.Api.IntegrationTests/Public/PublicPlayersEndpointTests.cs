using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Public;

/// <summary>
/// The public, signed-out side of the site (P-1, P-2 backend, P-8): the home page searches published players by name
/// with a position filter, paged in SQL; the locked player page shows the card, the counts, and each video's title, tag
/// and length and each document's title and page count, but never a media address, storage key or asset id (decisions
/// §6 "Public home page"). Players still on the waiting list do not exist publicly.
/// </summary>
public sealed class PublicPlayersEndpointTests : IAsyncLifetime
{
    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Where photos are kept.</summary>
    private readonly InMemoryObjectStorage _storage = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created assets so each gets its own checksum and Drive id.</summary>
    private int _assets;

    /// <summary>Counts created players so each gets its own Volleybox id.</summary>
    private int _players;

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
                ["Telegram:BotToken"] = "123456:public-players-test-token",
                ["Telegram:WebhookSecret"] = "public-players-secret",
                ["Telegram:Mode"] = "Webhook",
                ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["RateLimiting:PublicPermitsPerMinute"] = "1000",
                ["RateLimiting:AccessPermitsPerMinute"] = "1000",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(_storage));
                services.UseInlineBotNotices();
            });
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

    /// <summary>The list holds published players only, sorted by name, paged in SQL with the total.</summary>
    [Fact]
    public async Task The_list_shows_published_players_paged()
    {
        await AddPlayerAsync("Milad Ebadipour", Position.OutsideHitter);
        await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddPlayerAsync("Ivan Valeev", Position.OutsideHitter);
        await AddPlayerAsync("Aref Moradi", Position.Setter, publish: false);

        using var first = await GetAsync("/public/players?page=1&pageSize=2");
        using var second = await GetAsync("/public/players?page=2&pageSize=2");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstBody = await ReadJsonAsync(first);
        Names(firstBody).Should().Equal("Arash Tehrani", "Ivan Valeev");
        firstBody.RootElement.GetProperty("totalCount").GetInt32().Should().Be(3);
        firstBody.RootElement.GetProperty("totalPages").GetInt32().Should().Be(2);
        using var secondBody = await ReadJsonAsync(second);
        Names(secondBody).Should().Equal("Milad Ebadipour");
    }

    /// <summary>A card carries only what the public may see: no id, no Volleybox, no storage key.</summary>
    [Fact]
    public async Task A_card_shows_only_public_facts()
    {
        var player = await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddVideoAsync(player, "Highlights 2025", AssetTags.Highlight, 84);
        await AddVideoAsync(player, "Hidden", AssetTags.Spike, 30, hidden: true);
        await AddVideoAsync(player, "Processing", AssetTags.Spike, 30, ready: false);
        await AddDocumentAsync(player, "Season stats", 4);

        using var response = await GetAsync("/public/players");

        using var body = await ReadJsonAsync(response);
        var card = body.RootElement.GetProperty("items")[0];
        card.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("slug", "name", "position", "nationality", "videos", "documents", "hasPhoto");
        card.GetProperty("slug").GetString().Should().Be("arash-tehrani");
        card.GetProperty("position").GetProperty("code").GetString().Should().Be("OPP");
        card.GetProperty("nationality").GetProperty("code").GetString().Should().Be("IR");
        card.GetProperty("nationality").GetProperty("name").GetString().Should().Be("Iran");
        card.GetProperty("videos").GetInt32().Should().Be(1);
        card.GetProperty("documents").GetInt32().Should().Be(1);
        card.GetProperty("hasPhoto").GetBoolean().Should().BeTrue();
    }

    /// <summary>Search matches any part of the name, ignoring case; LIKE wildcards are matched literally.</summary>
    [Fact]
    public async Task Search_matches_part_of_the_name()
    {
        await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddPlayerAsync("Ivan Valeev", Position.OutsideHitter);

        using var partial = await GetAsync("/public/players?search=TEHR");
        using var wildcard = await GetAsync("/public/players?search=%25");
        using var underscore = await GetAsync("/public/players?search=_");

        using var partialBody = await ReadJsonAsync(partial);
        Names(partialBody).Should().Equal("Arash Tehrani");
        using var wildcardBody = await ReadJsonAsync(wildcard);
        Names(wildcardBody).Should().BeEmpty();
        using var underscoreBody = await ReadJsonAsync(underscore);
        Names(underscoreBody).Should().BeEmpty();
    }

    /// <summary>The position chip filters on the primary position, as the cards show it; an unknown code is 400.</summary>
    [Fact]
    public async Task The_position_filter_uses_the_primary_position()
    {
        await AddPlayerAsync("Arash Tehrani", Position.Opposite, secondary: Position.OutsideHitter);
        await AddPlayerAsync("Ivan Valeev", Position.OutsideHitter);

        using var hitters = await GetAsync("/public/players?position=OH");
        using var opposites = await GetAsync("/public/players?position=opp");
        using var unknown = await GetAsync("/public/players?position=XX");

        using var hittersBody = await ReadJsonAsync(hitters);
        Names(hittersBody).Should().Equal("Ivan Valeev");
        using var oppositesBody = await ReadJsonAsync(opposites);
        Names(oppositesBody).Should().Equal("Arash Tehrani");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Paging is checked like every other list: a page size over 100 is 400.</summary>
    [Fact]
    public async Task Paging_is_validated()
    {
        using var response = await GetAsync("/public/players?pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The locked page reads a published player by slug, with the totals of ready, visible footage.</summary>
    [Fact]
    public async Task The_locked_page_shows_the_player_and_totals()
    {
        var player = await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddVideoAsync(player, "Highlights 2025", AssetTags.Highlight, 90);
        await AddVideoAsync(player, "Full match vs Paykan", AssetTags.FullMatch, 6000);
        await AddVideoAsync(player, "Hidden", AssetTags.Spike, 600, hidden: true);
        await AddDocumentAsync(player, "Season stats", 4);

        using var response = await GetAsync("/public/players/arash-tehrani");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "slug", "name", "firstName", "position", "nationality", "volleyboxUrl", "hasPhoto", "stats");
        root.GetProperty("name").GetString().Should().Be("Arash Tehrani");
        root.GetProperty("firstName").GetString().Should().Be("Arash");
        root.GetProperty("volleyboxUrl").GetString().Should().StartWith("https://volleybox.net/");
        root.GetProperty("stats").GetProperty("videos").GetInt32().Should().Be(2);
        root.GetProperty("stats").GetProperty("minutes").GetInt32().Should().Be(102);
        root.GetProperty("stats").GetProperty("documents").GetInt32().Should().Be(1);
    }

    /// <summary>An unknown slug and a player still on the waiting list are the same 404.</summary>
    [Fact]
    public async Task Unknown_and_unpublished_players_are_404()
    {
        await AddPlayerAsync("Aref Moradi", Position.Setter, publish: false);

        foreach (var path in new[] { "aref-moradi", "aref-moradi/videos", "aref-moradi/documents", "aref-moradi/photo", "nobody", "nobody/videos" })
        {
            using var response = await GetAsync($"/public/players/{path}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        }
    }

    /// <summary>
    /// Locked videos are a title, tag and length only, the hero first, then page order; hidden and unready ones are left
    /// out. Nothing in the answer could be used to fetch the media (P-2 security test).
    /// </summary>
    [Fact]
    public async Task Locked_videos_carry_no_media_address()
    {
        var player = await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddVideoAsync(player, "Serve drills", AssetTags.Serve, 84);
        var hero = await AddVideoAsync(player, "Highlights 2025", AssetTags.Highlight, 3730);
        await AddVideoAsync(player, "Untagged clip", AssetTags.Untagged, 61);
        await AddVideoAsync(player, "Hidden", AssetTags.Spike, 30, hidden: true);
        await AddVideoAsync(player, "Processing", AssetTags.Spike, 30, ready: false);
        await SetHeroAsync(player, hero);

        using var response = await GetAsync("/public/players/arash-tehrani/videos?page=1&pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(raw);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("title").GetString()).Should().Equal("Highlights 2025", "Serve drills", "Untagged clip");
        items.Select(item => item.GetProperty("tag").GetString()).Should().Equal("Highlight", "Serve", "Video");
        items[0].GetProperty("durationSeconds").GetInt32().Should().Be(3730);
        items.Should().AllSatisfy(item =>
            item.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("title", "tag", "durationSeconds"));
        AssertNoMediaAddress(raw, player);
    }

    /// <summary>Locked documents are a title and page count only, in page order; nothing could fetch them.</summary>
    [Fact]
    public async Task Locked_documents_carry_no_media_address()
    {
        var player = await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddDocumentAsync(player, "Season stats", 4);
        await AddDocumentAsync(player, "Medical", 2, hidden: true);
        await AddDocumentAsync(player, "Scouting report", 12);

        using var response = await GetAsync("/public/players/arash-tehrani/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(raw);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("title").GetString()).Should().Equal("Season stats", "Scouting report");
        items.Select(item => item.GetProperty("pageCount").GetInt32()).Should().Equal(4, 12);
        items.Should().AllSatisfy(item =>
            item.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("title", "pageCount"));
        AssertNoMediaAddress(raw, player);
    }

    /// <summary>The locked page and the list never carry a media address either.</summary>
    [Fact]
    public async Task The_player_and_list_carry_no_media_address()
    {
        var player = await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddVideoAsync(player, "Highlights 2025", AssetTags.Highlight, 84);

        foreach (var path in new[] { "/public/players", "/public/players/arash-tehrani" })
        {
            using var response = await GetAsync(path);
            AssertNoMediaAddress(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), player);
        }
    }

    /// <summary>The photo is public (the home cards show it), streamed with a public cache; a player without one is 404.</summary>
    [Fact]
    public async Task The_photo_is_public()
    {
        var player = await AddPlayerAsync("Arash Tehrani", Position.Opposite);
        await AddPlayerAsync("Ivan Valeev", Position.OutsideHitter, photo: false);

        using var response = await GetAsync("/public/players/arash-tehrani/photo");
        using var missing = await GetAsync("/public/players/ivan-valeev/photo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        response.Headers.CacheControl!.Public.Should().BeTrue();
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(PhotoBytes(player));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Fails when an answer holds anything that could reach a stored file: keys, playlists, ids.</summary>
    /// <param name="raw">The raw JSON.</param>
    /// <param name="player">The player id, which is not public either.</param>
    private static void AssertNoMediaAddress(string raw, Guid player)
    {
        raw.Should().NotContain("assets/").And.NotContain(".m3u8").And.NotContain(".mp4").And.NotContain(".pdf")
            .And.NotContain("players/").And.NotContain("drive-").And.NotContain(player.ToString())
            .And.NotContain("\"id\"").And.NotContain("http://").And.NotContain("stream");
    }

    /// <summary>The names on a page.</summary>
    /// <param name="body">The page.</param>
    /// <returns>The names, in order.</returns>
    private static List<string?> Names(JsonDocument body) =>
        [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString())];

    /// <summary>The photo bytes stored for a player.</summary>
    /// <param name="player">The player id.</param>
    /// <returns>The bytes.</returns>
    private static byte[] PhotoBytes(Guid player) => [.. player.ToByteArray(), 0x57, 0x45, 0x42, 0x50];

    /// <summary>Stores a player from Iran, published unless told otherwise.</summary>
    /// <param name="name">The full name.</param>
    /// <param name="primary">The primary position.</param>
    /// <param name="secondary">A second position, if any.</param>
    /// <param name="publish">Whether the player is published.</param>
    /// <param name="photo">Whether the player has a photo in storage.</param>
    /// <returns>The player id.</returns>
    private async Task<Guid> AddPlayerAsync(string name, Position primary, Position? secondary = null, bool publish = true, bool photo = true)
    {
        var n = Interlocked.Increment(ref _players);
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        var fullName = FullName.Create(name);
        player.Rename(fullName, Slug.FromName(fullName));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create(secondary is { } second ? [primary, second] : [primary]));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/player-p{82000 + n}"));
        if (photo)
        {
            player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
            await _storage.PutAsync($"players/{player.Id}/photo.webp", new MemoryStream(PhotoBytes(player.Id)), "image/webp", TestContext.Current.CancellationToken);
        }

        if (publish)
        {
            if (!photo)
            {
                player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
            }

            player.Publish(DateTimeOffset.UtcNow);
        }

        await SaveAsync<Player>(player);
        if (publish && !photo)
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

    /// <summary>Stores a ready document for a player.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="title">The title.</param>
    /// <param name="pages">The page count.</param>
    /// <param name="hidden">Whether an admin hid it.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddDocumentAsync(Guid player, string title, int pages, bool hidden = false)
    {
        var asset = NewAsset(player, $"{title}.pdf", title, AssetTags.Stats);
        asset.AttachOriginal(StorageKey.Create($"assets/{asset.Id}/original.pdf"));
        asset.CompleteDocument(pages);
        if (hidden)
        {
            asset.Hide();
        }

        await SaveAsync<Asset>(asset);
        return asset.Id;
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

    /// <summary>Runs work against the database.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="query">The work.</param>
    /// <returns>The result.</returns>
    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Reads a public endpoint with no credentials at all.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var client = _api.CreateClient();
        return await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
    }

    /// <summary>Parses a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON document.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
