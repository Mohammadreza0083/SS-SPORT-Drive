using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// The Mini App photo step (T-4b): the analyst who owns a player, or any admin, uploads the photo as the raw request
/// body. It is checked, made upright, stripped of metadata, saved as WebP next to the original, and read back.
/// </summary>
public sealed class MiniAppPlayerPhotoTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:photo-test-token";

    /// <summary>The analyst who creates the players.</summary>
    private const long OwnerTelegramId = 701;

    /// <summary>Another analyst, who does not own them.</summary>
    private const long OtherTelegramId = 702;

    /// <summary>An admin.</summary>
    private const long AdminTelegramId = 703;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Stands in for R2 / RustFS.</summary>
    private readonly InMemoryObjectStorage _storage = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own Volleybox id and folder.</summary>
    private int _created;

    /// <summary>Starts PostgreSQL, boots the API with in-memory storage, and stores two analysts and an admin.</summary>
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
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(_storage));
                services.Replace(ServiceDescriptor.Singleton<IDriveFolderReader>(new FakeDriveFolderReader()));
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

        users.Add(User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), DateTimeOffset.UtcNow));
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
    /// The owner uploads a JPEG: the WebP and the original are stored under versioned keys, the player points at the
    /// WebP, and nothing is missing any more. The photo reads back as WebP.
    /// </summary>
    [Fact]
    public async Task Owner_uploads_the_photo()
    {
        var playerId = await CreatePlayerAsync();

        using var response = await PutPhotoAsync(OwnerTelegramId, playerId, Jpeg(1000, 1200), "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var key = body.RootElement.GetProperty("photoKey").GetString()!;
        key.Should().MatchRegex($"^players/{playerId}/photo-[0-9]+\\.webp$");
        body.RootElement.GetProperty("width").GetInt32().Should().Be(1000);
        body.RootElement.GetProperty("height").GetInt32().Should().Be(1200);
        body.RootElement.GetProperty("missingDetails").GetArrayLength().Should().Be(0);
        body.RootElement.GetProperty("isComplete").GetBoolean().Should().BeTrue();
        _storage.ContentTypeOf(key).Should().Be("image/webp");
        _storage.ContentTypeOf(key.Replace(".webp", "-original", StringComparison.Ordinal)).Should().Be("image/jpeg");

        using var photo = await GetPhotoAsync(OwnerTelegramId, playerId);
        photo.StatusCode.Should().Be(HttpStatusCode.OK);
        photo.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        using var image = new MagickImage(await photo.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        image.Format.Should().Be(MagickFormat.WebP);
    }

    /// <summary>Replacing the photo writes new keys and keeps the old objects.</summary>
    [Fact]
    public async Task Replacing_keeps_the_old_photo()
    {
        var playerId = await CreatePlayerAsync();
        using var first = await PutPhotoAsync(OwnerTelegramId, playerId, Jpeg(1000, 1000), "image/jpeg");
        using var firstBody = await ReadJsonAsync(first);
        await Task.Delay(5, TestContext.Current.CancellationToken);

        using var second = await PutPhotoAsync(AdminTelegramId, playerId, Jpeg(900, 1100), "image/jpeg");

        second.StatusCode.Should().Be(HttpStatusCode.OK, "an admin may change any player's photo");
        using var secondBody = await ReadJsonAsync(second);
        secondBody.RootElement.GetProperty("photoKey").GetString().Should().NotBe(firstBody.RootElement.GetProperty("photoKey").GetString());
        _storage.Keys.Should().Contain(firstBody.RootElement.GetProperty("photoKey").GetString()!);
    }

    /// <summary>An analyst cannot upload or read the photo of a player they do not own.</summary>
    [Fact]
    public async Task Other_analyst_is_refused()
    {
        var playerId = await CreatePlayerAsync();
        using var owner = await PutPhotoAsync(OwnerTelegramId, playerId, Jpeg(1000, 1000), "image/jpeg");

        using var upload = await PutPhotoAsync(OtherTelegramId, playerId, Jpeg(1000, 1000), "image/jpeg");
        using var read = await GetPhotoAsync(OtherTelegramId, playerId);

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Unknown players, and players without a photo yet, are 404.</summary>
    [Fact]
    public async Task Missing_player_or_photo_is_404()
    {
        var playerId = await CreatePlayerAsync();

        using var unknownUpload = await PutPhotoAsync(OwnerTelegramId, Guid.CreateVersion7(), Jpeg(1000, 1000), "image/jpeg");
        using var noPhoto = await GetPhotoAsync(OwnerTelegramId, playerId);

        unknownUpload.StatusCode.Should().Be(HttpStatusCode.NotFound);
        noPhoto.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Small photos and things that are not photos are refused with their codes, and nothing is stored.</summary>
    /// <param name="kind">What to send.</param>
    /// <param name="code">The expected code.</param>
    [Theory]
    [InlineData("small", "player.photo_too_small")]
    [InlineData("text", "player.photo_unreadable")]
    public async Task Bad_photos_return_their_code(string kind, string code)
    {
        var playerId = await CreatePlayerAsync();
        var content = kind == "small" ? Jpeg(600, 800) : Encoding.UTF8.GetBytes("not an image");

        using var response = await PutPhotoAsync(OwnerTelegramId, playerId, content, "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
        _storage.Keys.Should().NotContain(key => key.StartsWith($"players/{playerId}/", StringComparison.Ordinal));
    }

    /// <summary>An empty body is a validation error.</summary>
    [Fact]
    public async Task Empty_body_is_400()
    {
        var playerId = await CreatePlayerAsync();

        using var response = await PutPhotoAsync(OwnerTelegramId, playerId, [], "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>A body over 15 MB is refused before it is processed.</summary>
    [Fact]
    public async Task Oversized_body_is_413()
    {
        var playerId = await CreatePlayerAsync();

        using var response = await PutPhotoAsync(OwnerTelegramId, playerId, new byte[(15 * 1024 * 1024) + 1], "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("player.photo_too_large");
    }

    /// <summary>Both endpoints are documented under "Mini App" with the initData security scheme.</summary>
    [Fact]
    public async Task Endpoints_are_documented()
    {
        using var client = _api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));
        var path = document.RootElement.GetProperty("paths").GetProperty("/miniapp/players/{id}/photo");

        foreach (var (method, success) in new[] { ("put", "200"), ("get", "200") })
        {
            var operation = path.GetProperty(method);
            operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
            operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("Mini App");
            operation.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("TelegramMiniApp", out _)).Should().BeTrue();
            var responses = operation.GetProperty("responses");
            responses.TryGetProperty(success, out _).Should().BeTrue();
            responses.TryGetProperty("403", out _).Should().BeTrue();
            responses.TryGetProperty("404", out _).Should().BeTrue();
        }

        var put = path.GetProperty("put");
        put.GetProperty("requestBody").GetProperty("content").TryGetProperty("image/jpeg", out _).Should().BeTrue();
        put.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue();
        put.GetProperty("responses").TryGetProperty("413", out _).Should().BeTrue();
        put.GetProperty("responses").TryGetProperty("422", out _).Should().BeTrue();
        path.GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("image/webp", out _).Should().BeTrue();
    }

    /// <summary>Encodes a plain JPEG.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Jpeg(int width, int height)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, (uint)width, (uint)height);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>Parses a JSON response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The document; the caller disposes it.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>Creates a player owned by the owner analyst.</summary>
    /// <returns>The player id.</returns>
    private async Task<Guid> CreatePlayerAsync()
    {
        var n = Interlocked.Increment(ref _created);
        var form = new
        {
            category = "Men",
            name = $"Photo Player {(char)('a' + n)}",
            nationalityCode = "IR",
            positions = new[] { "OH" },
            volleyboxUrl = $"https://volleybox.net/photo-player-p{70000 + n}",
            driveFolderUrl = $"https://drive.google.com/drive/folders/photoFolder_{n:D4}",
        };
        using var response = await SendAsync(OwnerTelegramId, HttpMethod.Post, "/miniapp/players", JsonContent.Create(form));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a photo.</summary>
    /// <param name="telegramId">Who uploads.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="bytes">The body.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private Task<HttpResponseMessage> PutPhotoAsync(long telegramId, Guid playerId, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return SendAsync(telegramId, HttpMethod.Put, $"/miniapp/players/{playerId}/photo", content);
    }

    /// <summary>Reads a photo.</summary>
    /// <param name="telegramId">Who reads.</param>
    /// <param name="playerId">The player.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private Task<HttpResponseMessage> GetPhotoAsync(long telegramId, Guid playerId) =>
        SendAsync(telegramId, HttpMethod.Get, $"/miniapp/players/{playerId}/photo", null);

    /// <summary>Calls the API as a team member.</summary>
    /// <param name="telegramId">Who calls.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="content">The body, or <see langword="null"/>.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> SendAsync(long telegramId, HttpMethod method, string path, HttpContent? content)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, telegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
