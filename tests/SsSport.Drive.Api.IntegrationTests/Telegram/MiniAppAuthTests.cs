using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// Mini App requests authenticate with Telegram-signed initData in <c>Authorization: tma &lt;initData&gt;</c> (T-3):
/// a bad, missing or stale signature is 401; a correctly signed caller who is not an active team member is 403;
/// an active analyst or admin gets their profile from <c>GET /miniapp/me</c>.
/// </summary>
public sealed class MiniAppAuthTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks signatures with.</summary>
    private const string BotToken = "123456:mini-app-test-token";

    /// <summary>The endpoint under test.</summary>
    private static readonly Uri _me = new("/miniapp/me", UriKind.Relative);

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Starts PostgreSQL, migrates it and boots the API with the test bot token.</summary>
    /// <returns>A task that completes when the API is ready.</returns>
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
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger())));
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

    /// <summary>Without an Authorization header the caller is anonymous: 401 ProblemDetails.</summary>
    [Fact]
    public async Task Missing_init_data_is_401()
    {
        using var response = await GetMeAsync(authorization: null);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    /// <summary>Another authorization scheme is ignored by the Mini App handler: 401.</summary>
    [Fact]
    public async Task Other_scheme_is_401()
    {
        using var response = await GetMeAsync(new AuthenticationHeaderValue("Bearer", "abc"));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    /// <summary>initData signed for another bot is rejected: 401.</summary>
    [Fact]
    public async Task Forged_signature_is_401()
    {
        await SeedActiveAsync(501, Role.Analyst);

        using var response = await GetMeAsync(Tma(InitDataSigner.ForUser("999:other-bot", 501, DateTimeOffset.UtcNow)));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    /// <summary>initData older than 24 hours is rejected: 401.</summary>
    [Fact]
    public async Task Stale_init_data_is_401()
    {
        await SeedActiveAsync(502, Role.Analyst);

        using var response = await GetMeAsync(Tma(InitDataSigner.ForUser(BotToken, 502, DateTimeOffset.UtcNow.AddHours(-25))));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    /// <summary>A real Telegram user who never asked for access is signed in but has no role: 403.</summary>
    [Fact]
    public async Task Unknown_telegram_user_is_403()
    {
        using var response = await GetMeAsync(Tma(InitDataSigner.ForUser(BotToken, 503, DateTimeOffset.UtcNow)));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    /// <summary>A user still waiting for approval cannot use the Mini App: 403.</summary>
    [Fact]
    public async Task Pending_user_is_403()
    {
        await SeedAsync(User.RequestAccess(504, "pending_user", "Pending User", DateTimeOffset.UtcNow));

        using var response = await GetMeAsync(Tma(InitDataSigner.ForUser(BotToken, 504, DateTimeOffset.UtcNow)));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    /// <summary>A removed user loses Mini App access at once, even with fresh initData: 403.</summary>
    [Fact]
    public async Task Removed_user_is_403()
    {
        var user = User.RequestAccess(505, "gone", "Gone User", DateTimeOffset.UtcNow);
        user.Approve(Role.Analyst);
        user.Remove();
        await SeedAsync(user);

        using var response = await GetMeAsync(Tma(InitDataSigner.ForUser(BotToken, 505, DateTimeOffset.UtcNow)));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    /// <summary>An active analyst gets their profile, read from the database (not from the initData).</summary>
    [Fact]
    public async Task Active_analyst_gets_their_profile()
    {
        var id = await SeedActiveAsync(506, Role.Analyst);

        using var response = await GetMeAsync(Tma(InitDataSigner.ForUser(BotToken, 506, DateTimeOffset.UtcNow, firstName: "Renamed")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("id").GetGuid().Should().Be(id);
        body.RootElement.GetProperty("telegramId").GetInt64().Should().Be(506);
        body.RootElement.GetProperty("displayName").GetString().Should().Be("Analyst 506");
        body.RootElement.GetProperty("username").GetString().Should().Be("user_506");
        body.RootElement.GetProperty("role").GetString().Should().Be("Analyst");
        body.RootElement.TryGetProperty("email", out _).Should().BeFalse("the Mini App never needs the email");
    }

    /// <summary>An active admin is recognised as Admin.</summary>
    [Fact]
    public async Task Active_admin_gets_the_admin_role()
    {
        await SeedAsync(User.CreateFirstAdmin(507, "Owner", EmailAddress.Create("owner@ss-sport.co"), DateTimeOffset.UtcNow));

        using var response = await GetMeAsync(Tma(InitDataSigner.ForUser(BotToken, 507, DateTimeOffset.UtcNow)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("role").GetString().Should().Be("Admin");
    }

    /// <summary>The endpoint is documented under "Mini App" with its responses and the initData security scheme.</summary>
    [Fact]
    public async Task Endpoint_is_documented()
    {
        using var client = _api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));

        var operation = document.RootElement.GetProperty("paths").GetProperty("/miniapp/me").GetProperty("get");
        operation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        operation.GetProperty("description").GetString().Should().Contain("tma");
        operation.GetProperty("operationId").GetString().Should().Be("GetMiniAppProfile");
        operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("Mini App");
        var responses = operation.GetProperty("responses");
        responses.GetProperty("200").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _).Should().BeTrue();
        responses.GetProperty("401").GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();
        responses.GetProperty("403").GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();
        operation.GetProperty("security").EnumerateArray().Any(requirement => requirement.TryGetProperty("TelegramMiniApp", out _)).Should().BeTrue();

        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("TelegramMiniApp");
        scheme.GetProperty("in").GetString().Should().Be("header");
        scheme.GetProperty("name").GetString().Should().Be("Authorization");
    }

    /// <summary>Builds the Mini App Authorization header.</summary>
    /// <param name="initData">The signed initData.</param>
    /// <returns>The header value.</returns>
    private static AuthenticationHeaderValue Tma(string initData) => new("tma", initData);

    /// <summary>Asserts a ProblemDetails response with the given status.</summary>
    /// <param name="response">The response.</param>
    /// <param name="status">The expected status.</param>
    /// <returns>A task that completes when checked.</returns>
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
    }

    /// <summary>Calls <c>GET /miniapp/me</c>.</summary>
    /// <param name="authorization">The Authorization header, or <see langword="null"/> for none.</param>
    /// <returns>The response; the caller disposes it.</returns>
    private async Task<HttpResponseMessage> GetMeAsync(AuthenticationHeaderValue? authorization)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, _me);
        request.Headers.Authorization = authorization;
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Stores an approved user.</summary>
    /// <param name="telegramId">The Telegram id.</param>
    /// <param name="role">The role to approve as.</param>
    /// <returns>The user's id.</returns>
    private async Task<Guid> SeedActiveAsync(long telegramId, Role role)
    {
        var user = User.RequestAccess(telegramId, $"user_{telegramId}", $"Analyst {telegramId}", DateTimeOffset.UtcNow);
        user.Approve(role);
        await SeedAsync(user);
        return user.Id;
    }

    /// <summary>Stores a user through the repository and unit of work.</summary>
    /// <param name="user">The user.</param>
    /// <returns>A task that completes when saved.</returns>
    private async Task SeedAsync(User user)
    {
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
