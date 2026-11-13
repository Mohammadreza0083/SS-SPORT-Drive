using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// T-6b end to end: as ingest moves a player's files through copy, transcode and pages, the analyst's one progress
/// message (T-6a) is edited in place with the build steps and a progress bar; once the player is published and every
/// file is done it ends with the result card, and the player's QR code is sent once as a reply to it.
/// </summary>
public sealed class BuildProgressMessageTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:build-progress-test-token";

    /// <summary>The analyst who creates the players.</summary>
    private const long OwnerTelegramId = 931;

    /// <summary>The web app's public address in this test.</summary>
    private const string PublicBaseUrl = "https://drive.test.example";

    /// <summary>A valid md5.</summary>
    private const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Records the bot's messages.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>Stands in for R2: holds the QR code.</summary>
    private readonly InMemoryObjectStorage _storage = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own name, Volleybox id and folder.</summary>
    private int _created;

    /// <summary>Starts PostgreSQL, boots the API and stores the analyst.</summary>
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
                ["Web:PublicBaseUrl"] = PublicBaseUrl,
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.UseInlineBotNotices();
                services.Replace(ServiceDescriptor.Singleton<IDriveFolderReader>(new FakeDriveFolderReader()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(_storage));
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        var analyst = User.RequestAccess(OwnerTelegramId, "analyst_build", "Analyst Build", DateTimeOffset.UtcNow);
        analyst.Approve(Role.Analyst);
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().Add(analyst);
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

    /// <summary>A file found in Drive edits the same message with the build steps and an empty bar.</summary>
    [Fact]
    public async Task Adding_a_file_shows_the_build_steps()
    {
        var (id, _) = await CreatePlayerAsync();
        var sent = _bot.ToChat(OwnerTelegramId).Single();

        await AddAssetAsync(id, "game.mp4");

        var edit = _bot.ToChat(OwnerTelegramId).Last();
        edit.Kind.Should().Be("Edit");
        edit.MessageId.Should().Be(sent.MessageId);
        edit.Text.Should().EndWith(string.Join('\n',
            "⏳ Copy: 0/1 files",
            "⏳ Transcode & thumbnails: 0/1 videos",
            "░░░░░░░░░░ 0% · estimating the time left"));
    }

    /// <summary>Each step of the pipeline edits the same message; when the video is done the bar goes away.</summary>
    [Fact]
    public async Task Each_step_edits_the_same_message()
    {
        var (id, _) = await CreatePlayerAsync();
        var sent = _bot.ToChat(OwnerTelegramId).Single();
        var video = await AddAssetAsync(id, "game.mp4");

        await ChangeAssetAsync(video, asset => asset.StartCopying());
        LastText().Should().Contain("▓░░░░░░░░░ 10% · about ");

        await ChangeAssetAsync(video, asset =>
        {
            asset.AttachOriginal(StorageKey.Create($"originals/{id}/game.mp4"));
            asset.RecordVideoInfo(sourceHeight: 720, durationSeconds: 600);
        });
        LastText().Should().Contain("✅ Copy: 1/1 files").And.Contain("▓▓▓░░░░░░░ 30% · about ");

        await ChangeAssetAsync(video, asset => asset.MarkRenditionReady(RenditionQuality.P360, StorageKey.Create($"hls/{video}/360/index.m3u8")));
        LastText().Should().Contain("▓▓▓▓▓▓░░░░ 65% · about ");

        await ChangeAssetAsync(video, asset => asset.MarkRenditionReady(RenditionQuality.P720, StorageKey.Create($"hls/{video}/720/index.m3u8")));
        LastText().Should().EndWith("✅ Copy: 1/1 files\n✅ Transcode & thumbnails: 1/1 videos");

        var calls = _bot.ToChat(OwnerTelegramId);
        calls.Should().ContainSingle(call => call.Kind == "Send", "the progress is one message, edited in place");
        calls.Where(call => call.Kind == "Edit").Should().OnlyContain(call => call.MessageId == sent.MessageId);
    }

    /// <summary>A failed file is reported under the steps.</summary>
    [Fact]
    public async Task A_failed_file_is_reported()
    {
        var (id, _) = await CreatePlayerAsync();
        var report = await AddAssetAsync(id, "report.pdf");

        await ChangeAssetAsync(report, asset =>
        {
            asset.StartCopying();
            asset.Fail("The Drive file could not be read.");
        });

        LastText().Should().EndWith("✅ Copy: 0/1 files\n✅ Pages: 0/1 documents\n⚠️ 1 file failed; an admin will retry it");
    }

    /// <summary>Files removed from Drive are hidden and no longer counted.</summary>
    [Fact]
    public async Task Files_removed_from_Drive_are_not_counted()
    {
        var (id, _) = await CreatePlayerAsync();
        await AddAssetAsync(id, "game.mp4");
        var removed = await AddAssetAsync(id, "old-game.mp4");

        await ChangeAssetAsync(removed, asset => asset.MarkRemovedFromSource(DateTimeOffset.UtcNow));

        LastText().Should().Contain("⏳ Copy: 0/1 files").And.Contain("0/1 videos");
    }

    /// <summary>
    /// Published and every file done: the message ends with the result card and the public page, and the QR code is
    /// sent once, as a reply to the progress message; later refreshes do not send it again.
    /// </summary>
    [Fact]
    public async Task Published_player_gets_the_result_card_and_the_QR_code_once()
    {
        var (id, name) = await CreatePlayerAsync();
        using (var upload = await PutPhotoAsync(id))
        {
            upload.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var sent = _bot.ToChat(OwnerTelegramId).First(call => call.Kind == "Send");
        var video = await AddAssetAsync(id, "game.mp4");
        var report = await AddAssetAsync(id, "report.pdf");
        var qr = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var qrKey = $"players/{id}/qr.png";
        using (var png = new MemoryStream(qr))
        {
            await _storage.PutAsync(qrKey, png, "image/png", TestContext.Current.CancellationToken);
        }

        await ChangePlayerAsync(id, player =>
        {
            player.SetQrCode(StorageKey.Create(qrKey));
            player.Publish(DateTimeOffset.UtcNow);
        });
        _bot.ToChat(OwnerTelegramId).Should().NotContain(call => call.Kind == "Photo", "the files are not done yet");

        await ChangeAssetAsync(report, asset =>
        {
            asset.StartCopying();
            asset.AttachOriginal(StorageKey.Create($"originals/{id}/report.pdf"));
            asset.CompleteDocument(pageCount: 3);
        });
        await ChangeAssetAsync(video, asset =>
        {
            asset.StartCopying();
            asset.AttachOriginal(StorageKey.Create($"originals/{id}/game.mp4"));
            asset.RecordVideoInfo(sourceHeight: 360, durationSeconds: 60);
            asset.MarkRenditionReady(RenditionQuality.P360, StorageKey.Create($"hls/{video}/360/index.m3u8"));
        });

        var slug = Slug.FromName(FullName.Create(name)).Value;
        LastText().Should().StartWith($"✅ {name}").And.EndWith(string.Join('\n',
            "✅ Copy: 2/2 files",
            "✅ Transcode & thumbnails: 1/1 videos",
            "✅ Pages: 1/1 documents",
            "🎉 Live: 1 video · 1 document",
            $"🔗 {PublicBaseUrl}/players/{slug}"));
        var photo = _bot.ToChat(OwnerTelegramId).Should().ContainSingle(call => call.Kind == "Photo").Subject;
        photo.ReplyTo.Should().Be(sent.MessageId);
        photo.Text.Should().Be($"QR code for {name}");
        photo.Photo.Should().Equal(qr);

        using (var folder = await SendAsync(HttpMethod.Post, $"/miniapp/players/{id}/folders", new { driveFolderUrl = "buildExtra_0001" }))
        {
            folder.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        _bot.ToChat(OwnerTelegramId).Count(call => call.Kind == "Photo").Should().Be(1, "the QR code is sent only once");
    }

    /// <summary>Encodes a JPEG large enough for a player photo.</summary>
    /// <returns>The bytes.</returns>
    private static byte[] Jpeg()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 900, 1100);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>Gets the text the owner's chat shows last.</summary>
    /// <returns>The text of the last Send or Edit.</returns>
    private string LastText() => _bot.ToChat(OwnerTelegramId).Last(call => call.Kind is "Send" or "Edit").Text!;

    /// <summary>Creates a player owned by the analyst through the Mini App.</summary>
    /// <returns>The player id and name.</returns>
    private async Task<(Guid Id, string Name)> CreatePlayerAsync()
    {
        var n = Interlocked.Increment(ref _created);
        var name = $"Build Player {(char)('A' + n)}";
        using var response = await SendAsync(HttpMethod.Post, "/miniapp/players", new
        {
            category = "Men",
            name,
            nationalityCode = "IR",
            positions = new[] { "OH" },
            volleyboxUrl = $"https://volleybox.net/build-player-p{70000 + n}",
            driveFolderUrl = $"https://drive.google.com/drive/folders/buildFolder_{n:D4}",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), name);
    }

    /// <summary>Stores a queued file of a player, as ingest will.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddAssetAsync(Guid playerId, string fileName)
    {
        using var scope = _api.Services.CreateScope();
        var asset = Asset.Add(playerId, null, AssetSource.Drive, null, AssetFile.Create(fileName, Md5, 2048), Path.GetFileNameWithoutExtension(fileName), AssetTags.Untagged, 0, DateTimeOffset.UtcNow);
        scope.ServiceProvider.GetRequiredService<IRepository<Asset, Guid>>().Add(asset);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return asset.Id;
    }

    /// <summary>Moves a file through the pipeline, as the ingest workers will (renditions loaded), and saves it.</summary>
    /// <param name="assetId">The file.</param>
    /// <param name="change">The steps.</param>
    /// <returns>A task that completes when it is saved and its notices ran.</returns>
    private async Task ChangeAssetAsync(Guid assetId, Action<Asset> change)
    {
        using var scope = _api.Services.CreateScope();
        var asset = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Asset>()
            .Include(file => file.Renditions)
            .SingleAsync(file => file.Id == assetId, TestContext.Current.CancellationToken);
        change(asset);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Changes a player, as an admin would, and saves it.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="change">The change.</param>
    /// <returns>A task that completes when it is saved and its notices ran.</returns>
    private async Task ChangePlayerAsync(Guid playerId, Action<Player> change)
    {
        using var scope = _api.Services.CreateScope();
        var player = await scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().GetByIdAsync(playerId, TestContext.Current.CancellationToken);
        change(player!);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Uploads the player's photo through the Mini App.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> PutPhotoAsync(Guid playerId)
    {
        using var client = _api.CreateClient();
        var content = new ByteArrayContent(Jpeg());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/miniapp/players/{playerId}/photo", UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, OwnerTelegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Calls the Mini App API as the analyst.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, OwnerTelegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
