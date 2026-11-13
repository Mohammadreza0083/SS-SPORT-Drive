using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Security;
using SsSport.Drive.Api.IntegrationTests.Telegram;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Accounts;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.IntegrationTests.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Public;

/// <summary>
/// Asking for access to a locked player (P-3, P-7, P-8; decisions §6 "Anti-spam" and §7a):
/// <list type="bullet">
/// <item>a signed-out visitor proves their email with a 6-digit code (the gate's rules: 10 minutes, 5 tries, 60 s
/// before another, 5 per hour) and passes Cloudflare Turnstile; any ordinary email works (owner, 2026-11-12);</item>
/// <item>a signed-in viewer (<c>X-Viewer-Account</c>, a P-5 account session) asks in one click, with no code and no Turnstile;</item>
/// <item>limits apply per email (5 requests a day) and per player (20 an hour); asking again for the same player while
/// a request is pending stores nothing new;</item>
/// </list>
/// </summary>
public sealed partial class PublicAccessEndpointTests : IAsyncLifetime
{
    /// <summary>The header the API reads a signed-in viewer's session token from.</summary>
    private const string AccountHeader = "X-Viewer-Account";

    /// <summary>A visitor's personal address.</summary>
    private const string VisitorEmail = "j.demir@gmail.com";

    /// <summary>What the visitor writes about themselves.</summary>
    private const string About = "Aurora Volley, head scout. Looking for an opposite for 2027.";

    /// <summary>A Turnstile token the fake verifier accepts.</summary>
    private const string GoodTurnstile = "turnstile-ok";

    /// <summary>The address the requests come from.</summary>
    private const string ClientIp = "203.0.113.9";

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The clock the API reads; tests move it forward.</summary>
    private readonly ShiftableClock _clock = new();

    /// <summary>Catches the codes the API sends.</summary>
    private readonly RecordingEmailTransport _mail = new();

    /// <summary>Accepts <see cref="GoodTurnstile"/> only, and records what it was asked.</summary>
    private readonly FakeTurnstile _turnstile = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Counts created players so each gets its own Volleybox id.</summary>
    private int _players;

