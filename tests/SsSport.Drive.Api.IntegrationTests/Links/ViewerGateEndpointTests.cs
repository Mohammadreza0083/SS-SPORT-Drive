using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Links;

/// <summary>
/// The email gate (L-2): a viewer asks for a 6-digit code for an email the link admits, types it in, and gets a device
/// remembered for 90 days and a one-day session. Codes last 10 minutes and 5 tries; new codes wait 60 seconds and are
/// capped per hour. Tokens are stored only as hashes.
/// </summary>
public sealed class ViewerGateEndpointTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    private const string BotToken = "123456:viewer-gate-test-token";

    /// <summary>An admin, who creates the links.</summary>
    private const long AdminTelegramId = 801;

    /// <summary>The recipient's email.</summary>
    private const string RecipientEmail = "marco@lube.it";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The clock the API reads; tests move it forward.</summary>
    private readonly ShiftableClock _clock = new();

    /// <summary>Catches the codes the API sends.</summary>
    private readonly RecordingEmailCodeSender _mail = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own name and Volleybox id.</summary>
    private int _players;

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
                ["Telegram:WebhookSecret"] = "viewer-gate-secret",
                ["Telegram:Mode"] = "Webhook",
                ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["RateLimiting:LinkPermitsPerMinute"] = "1000",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.Replace(ServiceDescriptor.Singleton<IEmailCodeSender>(_mail));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.UseInlineBotNotices();
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
            .Add(User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), _clock.GetUtcNow()));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Before the gate, a Personal link shows the player's name, position and nationality, and nothing else about them.</summary>
    [Fact]
    public async Task The_gate_card_shows_the_player()
    {
        var code = await CreateLinkAsync();

        using var response = await Client().GetAsync(new Uri($"/links/{code}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var player = body.RootElement.GetProperty("player");
        player.GetProperty("name").GetString().Should().Be("Arash Tehrani");
        player.GetProperty("position").GetString().Should().Be("OPP");
        player.GetProperty("nationality").GetProperty("code").GetString().Should().Be("IR");
        player.GetProperty("nationality").GetProperty("name").GetString().Should().Be("Iran");
        player.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("name", "position", "nationality");
    }

    /// <summary>A Roster link shows only how many players it holds.</summary>
    [Fact]
    public async Task A_roster_gate_shows_no_player()
    {
        var code = await CreateLinkAsync(players: 2);

        using var response = await Client().GetAsync(new Uri($"/links/{code}", UriKind.Relative), TestContext.Current.CancellationToken);

        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("playerCount").GetInt32().Should().Be(2);
        body.RootElement.GetProperty("player").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>The recipient asks for a code: one 6-digit code is mailed, and only its hash is stored.</summary>
    [Fact]
    public async Task The_recipient_gets_a_code_by_email()
    {
        var code = await CreateLinkAsync();

        using var response = await PostAsync($"/links/{code}/email-code", new { email = "Marco@Lube.it" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("email").GetString().Should().Be("m***@lube.it");
        body.RootElement.GetProperty("resendAfterSeconds").GetInt32().Should().Be(60);
        body.RootElement.GetProperty("expiresInSeconds").GetInt32().Should().Be(600);

        var sent = _mail.Sent.Should().ContainSingle().Subject;
        sent.To.Value.Should().Be(RecipientEmail);
        sent.Code.Should().MatchRegex("^[0-9]{6}$");
        var stored = await QueryAsync(db => db.Set<EmailChallenge>().Select(challenge => challenge.CodeHash).SingleAsync(TestContext.Current.CancellationToken));
        stored.Should().NotBeNullOrEmpty().And.NotContain(sent.Code);
    }

    /// <summary>The right code opens: a device token and a session token, both stored only as hashes; the session tells who it is.</summary>
    [Fact]
    public async Task The_right_code_opens_the_link()
    {
        var code = await CreateLinkAsync();

        var access = await VerifyAsync(code, RecipientEmail);

        access.DeviceToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        access.SessionToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        access.DeviceExpiresAt.Should().Be(_clock.GetUtcNow().AddDays(90));
        access.SessionExpiresAt.Should().Be(_clock.GetUtcNow().AddDays(1));

        using var session = await SessionAsync(code, access.SessionToken);
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(session);
        body.RootElement.GetProperty("email").GetString().Should().Be(RecipientEmail);
        body.RootElement.GetProperty("isForward").GetBoolean().Should().BeFalse();

        var deviceHashes = await QueryAsync(db => db.Set<TrustedDevice>().Select(device => device.TokenHash).ToListAsync(TestContext.Current.CancellationToken));
        var sessionHashes = await QueryAsync(db => db.Set<ViewerSession>().Select(item => item.TokenHash).ToListAsync(TestContext.Current.CancellationToken));
        deviceHashes.Should().ContainSingle().Which.Should().NotBe(access.DeviceToken);
        sessionHashes.Should().ContainSingle().Which.Should().NotBe(access.SessionToken);
    }

    /// <summary>A trusted device gets a new session without a code, even after the first session ran out.</summary>
    [Fact]
    public async Task A_trusted_device_opens_without_a_code()
    {
        var code = await CreateLinkAsync();
        var access = await VerifyAsync(code, RecipientEmail);
        _clock.Advance(TimeSpan.FromDays(2));

        using var expired = await SessionAsync(code, access.SessionToken);
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var response = await OpenAsync(code, access.DeviceToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("deviceToken").ValueKind.Should().Be(JsonValueKind.Null);
        var sessionToken = body.RootElement.GetProperty("sessionToken").GetString()!;
        using var session = await SessionAsync(code, sessionToken);
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        _mail.Sent.Should().ContainSingle();
    }

    /// <summary>An unknown device, a device trusted for another link, and a device past 90 days all need the gate.</summary>
    [Fact]
    public async Task Other_devices_need_the_gate()
    {
        var code = await CreateLinkAsync(expiresInDays: null);
        var otherCode = await CreateLinkAsync();
        var access = await VerifyAsync(code, RecipientEmail);

        using (var unknown = await OpenAsync(code, "not-a-device-token-at-all-aaaaaaaaaaaaaaaaaa"))
        {
            unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            using var body = await ReadJsonAsync(unknown);
            body.RootElement.GetProperty("code").GetString().Should().Be("viewer.verification_required");
        }

        using (var none = await OpenAsync(code, null))
        {
            none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var otherLink = await OpenAsync(otherCode, access.DeviceToken))
        {
            otherLink.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var otherSession = await SessionAsync(otherCode, access.SessionToken))
        {
            otherSession.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        _clock.Advance(TimeSpan.FromDays(90));
        using var old = await OpenAsync(code, access.DeviceToken);
        old.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Verifying on a device that already has a token keeps that token and trusts it for the new link too.</summary>
    [Fact]
    public async Task One_device_token_serves_several_links()
    {
        var first = await CreateLinkAsync();
        var second = await CreateLinkAsync();
        var access = await VerifyAsync(first, RecipientEmail);
        _clock.Advance(TimeSpan.FromMinutes(2));

        var again = await VerifyAsync(second, RecipientEmail, access.DeviceToken);

        again.DeviceToken.Should().Be(access.DeviceToken);
        using var reopened = await OpenAsync(first, access.DeviceToken);
        reopened.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Wrong codes count down from 5; the fifth spends the code and even the right one is refused after it.</summary>
    [Fact]
    public async Task Five_wrong_codes_spend_the_code()
    {
        var code = await CreateLinkAsync();
        using (await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail }))
        {
        }

        var right = _mail.Sent.Single().Code;
        var wrong = right == "000000" ? "111111" : "000000";
        for (var left = 4; left > 0; left--)
        {
            using var response = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = wrong });
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            using var body = await ReadJsonAsync(response);
            body.RootElement.GetProperty("code").GetString().Should().Be("email_code.invalid");
            body.RootElement.GetProperty("attemptsLeft").GetInt32().Should().Be(left);
        }

        using (var fifth = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = wrong }))
        {
            using var body = await ReadJsonAsync(fifth);
            body.RootElement.GetProperty("code").GetString().Should().Be("email_code.exhausted");
        }

        using var late = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = right });
        late.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var lateBody = await ReadJsonAsync(late);
        lateBody.RootElement.GetProperty("code").GetString().Should().Be("email_code.exhausted");
    }

    /// <summary>A code is good for 10 minutes; a code for another email does not open.</summary>
    [Fact]
    public async Task Codes_expire_and_belong_to_one_email()
    {
        var code = await CreateLinkAsync();
        using (await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail }))
        {
        }

        var sent = _mail.Sent.Single().Code;
        using (var otherEmail = await PostAsync($"/links/{code}/verify", new { email = "scout@other.com", code = sent }))
        {
            using var body = await ReadJsonAsync(otherEmail);
            body.RootElement.GetProperty("code").GetString().Should().Be("email_code.expired");
        }

        _clock.Advance(TimeSpan.FromMinutes(10));
        using var response = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = sent });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var expired = await ReadJsonAsync(response);
        expired.RootElement.GetProperty("code").GetString().Should().Be("email_code.expired");
    }

    /// <summary>A new code waits 60 seconds, and an email gets at most 5 codes per link per hour.</summary>
    [Fact]
    public async Task New_codes_are_throttled()
    {
        var code = await CreateLinkAsync();
        using (await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail }))
        {
        }

        using (var tooSoon = await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail }))
        {
            tooSoon.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            tooSoon.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(60));
            using var body = await ReadJsonAsync(tooSoon);
            body.RootElement.GetProperty("code").GetString().Should().Be("email_code.too_many");
        }

        for (var i = 0; i < 4; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(61));
            using var response = await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail });
            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        _clock.Advance(TimeSpan.FromSeconds(61));
        using var sixth = await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail });
        sixth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        _mail.Sent.Should().HaveCount(5);
    }

    /// <summary>Only the newest code opens: asking again replaces the code before it.</summary>
    [Fact]
    public async Task Only_the_newest_code_opens()
    {
        var code = await CreateLinkAsync();
        using (await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail }))
        {
        }

        _clock.Advance(TimeSpan.FromSeconds(61));
        using (await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail }))
        {
        }

        var codes = _mail.Sent.Select(sent => sent.Code).ToList();
        if (codes[0] != codes[1])
        {
            using var old = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = codes[0] });
            old.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        using var newest = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = codes[1] });
        newest.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A forwarded link opens for another email after the code, and the session says it is a forward.</summary>
    [Fact]
    public async Task A_forward_is_admitted_and_marked()
    {
        var code = await CreateLinkAsync();

        var access = await VerifyAsync(code, "scout@other.com");

        using var session = await SessionAsync(code, access.SessionToken);
        using var body = await ReadJsonAsync(session);
        body.RootElement.GetProperty("email").GetString().Should().Be("scout@other.com");
        body.RootElement.GetProperty("isForward").GetBoolean().Should().BeTrue();
    }

    /// <summary>With forwarding off, another email is refused before any code is sent.</summary>
    [Fact]
    public async Task Without_forwarding_other_emails_are_refused()
    {
        var code = await CreateLinkAsync(allowForwarding: false);

        using var response = await PostAsync($"/links/{code}/email-code", new { email = "scout@other.com" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("share_link.email_not_allowed");
        _mail.Sent.Should().BeEmpty();
    }

    /// <summary>A link that does not verify new devices opens without the gate, as the recipient's first email.</summary>
    [Fact]
    public async Task A_link_without_verification_opens_at_once()
    {
        var code = await CreateLinkAsync(verifyOnNewDevice: false);

        using var response = await OpenAsync(code, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        using var session = await SessionAsync(code, body.RootElement.GetProperty("sessionToken").GetString()!);
        using var sessionBody = await ReadJsonAsync(session);
        sessionBody.RootElement.GetProperty("email").GetString().Should().Be(RecipientEmail);
    }

    /// <summary>Once a link stops opening, its codes, devices and sessions all answer the same 404.</summary>
    [Fact]
    public async Task A_paused_link_closes_the_gate()
    {
        var code = await CreateLinkAsync();
        var access = await VerifyAsync(code, RecipientEmail);
        var id = await QueryAsync(db => db.Set<ShareLink>().Where(link => link.Code == ShareLinkCode.Create(code)).Select(link => link.Id).SingleAsync(TestContext.Current.CancellationToken));
        using (await AdminPostAsync($"/admin/links/{id}/pause"))
        {
        }

        using var ask = await PostAsync($"/links/{code}/email-code", new { email = RecipientEmail });
        using var open = await OpenAsync(code, access.DeviceToken);
        using var session = await SessionAsync(code, access.SessionToken);
        using var verify = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = "123456" });

        foreach (var response in new[] { ask, open, session, verify })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var body = await ReadJsonAsync(response);
            body.RootElement.GetProperty("code").GetString().Should().Be("share_link.unavailable");
        }
    }

    /// <summary>Malformed forms are 400; a malformed email is the email rule's 422.</summary>
    [Theory]
    [InlineData("/email-code", """{ }""", 400, "email")]
    [InlineData("/verify", """{ "email": "marco@lube.it", "code": "12345" }""", 400, "code")]
    [InlineData("/verify", """{ "email": "marco@lube.it", "code": "12a456" }""", 400, "code")]
    [InlineData("/email-code", """{ "email": "not-an-email" }""", 422, "email.invalid")]
    public async Task Bad_forms_are_refused(string path, string json, int status, string what)
    {
        var code = await CreateLinkAsync();
        using var client = Client();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri($"/links/{code}{path}", UriKind.Relative), content, TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().Be(status);
        using var body = await ReadJsonAsync(response);
        if (status == 400)
        {
            body.RootElement.GetProperty("errors").TryGetProperty(what, out _).Should().BeTrue();
        }
        else
        {
            body.RootElement.GetProperty("code").GetString().Should().Be(what);
        }
    }

    /// <summary>A device token far longer than a real one is refused before anything is looked up.</summary>
    [Fact]
    public async Task An_oversized_device_token_is_400()
    {
        var code = await CreateLinkAsync();

        using var response = await PostAsync($"/links/{code}/verify", new { email = RecipientEmail, code = "123456" }, new string('a', 129));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("errors").TryGetProperty("deviceToken", out _).Should().BeTrue();
    }

    /// <summary>Asks for a code, reads it from the mail and verifies it.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="email">The email.</param>
    /// <param name="deviceToken">The device token the browser already has, or <see langword="null"/>.</param>
    /// <returns>The tokens.</returns>
    private async Task<Access> VerifyAsync(string code, string email, string? deviceToken = null)
    {
        using (var asked = await PostAsync($"/links/{code}/email-code", new { email }))
        {
            asked.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var sent = _mail.Sent.Last(item => item.To.Value == email).Code;
        using var response = await PostAsync($"/links/{code}/verify", new { email, code = sent }, deviceToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        return new Access(
            root.GetProperty("deviceToken").GetString()!,
            root.GetProperty("deviceExpiresAt").GetDateTimeOffset(),
            root.GetProperty("sessionToken").GetString()!,
            root.GetProperty("sessionExpiresAt").GetDateTimeOffset());
    }

    /// <summary>Asks for a session on a device.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="deviceToken">The device token, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    private Task<HttpResponseMessage> OpenAsync(string code, string? deviceToken) => PostAsync($"/links/{code}/session", null, deviceToken);

    /// <summary>Reads a session.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="sessionToken">The session token.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> SessionAsync(string code, string sessionToken)
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/links/{code}/session", UriKind.Relative));
        request.Headers.Add("X-Viewer-Session", sessionToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Posts JSON to a public endpoint, with a device token header when given.</summary>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <param name="deviceToken">The device token, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? deviceToken = null)
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        if (deviceToken is not null)
        {
            request.Headers.Add("X-Viewer-Device", deviceToken);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Posts as the admin.</summary>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> AdminPostAsync(string path, object? body = null)
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("tma", InitDataSigner.ForUser(BotToken, AdminTelegramId, DateTimeOffset.UtcNow));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Creates published players and a link to them for the recipient, and returns its code.</summary>
    /// <param name="players">How many players.</param>
    /// <param name="allowForwarding">The forwarding switch.</param>
    /// <param name="verifyOnNewDevice">The device switch.</param>
    /// <param name="expiresInDays">7, 30 or 90, or <see langword="null"/> for a link that never expires.</param>
    /// <returns>The code.</returns>
    private async Task<string> CreateLinkAsync(int players = 1, bool allowForwarding = true, bool verifyOnNewDevice = true, int? expiresInDays = 30)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < players; i++)
        {
            ids.Add(await AddPlayerAsync());
        }

        using var response = await AdminPostAsync("/admin/links", new
        {
            recipient = new { name = "Marco Rossi", organisation = "Lube Volley", emails = new[] { RecipientEmail } },
            playerIds = ids,
            expiresInDays,
            allowForwarding,
            verifyOnNewDevice,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Stores a published opposite from Iran: the first is Arash Tehrani, the others get their own names and Volleybox ids.</summary>
    /// <returns>The player id.</returns>
    private async Task<Guid> AddPlayerAsync()
    {
        var n = Interlocked.Increment(ref _players);
        var player = Player.Create(PlayerCategory.Men, _clock.GetUtcNow());
        var name = FullName.Create(n == 1 ? "Arash Tehrani" : $"Arash Tehrani {(char)('A' + n)}{(char)('a' + n)}");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Opposite, Position.OutsideHitter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/arash-tehrani-p{80000 + n}"));
        player.Publish(_clock.GetUtcNow());

        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().Add(player);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return player.Id;
    }

    /// <summary>Runs a read against the database.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="query">The query.</param>
    /// <returns>The result.</returns>
    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A client for the API.</summary>
    /// <returns>The client.</returns>
    private HttpClient Client() => _api.CreateClient();

    /// <summary>Parses a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON document.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>The tokens a verified viewer gets.</summary>
    /// <param name="DeviceToken">The device token.</param>
    /// <param name="DeviceExpiresAt">When the device is forgotten.</param>
    /// <param name="SessionToken">The session token.</param>
    /// <param name="SessionExpiresAt">When the session ends.</param>
    private sealed record Access(string DeviceToken, DateTimeOffset DeviceExpiresAt, string SessionToken, DateTimeOffset SessionExpiresAt);

    /// <summary>Records the codes instead of mailing them.</summary>
    private sealed class RecordingEmailCodeSender : IEmailCodeSender
    {
        /// <summary>The mails, in order.</summary>
        private readonly ConcurrentQueue<(EmailAddress To, string Code)> _sent = new();

        /// <summary>Gets the mails, in order.</summary>
        public IReadOnlyList<(EmailAddress To, string Code)> Sent => [.. _sent];

        /// <inheritdoc />
        public Task SendAsync(EmailAddress to, string code, CancellationToken cancellationToken)
        {
            _sent.Enqueue((to, code));
            return Task.CompletedTask;
        }
    }

    /// <summary>A clock that starts at a whole second now and moves only when told.</summary>
    private sealed class ShiftableClock : TimeProvider
    {
        /// <summary>The current time.</summary>
        private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>Moves the clock forward.</summary>
        /// <param name="by">How far.</param>
        public void Advance(TimeSpan by) => _now += by;
    }
}
