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
using SsSport.Drive.Domain.Audit;
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
/// capped per hour. Tokens are stored only as hashes. Forwards (L-3) wait for an admin's Keep in the bot before they open;
/// Block shuts the email out of the link.
/// </summary>
public sealed class ViewerGateEndpointTests : IAsyncLifetime
{
    /// <summary>The bot token the API checks Mini App signatures with.</summary>
    private const string BotToken = "123456:viewer-gate-test-token";

    /// <summary>An admin, who creates the links.</summary>
    private const long AdminTelegramId = 801;

    /// <summary>An analyst, who may not decide on forwards.</summary>
    private const long AnalystTelegramId = 802;

    /// <summary>The webhook secret.</summary>
    private const string WebhookSecret = "viewer-gate-secret";

    /// <summary>A forwarded email.</summary>
    private const string ForwardEmail = "scout@other.com";

    /// <summary>The recipient's email.</summary>
    private const string RecipientEmail = "marco@lube.it";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The clock the API reads; tests move it forward.</summary>
    private readonly ShiftableClock _clock = new();

    /// <summary>Catches the codes the API sends.</summary>
    private readonly RecordingEmailCodeSender _mail = new();

    /// <summary>Records the bot's messages.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>The last Telegram update id used.</summary>
    private int _updateId;

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
                ["Telegram:WebhookSecret"] = WebhookSecret,
                ["Telegram:Mode"] = "Webhook",
                ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["RateLimiting:LinkPermitsPerMinute"] = "1000",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot));
                services.Replace(ServiceDescriptor.Singleton<IEmailCodeSender>(_mail));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.UseInlineBotNotices();
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);

        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        users.Add(User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), _clock.GetUtcNow()));
        var analyst = User.RequestAccess(AnalystTelegramId, "analyst", "Analyst", _clock.GetUtcNow());
        analyst.Approve(Role.Analyst);
        users.Add(analyst);
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

    /// <summary>
    /// A forward's right code trusts the device but opens nothing: no session until an admin keeps it. Every active admin
    /// is told in the bot, with the link, the recipient, the player and the full email, and Keep / Block buttons.
    /// </summary>
    [Fact]
    public async Task A_forward_waits_for_an_admin()
    {
        var code = await CreateLinkAsync();

        using var verified = await VerifyRawAsync(code, ForwardEmail);

        var root = verified.RootElement;
        root.GetProperty("awaitingApproval").GetBoolean().Should().BeTrue();
        root.GetProperty("deviceToken").GetString().Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        root.GetProperty("sessionToken").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("sessionExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await QueryAsync(db => db.Set<ViewerSession>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);

        using var open = await OpenAsync(code, root.GetProperty("deviceToken").GetString());
        open.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var openBody = await ReadJsonAsync(open);
        openBody.RootElement.GetProperty("code").GetString().Should().Be("share_link.forward_pending");

        var alert = ForwardAlert();
        alert.Text.Should().Contain(code).And.Contain(ForwardEmail).And.Contain("Marco Rossi").And.Contain("Arash Tehrani");
        alert.Keyboard!.Rows.SelectMany(row => row).Should().HaveCount(2);
        ForwardAlerts(AnalystTelegramId).Should().BeEmpty();
    }

    /// <summary>Keep opens the link on the waiting device, as a forward; the alert says who kept it and the decision is audited.</summary>
    [Fact]
    public async Task Keep_opens_the_forward()
    {
        var code = await CreateLinkAsync();
        using var verified = await VerifyRawAsync(code, ForwardEmail);
        var deviceToken = verified.RootElement.GetProperty("deviceToken").GetString();

        await PressAsync(AdminTelegramId, ForwardButton(0));

        using var open = await OpenAsync(code, deviceToken);
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        using var openBody = await ReadJsonAsync(open);
        using var session = await SessionAsync(code, openBody.RootElement.GetProperty("sessionToken").GetString()!);
        using var sessionBody = await ReadJsonAsync(session);
        sessionBody.RootElement.GetProperty("email").GetString().Should().Be(ForwardEmail);
        sessionBody.RootElement.GetProperty("isForward").GetBoolean().Should().BeTrue();

        var edit = _bot.ToChat(AdminTelegramId).Last(call => call.Kind == "Edit");
        edit.Text.Should().Contain("Owner").And.Contain(ForwardEmail);
        edit.Keyboard.Should().BeNull();
        var actions = await QueryAsync(db => db.Set<AuditEntry>().Select(entry => entry.Action).ToListAsync(TestContext.Current.CancellationToken));
        actions.Should().Contain("share_link.forward_kept");
    }

    /// <summary>Once kept, the forward verifies on another device and opens at once, and the admins are not told again.</summary>
    [Fact]
    public async Task A_kept_forward_opens_on_other_devices_without_a_new_alert()
    {
        var code = await CreateLinkAsync();
        using (await VerifyRawAsync(code, ForwardEmail))
        {
        }

        await PressAsync(AdminTelegramId, ForwardButton(0));
        _clock.Advance(TimeSpan.FromSeconds(61));

        using var again = await VerifyRawAsync(code, ForwardEmail);

        again.RootElement.GetProperty("awaitingApproval").GetBoolean().Should().BeFalse();
        again.RootElement.GetProperty("sessionToken").GetString().Should().NotBeNullOrEmpty();
        ForwardAlerts(AdminTelegramId).Should().ContainSingle();
    }

    /// <summary>Block forgets the email's devices on the link, and the email cannot ask for a code there again.</summary>
    [Fact]
    public async Task Block_shuts_the_forward_out()
    {
        var code = await CreateLinkAsync();
        using var verified = await VerifyRawAsync(code, ForwardEmail);
        var deviceToken = verified.RootElement.GetProperty("deviceToken").GetString();

        await PressAsync(AdminTelegramId, ForwardButton(1));

        using var open = await OpenAsync(code, deviceToken);
        open.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await QueryAsync(db => db.Set<TrustedDevice>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);

        _clock.Advance(TimeSpan.FromSeconds(61));
        using var ask = await PostAsync($"/links/{code}/email-code", new { email = ForwardEmail });
        ask.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var askBody = await ReadJsonAsync(ask);
        askBody.RootElement.GetProperty("code").GetString().Should().Be("share_link.email_not_allowed");

        var actions = await QueryAsync(db => db.Set<AuditEntry>().Select(entry => entry.Action).ToListAsync(TestContext.Current.CancellationToken));
        actions.Should().Contain("share_link.forward_blocked");
    }

    /// <summary>A code asked for before the block cannot be used after it.</summary>
    [Fact]
    public async Task A_code_from_before_the_block_does_not_open()
    {
        var code = await CreateLinkAsync();
        using (await VerifyRawAsync(code, ForwardEmail))
        {
        }

        _clock.Advance(TimeSpan.FromSeconds(61));
        using (var asked = await PostAsync($"/links/{code}/email-code", new { email = ForwardEmail }))
        {
            asked.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        await PressAsync(AdminTelegramId, ForwardButton(1));
        var sent = _mail.Sent.Last(item => item.To.Value == ForwardEmail).Code;

        using var response = await PostAsync($"/links/{code}/verify", new { email = ForwardEmail, code = sent });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("share_link.email_not_allowed");
        (await QueryAsync(db => db.Set<TrustedDevice>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    /// <summary>Only admins decide: an analyst's press is refused and the forward keeps waiting; a second decision changes nothing.</summary>
    [Fact]
    public async Task Only_the_first_admin_decision_counts()
    {
        var code = await CreateLinkAsync();
        using var verified = await VerifyRawAsync(code, ForwardEmail);
        var deviceToken = verified.RootElement.GetProperty("deviceToken").GetString();

        await PressAsync(AnalystTelegramId, ForwardButton(1));
        _bot.Calls.Should().ContainSingle(call => call.Kind == "Answer" && call.ShowAlert);
        using (var stillWaiting = await OpenAsync(code, deviceToken))
        {
            stillWaiting.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        await PressAsync(AdminTelegramId, ForwardButton(0));
        await PressAsync(AdminTelegramId, ForwardButton(1));

        using var open = await OpenAsync(code, deviceToken);
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        _bot.ToChat(AdminTelegramId).Last(call => call.Kind == "Edit").Text.Should().Contain("already");
    }

    /// <summary>The recipient's own email never counts as a forward: no alert.</summary>
    [Fact]
    public async Task The_recipient_is_not_a_forward()
    {
        var code = await CreateLinkAsync();

        await VerifyAsync(code, RecipientEmail);

        ForwardAlerts(AdminTelegramId).Should().BeEmpty();
        (await QueryAsync(db => db.Set<LinkForward>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
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

    /// <summary>Asks for a code, reads it from the mail and verifies it; expects 200 and returns the body as it is.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="email">The email.</param>
    /// <returns>The response body.</returns>
    private async Task<JsonDocument> VerifyRawAsync(string code, string email)
    {
        using (var asked = await PostAsync($"/links/{code}/email-code", new { email }))
        {
            asked.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var sent = _mail.Sent.Last(item => item.To.Value == email).Code;
        using var response = await PostAsync($"/links/{code}/verify", new { email, code = sent });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync(response);
    }

    /// <summary>Gets the forward alert the admin received.</summary>
    /// <returns>The bot message.</returns>
    private BotCall ForwardAlert() => ForwardAlerts(AdminTelegramId).Should().ContainSingle().Subject;

    /// <summary>Gets the forward alerts a user received: the messages with Keep / Block buttons.</summary>
    /// <param name="telegramId">The user.</param>
    /// <returns>The alerts, in order.</returns>
    private List<BotCall> ForwardAlerts(long telegramId) =>
        [.. _bot.ToChat(telegramId).Where(call => call.Kind == "Send" && call.Keyboard?.Rows.SelectMany(row => row).Any(button => button.CallbackData.StartsWith("fwd:", StringComparison.Ordinal)) == true)];

    /// <summary>Gets the callback data of a button of the forward alert.</summary>
    /// <param name="index">0 for Keep, 1 for Block.</param>
    /// <returns>The callback data.</returns>
    private string ForwardButton(int index) => ForwardAlert().Keyboard!.Rows.SelectMany(row => row).ElementAt(index).CallbackData;

    /// <summary>Presses a bot button as a Telegram user, through the webhook.</summary>
    /// <param name="fromId">Who presses.</param>
    /// <param name="data">The callback data.</param>
    /// <returns>A task that completes when the API answered 200.</returns>
    private async Task PressAsync(long fromId, string data)
    {
        var update = new
        {
            update_id = Interlocked.Increment(ref _updateId),
            callback_query = new
            {
                id = $"cb{Interlocked.Increment(ref _updateId)}",
                from = new { id = fromId, is_bot = false, first_name = "Reza", username = $"reza_{fromId}" },
                chat_instance = "1",
                data,
                message = new { message_id = 5, date = 1761379200, chat = new { id = fromId, type = "private" }, text = "alert" },
            },
        };
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/telegram/webhook", UriKind.Relative)) { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", WebhookSecret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