    /// <summary>Starts PostgreSQL, boots the API and stores the player <c>arash-tehrani</c>.</summary>
    /// <returns>A task that completes when everything is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:BotToken"] = "123456:public-access-test-token",
                ["Telegram:WebhookSecret"] = "public-access-secret",
                ["Telegram:Mode"] = "Webhook",
                ["Viewer:TokenKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["RateLimiting:PublicPermitsPerMinute"] = "1000",
                ["RateLimiting:AccessPermitsPerMinute"] = "1000",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(new RecordingMessenger()));
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(_mail);
                services.Replace(ServiceDescriptor.Singleton<ITurnstileVerifier>(_turnstile));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
                services.AddTransient<IStartupFilter, TestClientAddress>();
                services.UseInlineBotNotices();
            });
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);
        await AddPlayerAsync("Arash Tehrani");
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A visitor asks for a code: one 6-digit code is mailed to their (personal) address; only its hash is stored.</summary>
    [Fact]
    public async Task A_visitor_gets_a_code_by_email()
    {
        using var response = await PostAsync("/public/access-code", new { email = "J.Demir@Gmail.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("email").GetString().Should().Be("j***@gmail.com");
        body.RootElement.GetProperty("resendAfterSeconds").GetInt32().Should().Be(60);
        body.RootElement.GetProperty("expiresInSeconds").GetInt32().Should().Be(600);

        var sent = _mail.Sent.Should().ContainSingle().Subject;
        sent.To.Value.Should().Be(VisitorEmail);
        sent.Code.Should().MatchRegex("^[0-9]{6}$");
        var stored = await QueryAsync(db => db.Set<AccessCodeChallenge>().Select(challenge => challenge.CodeHash).SingleAsync(TestContext.Current.CancellationToken));
        stored.Should().NotBeNullOrEmpty().And.NotContain(sent.Code);
    }

    /// <summary>No email is 400; a malformed one is 422 <c>email.invalid</c>. Nothing is mailed.</summary>
    [Fact]
    public async Task A_bad_email_gets_no_code()
    {
        using var missing = await PostAsync("/public/access-code", new { email = "" });
        using var malformed = await PostAsync("/public/access-code", new { email = "not-an-email" });

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        malformed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(malformed);
        body.RootElement.GetProperty("code").GetString().Should().Be("email.invalid");
        _mail.Sent.Should().BeEmpty();
    }

    /// <summary>Another code waits 60 seconds; then at most 5 an hour. Refusals are 429 with <c>Retry-After</c>.</summary>
    [Fact]
    public async Task Codes_are_limited()
    {
        (await RequestCodeAsync()).Should().NotBeNull();

        using var tooSoon = await PostAsync("/public/access-code", new { email = VisitorEmail });
        tooSoon.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        tooSoon.Headers.RetryAfter!.Delta.Should().BePositive();
        using (var body = await ReadJsonAsync(tooSoon))
        {
            body.RootElement.GetProperty("code").GetString().Should().Be("email_code.too_many");
        }

        for (var i = 0; i < 4; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(61));
            (await RequestCodeAsync()).Should().NotBeNull();
        }

        _clock.Advance(TimeSpan.FromSeconds(61));
        using var sixth = await PostAsync("/public/access-code", new { email = VisitorEmail });
        sixth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// The right code and a good Turnstile token store one pending request for the player and email; Turnstile was asked
    /// with the token and the visitor's address.
    /// </summary>
    [Fact]
    public async Task The_right_code_sends_the_request()
    {
        var code = await RequestCodeAsync();

        using var response = await SubmitAsync(code);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("pending");

        var request = await QueryAsync(db => db.Set<AccessRequest>().SingleAsync(TestContext.Current.CancellationToken));
        request.Email.Value.Should().Be(VisitorEmail);
        request.About.Should().Be(About);
        request.Source.Should().Be(AccessRequestSource.EmailCode);
        request.Status.Should().Be(AccessRequestStatus.Pending);
        request.PlayerId.Should().Be(await PlayerIdAsync("arash-tehrani"));
        _turnstile.Calls.Should().ContainSingle().Which.Should().Be((GoodTurnstile, ClientIp));
    }

    /// <summary>A wrong code is 422 <c>email_code.invalid</c> with the tries left, and stores no request.</summary>
    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        var code = await RequestCodeAsync();

        using var response = await SubmitAsync(code == "000000" ? "111111" : "000000");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("email_code.invalid");
        body.RootElement.GetProperty("attemptsLeft").GetInt32().Should().Be(4);
        (await CountRequestsAsync()).Should().Be(0);
    }

    /// <summary>A code opens one request: sending it again is <c>email_code.used</c>.</summary>
    [Fact]
    public async Task A_code_is_used_once()
    {
        await AddPlayerAsync("Ivan Valeev");
        var code = await RequestCodeAsync();
        using var first = await SubmitAsync(code);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var again = await SubmitAsync(code, slug: "ivan-valeev");

        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(again);
        body.RootElement.GetProperty("code").GetString().Should().Be("email_code.used");
    }

    /// <summary>No code asked for that email is <c>email_code.expired</c>.</summary>
    [Fact]
    public async Task A_code_never_asked_for_is_expired()
    {
        using var response = await SubmitAsync("123456");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("email_code.expired");
    }

    /// <summary>
    /// A missing or refused Turnstile token is 422 <c>turnstile.failed</c> and is checked before the code, so it uses no
    /// try: the same code still works afterwards.
    /// </summary>
    [Fact]
    public async Task Turnstile_is_checked_before_the_code()
    {
        var code = await RequestCodeAsync();

        using var refused = await SubmitAsync(code, turnstile: "bot");
        using var missing = await SubmitAsync(code, turnstile: null);

        foreach (var response in new[] { refused, missing })
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            using var body = await ReadJsonAsync(response);
            body.RootElement.GetProperty("code").GetString().Should().Be("turnstile.failed");
        }

        var attempts = await QueryAsync(db => db.Set<AccessCodeChallenge>().Select(challenge => challenge.FailedAttempts).SingleAsync(TestContext.Current.CancellationToken));
        attempts.Should().Be(0);
        using var ok = await SubmitAsync(code);
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>The form is checked on the server: a description of 0 or 1001 characters, a missing slug or a code that is not 6 digits is 400.</summary>
    [Fact]
    public async Task The_form_is_validated()
    {
        var bodies = new object[]
        {
            new { playerSlug = "arash-tehrani", about = "", email = VisitorEmail, code = "123456", turnstileToken = GoodTurnstile },
            new { playerSlug = "arash-tehrani", about = new string('a', 1001), email = VisitorEmail, code = "123456", turnstileToken = GoodTurnstile },
            new { playerSlug = "", about = About, email = VisitorEmail, code = "123456", turnstileToken = GoodTurnstile },
            new { playerSlug = "arash-tehrani", about = About, email = VisitorEmail, code = "12a456", turnstileToken = GoodTurnstile },
            new { playerSlug = "arash-tehrani", about = About, email = "", code = "123456", turnstileToken = GoodTurnstile },
        };

        foreach (var body in bodies)
        {
            using var response = await PostAsync("/public/access-requests", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        (await CountRequestsAsync()).Should().Be(0);
    }

    /// <summary>An unknown player, or one still on the waiting list, is 404 and spends nothing.</summary>
    [Fact]
    public async Task Only_published_players_can_be_asked_for()
    {
        await AddPlayerAsync("Aref Moradi", publish: false);
        var code = await RequestCodeAsync();

        using var waiting = await SubmitAsync(code, slug: "aref-moradi");
        using var unknown = await SubmitAsync(code, slug: "nobody");

        waiting.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var ok = await SubmitAsync(code);
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Asking again for the same player while a request is pending answers the same, and stores nothing new.</summary>
    [Fact]
    public async Task A_pending_request_is_not_repeated()
    {
        using var first = await SubmitAsync(await RequestCodeAsync());
        _clock.Advance(TimeSpan.FromSeconds(61));

        using var again = await SubmitAsync(await RequestCodeAsync());

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountRequestsAsync()).Should().Be(1);
    }

    /// <summary>A signed-in viewer asks in one click: no email, code or Turnstile; the email is the session's.</summary>
    [Fact]
    public async Task A_signed_in_viewer_asks_in_one_click()
    {
        var token = await SignInAsync("scout@aurora-volley.example");

        using var response = await PostAsync("/public/access-requests", new { playerSlug = "arash-tehrani", about = About }, token);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await QueryAsync(db => db.Set<AccessRequest>().SingleAsync(TestContext.Current.CancellationToken));
        request.Email.Value.Should().Be("scout@aurora-volley.example");
        request.Source.Should().Be(AccessRequestSource.SignedIn);
        _turnstile.Calls.Should().BeEmpty();
        _mail.Sent.Should().BeEmpty();
    }

    /// <summary>A signed-in request ignores any email in the body: the session's email is the one recorded.</summary>
    [Fact]
    public async Task A_signed_in_request_cannot_choose_another_email()
    {
        var token = await SignInAsync("scout@aurora-volley.example");

        using var response = await PostAsync("/public/access-requests", new { playerSlug = "arash-tehrani", about = About, email = "someone@else.example", code = "123456" }, token);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await QueryAsync(db => db.Set<AccessRequest>().SingleAsync(TestContext.Current.CancellationToken));
        request.Email.Value.Should().Be("scout@aurora-volley.example");
    }

    /// <summary>An ended, signed-out or unknown account session is 401 <c>sign_in.required</c>: the page falls back to the code flow.</summary>
    [Fact]
    public async Task A_dead_account_session_is_refused()
    {
        var ended = await SignInAsync("scout@aurora-volley.example");
        _clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromSeconds(1)));
        var signedOut = await SignInAsync("other@aurora-volley.example");
        await EndSessionAsync(signedOut);

        foreach (var token in new[] { ended, signedOut, "not-a-session-token-at-all-aaaaaaaaaaaaaaaa" })
        {
            using var response = await PostAsync("/public/access-requests", new { playerSlug = "arash-tehrani", about = About }, token);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            using var body = await ReadJsonAsync(response);
            body.RootElement.GetProperty("code").GetString().Should().Be("sign_in.required");
        }

        (await CountRequestsAsync()).Should().Be(0);
    }

    /// <summary>One email may send at most 5 requests in 24 hours: the sixth is 429 <c>access_request.too_many</c>.</summary>
    [Fact]
    public async Task Requests_are_limited_per_email()
    {
        var token = await SignInAsync("scout@aurora-volley.example");
        for (var i = 0; i < 6; i++)
        {
            await AddPlayerAsync($"Player Number {(char)('A' + i)}");
        }

        for (var i = 0; i < 5; i++)
        {
            using var allowed = await PostAsync("/public/access-requests", new { playerSlug = $"player-number-{(char)('a' + i)}", about = About }, token);
            allowed.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var refused = await PostAsync("/public/access-requests", new { playerSlug = "player-number-f", about = About }, token);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter!.Delta.Should().BePositive();
        using var body = await ReadJsonAsync(refused);
        body.RootElement.GetProperty("code").GetString().Should().Be("access_request.too_many");

        _clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)));
        var fresh = await SignInAsync("scout@aurora-volley.example");
        using var nextDay = await PostAsync("/public/access-requests", new { playerSlug = "player-number-f", about = About }, fresh);
        nextDay.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>One player may get at most 20 requests an hour from all emails together: the 21st is 429.</summary>
    [Fact]
    public async Task Requests_are_limited_per_player()
    {
        for (var i = 0; i < 20; i++)
        {
            var token = await SignInAsync($"scout{i}@club.example");
            using var allowed = await PostAsync("/public/access-requests", new { playerSlug = "arash-tehrani", about = About }, token);
            allowed.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var last = await SignInAsync("scout20@club.example");
        using var refused = await PostAsync("/public/access-requests", new { playerSlug = "arash-tehrani", about = About }, last);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var body = await ReadJsonAsync(refused);
        body.RootElement.GetProperty("code").GetString().Should().Be("access_request.too_many");
    }

    /// <summary>Asks for a code for <see cref="VisitorEmail"/> and returns the mailed code.</summary>
    /// <returns>The code.</returns>
    private async Task<string> RequestCodeAsync()
    {
        var before = _mail.Sent.Count;
        using var response = await PostAsync("/public/access-code", new { email = VisitorEmail });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _mail.Sent.Should().HaveCount(before + 1);
        return _mail.Sent[^1].Code;
    }

    /// <summary>Sends a signed-out access request for <see cref="VisitorEmail"/>.</summary>
    /// <param name="code">The typed code.</param>
    /// <param name="slug">The player.</param>
    /// <param name="turnstile">The Turnstile token, or <see langword="null"/> for none.</param>
    /// <returns>The response.</returns>
    private Task<HttpResponseMessage> SubmitAsync(string code, string slug = "arash-tehrani", string? turnstile = GoodTurnstile) =>
        PostAsync("/public/access-requests", new { playerSlug = slug, about = About, email = VisitorEmail, code, turnstileToken = turnstile });

    /// <summary>Starts a signed-in viewer session directly, as the magic link (P-5) will.</summary>
    /// <param name="email">The viewer's email.</param>
    /// <returns>The session token.</returns>
    private async Task<string> SignInAsync(string email)
    {
        using var scope = _api.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<IViewerSecrets>();
        var token = secrets.NewToken();
        scope.ServiceProvider.GetRequiredService<IRepository<AccountSession, Guid>>()
            .Add(AccountSession.Start(SignInAudience.Viewer, Guid.CreateVersion7(), EmailAddress.Create(email), secrets.HashToken(token), _clock.GetUtcNow()));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return token;
    }

    /// <summary>Ends a signed-in session, as signing out does.</summary>
    /// <param name="token">The session token.</param>
    /// <returns>A task that completes when the session is ended.</returns>
    private async Task EndSessionAsync(string token)
    {
        using var scope = _api.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<IViewerSecrets>();
        var session = await scope.ServiceProvider.GetRequiredService<IRepository<AccountSession, Guid>>()
            .FirstOrDefaultAsync(new AccountSessionByTokenSpec(secrets.HashToken(token)), TestContext.Current.CancellationToken);
        session!.End(_clock.GetUtcNow());
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stores a complete player, published unless told otherwise; the slug comes from the name.</summary>
    /// <param name="name">The full name.</param>
    /// <param name="publish">Whether to publish.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task AddPlayerAsync(string name, bool publish = true)
    {
        var n = Interlocked.Increment(ref _players);
        var player = Player.Create(PlayerCategory.Men, _clock.GetUtcNow());
        var fullName = FullName.Create(name);
        player.Rename(fullName, Slug.FromName(fullName));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Opposite]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/player-p{83000 + n}"));
        if (publish)
        {
            player.Publish(_clock.GetUtcNow());
        }

        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().Add(player);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Finds a player's id by slug.</summary>
    /// <param name="slug">The slug.</param>
    /// <returns>The id.</returns>
    private Task<Guid> PlayerIdAsync(string slug) =>
        QueryAsync(db => db.Set<Player>().Where(player => player.Slug == Slug.Create(slug)).Select(player => player.Id).SingleAsync(TestContext.Current.CancellationToken));

    /// <summary>Counts stored access requests.</summary>
    /// <returns>The count.</returns>
    private Task<int> CountRequestsAsync() => QueryAsync(db => db.Set<AccessRequest>().CountAsync(TestContext.Current.CancellationToken));

    /// <summary>Runs work against the database.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="query">The work.</param>
    /// <returns>The result.</returns>
    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Posts JSON from <see cref="ClientIp"/>, with the account header when given.</summary>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <param name="accountToken">The signed-in viewer's token, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? accountToken = null)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(TestClientAddress.Header, ClientIp);
        if (accountToken is not null)
        {
            request.Headers.Add(AccountHeader, accountToken);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Parses a response body.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON document.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>Stands in for Resend (L-4): records each code email and reads the 6-digit code out of its text.</summary>
    private sealed partial class RecordingEmailTransport : IEmailTransport
    {
        /// <summary>The mails, in order.</summary>
        private readonly ConcurrentQueue<(EmailAddress To, string Code)> _sent = new();

        /// <summary>Gets the mails, in order.</summary>
        public IReadOnlyList<(EmailAddress To, string Code)> Sent => [.. _sent];

        /// <inheritdoc />
        public string Name => "Resend";

        /// <inheritdoc />
        public bool IsConfigured => true;

        /// <inheritdoc />
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            _sent.Enqueue((message.To, CodePattern().Match(message.Text).Value));
            return Task.CompletedTask;
        }

        /// <summary>Six digits on their own.</summary>
        /// <returns>The pattern.</returns>
        [System.Text.RegularExpressions.GeneratedRegex(@"\b\d{6}\b")]
        private static partial System.Text.RegularExpressions.Regex CodePattern();
    }

    /// <summary>A Turnstile that accepts <see cref="GoodTurnstile"/> only and records each call.</summary>
    private sealed class FakeTurnstile : ITurnstileVerifier
    {
        /// <summary>The calls, in order.</summary>
        private readonly ConcurrentQueue<(string? Token, string? ClientIp)> _calls = new();

        /// <summary>Gets the calls, in order.</summary>
        public IReadOnlyList<(string? Token, string? ClientIp)> Calls => [.. _calls];

        /// <inheritdoc />
        public Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken)
        {
            _calls.Enqueue((token, clientIp));
            return Task.FromResult(token == GoodTurnstile);
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
