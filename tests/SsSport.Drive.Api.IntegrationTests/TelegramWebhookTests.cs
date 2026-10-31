using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// <c>POST /telegram/webhook</c> accepts updates only with the configured secret token, records each update once and
/// always answers 200 to Telegram for authenticated deliveries, so Telegram never retries in a loop.
/// </summary>
public sealed class TelegramWebhookTests : IAsyncLifetime
{
    /// <summary>The secret configured for the tests.</summary>
    private const string Secret = "test_Secret-123";

    /// <summary>The header Telegram sends the secret in.</summary>
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Stands in for Telegram, so the bot's replies never reach the real Bot API.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>Starts PostgreSQL and applies the migrations.</summary>
    /// <returns>A task that completes when the database is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var factory = CreateApi(Secret);
        await DatabaseMigrator.MigrateAsync(factory.Services, TestContext.Current.CancellationToken);
    }

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>Without the secret header the request is rejected with 401 and nothing is recorded.</summary>
    [Fact]
    public async Task Missing_secret_is_rejected()
    {
        await using var factory = CreateApi(Secret);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/telegram/webhook", UriKind.Relative), Update(5001), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountAsync(5001)).Should().Be(0);
    }

    /// <summary>A wrong secret is rejected with 401.</summary>
    [Fact]
    public async Task Wrong_secret_is_rejected()
    {
        await using var factory = CreateApi(Secret);
        using var response = await PostAsync(factory, Update(5002), "wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>When no secret is configured, every request is rejected (fail closed).</summary>
    [Fact]
    public async Task No_configured_secret_rejects_everything()
    {
        await using var factory = CreateApi(secret: "");
        using var response = await PostAsync(factory, Update(5003), "");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A valid update is answered 200 and recorded; the same update delivered again is answered 200 and recorded once.</summary>
    [Fact]
    public async Task Valid_update_is_recorded_once()
    {
        await using var factory = CreateApi(Secret);

        using (var first = await PostAsync(factory, Update(5004), Secret))
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var repeat = await PostAsync(factory, Update(5004), Secret))
        {
            repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await CountAsync(5004)).Should().Be(1);
        _bot.Calls.Should().NotBeEmpty("the update reached the bot, which answered through the stand-in messenger");
    }

    /// <summary>A body that is not a Telegram update is answered 400 with ProblemDetails.</summary>
    [Fact]
    public async Task Malformed_body_is_a_bad_request()
    {
        await using var factory = CreateApi(Secret);
        using var body = new StringContent("{not json", Encoding.UTF8, "application/json");

        using var response = await PostAsync(factory, body, Secret);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>The endpoint is documented under "Telegram" with its responses and the secret-header security scheme.</summary>
    [Fact]
    public async Task Webhook_is_documented()
    {
        await using var factory = CreateApi(Secret);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));

        var operation = document.RootElement.GetProperty("paths").GetProperty("/telegram/webhook").GetProperty("post");
        operation.GetProperty("operationId").GetString().Should().Be("ReceiveTelegramUpdate");
        operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("Telegram");
        var responses = operation.GetProperty("responses");
        responses.TryGetProperty("200", out _).Should().BeTrue();
        responses.GetProperty("400").GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();
        responses.GetProperty("401").GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();
        operation.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("TelegramWebhookSecret", out _)).Should().BeTrue();
        operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue();

        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("TelegramWebhookSecret");
        scheme.GetProperty("type").GetString().Should().Be("apiKey");
        scheme.GetProperty("in").GetString().Should().Be("header");
        scheme.GetProperty("name").GetString().Should().Be(SecretHeader);
    }

    /// <summary>Builds a minimal Telegram text-message update.</summary>
    /// <param name="updateId">The update id.</param>
    /// <returns>The JSON body.</returns>
    private static JsonContent Update(long updateId) => JsonContent.Create(new
    {
        update_id = updateId,
        message = new
        {
            message_id = 1,
            date = 1761379200,
            chat = new { id = 42, type = "private" },
            from = new { id = 42, is_bot = false, first_name = "Reza" },
            text = "/start",
        },
    });

    /// <summary>Posts to the webhook with a secret header.</summary>
    /// <param name="factory">The API host.</param>
    /// <param name="body">The body.</param>
    /// <param name="secret">The secret to send.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> PostAsync(WebApplicationFactory<Program> factory, HttpContent body, string secret)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/telegram/webhook", UriKind.Relative)) { Content = body };
        request.Headers.Add(SecretHeader, secret);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Counts the recorded rows for an update id.</summary>
    /// <param name="updateId">The update id.</param>
    /// <returns>The row count.</returns>
    private async Task<long> CountAsync(long updateId)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM telegram_updates WHERE update_id = @id", connection);
        command.Parameters.AddWithValue("id", updateId);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Creates the in-memory API against the test database with a given webhook secret. The bot answers through
    /// <see cref="RecordingMessenger"/>, never the real Bot API.
    /// </summary>
    /// <param name="secret">The configured secret; empty disables the webhook.</param>
    /// <returns>The API host.</returns>
    private WebApplicationFactory<Program> CreateApi(string secret) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:WebhookSecret"] = secret,
                // CI has no bot token; blank it here too, so a developer's .env cannot hide a real Bot API call.
                ["Telegram:BotToken"] = string.Empty,
            }))
            .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot)).UseInlineBotNotices()));
}
