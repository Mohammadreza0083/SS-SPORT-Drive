using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Api.IntegrationTests.Public;

/// <summary>
/// The admins' access-request inbox (P-4; decisions §7, §7a "Access requests"):
/// <list type="bullet">
/// <item>a new request alerts every active admin in the bot at once, with the player, the email, what the requester
/// wrote, and Approve / Decline buttons;</item>
/// <item>the console reads the inbox from <c>GET /admin/access-requests</c> (paged in SQL, newest first, pending by
/// default) and decides with <c>POST /admin/access-requests/{id}/approve|decline</c>; admins only (Mini App signature
/// or console session);</item>
/// <item>approving creates a normal personal <c>/r/</c> link for the requester's email (30 days unless the admin
/// changes the expiry, the players or the note) and emails it; declining sends nothing;</item>
/// <item>the first decision is final (409 <c>access_request.already_decided</c>), and every decision is audited.</item>
/// </list>
/// </summary>
public sealed class AccessRequestInboxTests : IAsyncLifetime
{
    /// <summary>The header the API reads an admin console session from.</summary>
    private const string AdminHeader = "X-Admin-Session";

    /// <summary>The requester's personal address.</summary>
    private const string RequesterEmail = "j.demir@gmail.com";

    /// <summary>What the requester wrote.</summary>
    private const string About = "Aurora Volley, head scout. Looking for an opposite for 2027.";

    /// <summary>The webhook secret of <see cref="BackendTestHost"/>.</summary>
    private const string WebhookSecret = "backend-test-secret";

    /// <summary>Counts webhook updates so each has its own id.</summary>
    private int _updateId;

    /// <summary>The API and database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the API.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the API.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    // ---------------------------------------------------------------- bot alert

    /// <summary>A new request alerts the active admin with the player, the email, the description and the two buttons; the analyst gets no alert.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_new_request_alerts_every_admin()
    {
        var player = await _host.AddPlayerAsync();

        var requestId = await SubmitAsync(player);

        var alert = await _host.Bot.WaitForAsync(call => call.ChatId == BackendTestHost.AdminTelegramId && call.Text!.Contains(RequesterEmail, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        alert.Text.Should().Contain("Arash Tehrani").And.Contain(About);
        var id = requestId.ToString("N", CultureInfo.InvariantCulture);
        alert.ButtonData.Should().Equal($"areq:ok:{id}", $"areq:no:{id}");
        _host.Bot.ToChat(BackendTestHost.AnalystTelegramId).Should().NotContain(call => call.Text!.Contains(RequesterEmail, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- inbox

    /// <summary>The inbox lists pending requests newest first with the player, the email, the description and the source.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_inbox_lists_pending_requests_newest_first()
    {
        var player = await _host.AddPlayerAsync();
        var older = await SubmitAsync(player, "older@club.example", DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = await SubmitAsync(player, RequesterEmail, DateTimeOffset.UtcNow);
        var decided = await SubmitAsync(player, "decided@club.example", DateTimeOffset.UtcNow.AddMinutes(-5));
        (await DeclineAsync(decided)).Dispose();

        using var response = await _host.SendAsync(HttpMethod.Get, "/admin/access-requests?page=1&pageSize=20", headers: await AdminSessionAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(newer, older);
        var first = items[0];
        first.GetProperty("email").GetString().Should().Be(RequesterEmail);
        first.GetProperty("about").GetString().Should().Be(About);
        first.GetProperty("source").GetString().Should().Be("EmailCode");
        first.GetProperty("status").GetString().Should().Be("Pending");
        first.GetProperty("player").GetProperty("id").GetGuid().Should().Be(player);
        first.GetProperty("player").GetProperty("name").GetString().Should().Be("Arash Tehrani");
        first.GetProperty("player").GetProperty("slug").GetString().Should().Be("arash-tehrani");
    }

    /// <summary><c>?status=</c> picks the decided ones; an unknown status is 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_inbox_filters_by_status()
    {
        var player = await _host.AddPlayerAsync();
        var declined = await SubmitAsync(player, "declined@club.example");
        await SubmitAsync(player);
        (await DeclineAsync(declined)).Dispose();

        using var response = await _host.SendAsync(HttpMethod.Get, "/admin/access-requests?status=Declined", telegramId: BackendTestHost.AdminTelegramId);
        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/access-requests?status=Maybe", telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        var item = body.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("id").GetGuid().Should().Be(declined);
        item.GetProperty("status").GetString().Should().Be("Declined");
        item.GetProperty("decidedBy").GetString().Should().Be("Owner");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The inbox is for admins: nobody signed in is 401, an analyst is 403.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_read_the_inbox()
    {
        using var anonymous = await _host.SendAsync(HttpMethod.Get, "/admin/access-requests");
        using var analyst = await _host.SendAsync(HttpMethod.Get, "/admin/access-requests", telegramId: BackendTestHost.AnalystTelegramId);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- approve

    /// <summary>
    /// Approving creates a personal link for the requester's email to the requested player, 30 days by default, emails
    /// the link (general mail, not the code providers), answers the decided request and audits the decision.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Approving_creates_and_emails_a_personal_link()
    {
        var player = await _host.AddPlayerAsync();
        var requestId = await SubmitAsync(player);

        using var response = await ApproveAsync(requestId, new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("Approved");
        body.RootElement.GetProperty("decidedBy").GetString().Should().Be("Owner");
        var code = body.RootElement.GetProperty("shareLink").GetProperty("code").GetString()!;
        body.RootElement.GetProperty("shareLink").GetProperty("path").GetString().Should().Be($"/r/{code}");

        var link = await _host.QueryAsync(db => db.Set<ShareLink>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        link.Code.Value.Should().Be(code);
        link.PlayerIds.Should().Equal(player);
        link.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
        var recipient = await _host.QueryAsync(db => db.Set<Recipient>().AsNoTracking().SingleAsync(r => r.Id == link.RecipientId, TestContext.Current.CancellationToken));
        recipient.Emails.Select(email => email.Value).Should().Equal(RequesterEmail);

        var mail = _host.GoogleSmtp.Sent.Should().ContainSingle().Subject;
        mail.To.Value.Should().Be(RequesterEmail);
        mail.Text.Should().Contain($"{BackendTestHost.PublicBaseUrl}/r/{code}").And.Contain("Arash Tehrani");
        _host.Resend.Sent.Should().BeEmpty();

        var stored = await _host.QueryAsync(db => db.Set<AccessRequest>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        stored.Status.Should().Be(AccessRequestStatus.Approved);
        stored.ShareLinkId.Should().Be(link.Id);
        stored.DecidedByUserId.Should().Be(_host.AdminId);
        (await AuditCountAsync("access_request.approved")).Should().Be(1);
    }

    /// <summary>"Approve with changes": the admin picks the expiry (7/30/90), more players and a note.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Approving_with_changes_uses_the_admins_choices()
    {
        var player = await _host.AddPlayerAsync();
        var other = await _host.AddPlayerAsync();
        var requestId = await SubmitAsync(player);

        using var response = await ApproveAsync(requestId, new { expiresInDays = 90, playerIds = new[] { player, other }, note = "Both opposites we discussed." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = await _host.QueryAsync(db => db.Set<ShareLink>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        link.PlayerIds.Should().Equal(player, other);
        link.Type.Should().Be(ShareLinkType.Roster);
        link.Note.Should().Be("Both opposites we discussed.");
        link.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), TimeSpan.FromMinutes(5));
    }

    /// <summary>Every link expires: an expiry other than 7, 30 or 90 days is 400 and nothing is decided.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_expiry_is_refused()
    {
        var requestId = await SubmitAsync(await _host.AddPlayerAsync());

        using var response = await ApproveAsync(requestId, new { expiresInDays = 45 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusOfAsync(requestId)).Should().Be(AccessRequestStatus.Pending);
        (await _host.QueryAsync(db => db.Set<ShareLink>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    /// <summary>The first decision is final: a second approve or a decline is 409 and creates no second link or email.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_decided_request_cannot_be_decided_again()
    {
        var requestId = await SubmitAsync(await _host.AddPlayerAsync());
        (await ApproveAsync(requestId, new { })).Dispose();

        using var again = await ApproveAsync(requestId, new { });
        using var decline = await DeclineAsync(requestId);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BackendTestHost.ProblemCodeAsync(again)).Should().Be("access_request.already_decided");
        decline.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _host.QueryAsync(db => db.Set<ShareLink>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
        _host.GoogleSmtp.Sent.Should().ContainSingle();
    }

    /// <summary>An unknown request is 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_request_is_not_found()
    {
        using var approve = await ApproveAsync(Guid.CreateVersion7(), new { });
        using var decline = await DeclineAsync(Guid.CreateVersion7());

        approve.StatusCode.Should().Be(HttpStatusCode.NotFound);
        decline.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>An analyst cannot decide (403) and nothing changes.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_decide()
    {
        var requestId = await SubmitAsync(await _host.AddPlayerAsync());

        using var response = await _host.SendAsync(HttpMethod.Post, $"/admin/access-requests/{requestId}/approve", new { }, telegramId: BackendTestHost.AnalystTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusOfAsync(requestId)).Should().Be(AccessRequestStatus.Pending);
    }

    // ---------------------------------------------------------------- decline

    /// <summary>Declining records the decision and the audit entry, creates no link and sends no email.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Declining_sends_nothing()
    {
        var requestId = await SubmitAsync(await _host.AddPlayerAsync());

        using var response = await DeclineAsync(requestId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("Declined");
        body.RootElement.GetProperty("shareLink").ValueKind.Should().Be(JsonValueKind.Null);
        (await StatusOfAsync(requestId)).Should().Be(AccessRequestStatus.Declined);
        (await _host.QueryAsync(db => db.Set<ShareLink>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
        _host.GoogleSmtp.Sent.Should().BeEmpty();
        (await AuditCountAsync("access_request.declined")).Should().Be(1);
    }

    // ---------------------------------------------------------------- bot buttons

    /// <summary>Pressing Approve in the bot approves with the defaults, emails the link and replies with the link code.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Approve_in_the_bot_creates_the_link()
    {
        var requestId = await SubmitAsync(await _host.AddPlayerAsync());

        await PressAsync(BackendTestHost.AdminTelegramId, $"areq:ok:{requestId:N}");

        (await StatusOfAsync(requestId)).Should().Be(AccessRequestStatus.Approved);
        var link = await _host.QueryAsync(db => db.Set<ShareLink>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        link.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
        _host.GoogleSmtp.Sent.Should().ContainSingle().Which.To.Value.Should().Be(RequesterEmail);
        await _host.Bot.WaitForAsync(call => call.ChatId == BackendTestHost.AdminTelegramId && call.Text!.Contains(link.Code.Value, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }

    /// <summary>Pressing Decline in the bot declines and sends no email.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Decline_in_the_bot_declines()
    {
        var requestId = await SubmitAsync(await _host.AddPlayerAsync());

        await PressAsync(BackendTestHost.AdminTelegramId, $"areq:no:{requestId:N}");

        (await StatusOfAsync(requestId)).Should().Be(AccessRequestStatus.Declined);
        _host.GoogleSmtp.Sent.Should().BeEmpty();
    }

    /// <summary>A non-admin's press changes nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_analysts_press_changes_nothing()
    {
        var requestId = await SubmitAsync(await _host.AddPlayerAsync());

        await PressAsync(BackendTestHost.AnalystTelegramId, $"areq:ok:{requestId:N}");

        (await StatusOfAsync(requestId)).Should().Be(AccessRequestStatus.Pending);
        (await _host.QueryAsync(db => db.Set<ShareLink>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Stores a request as the public endpoint would (the domain event runs the alert inline).</summary>
    /// <param name="player">The player asked for.</param>
    /// <param name="email">The requester's email.</param>
    /// <param name="at">When it was made, or now.</param>
    /// <returns>The request id.</returns>
    private async Task<Guid> SubmitAsync(Guid player, string email = RequesterEmail, DateTimeOffset? at = null)
    {
        var request = AccessRequest.Submit(player, EmailAddress.Create(email), About, AccessRequestSource.EmailCode, at ?? DateTimeOffset.UtcNow);
        await _host.SaveAsync(request);
        return request.Id;
    }

    /// <summary>Approves through the console API as the admin (Mini App signature).</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="body">The choices.</param>
    /// <returns>The response.</returns>
    private Task<HttpResponseMessage> ApproveAsync(Guid requestId, object body) =>
        _host.SendAsync(HttpMethod.Post, $"/admin/access-requests/{requestId}/approve", body, telegramId: BackendTestHost.AdminTelegramId);

    /// <summary>Declines through the console API as the admin (Mini App signature).</summary>
    /// <param name="requestId">The request.</param>
    /// <returns>The response.</returns>
    private Task<HttpResponseMessage> DeclineAsync(Guid requestId) =>
        _host.SendAsync(HttpMethod.Post, $"/admin/access-requests/{requestId}/decline", new { }, telegramId: BackendTestHost.AdminTelegramId);

    /// <summary>Signs the admin in to the console (A-1) and returns the session header.</summary>
    /// <returns>The header.</returns>
    private async Task<Dictionary<string, string>> AdminSessionAsync()
    {
        using var link = await _host.SendAsync(HttpMethod.Post, "/auth/admin/link", new { email = BackendTestHost.AdminEmail });
        link.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = RecordingEmailTransport.TokenIn(_host.Resend.Sent[^1]);
        using var session = await _host.SendAsync(HttpMethod.Post, "/auth/admin/session", new { token });
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(session);
        return new() { [AdminHeader] = body.RootElement.GetProperty("sessionToken").GetString()! };
    }

    /// <summary>Posts a button press on the bot's alert through the real webhook.</summary>
    /// <param name="fromId">The presser's Telegram id.</param>
    /// <param name="data">The button's callback data.</param>
    /// <returns>A task that completes when the API answered 200.</returns>
    private async Task PressAsync(long fromId, string data)
    {
        var update = new
        {
            update_id = Interlocked.Increment(ref _updateId),
            callback_query = new
            {
                id = $"cb{Interlocked.Increment(ref _updateId)}",
                from = new { id = fromId, is_bot = false, first_name = "Owner", username = $"user_{fromId}" },
                chat_instance = "1",
                data,
                message = new { message_id = 7, date = 1761379200, chat = new { id = fromId, type = "private" }, text = "alert" },
            },
        };
        using var client = _host.Api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/telegram/webhook", UriKind.Relative)) { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", WebhookSecret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Reads a request's status.</summary>
    /// <param name="requestId">The request.</param>
    /// <returns>The status.</returns>
    private Task<AccessRequestStatus> StatusOfAsync(Guid requestId) =>
        _host.QueryAsync(db => db.Set<AccessRequest>().AsNoTracking().Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync(TestContext.Current.CancellationToken));

    /// <summary>Counts audit entries with an action.</summary>
    /// <param name="action">The action code.</param>
    /// <returns>The count.</returns>
    private Task<int> AuditCountAsync(string action) =>
        _host.QueryAsync(db => db.Set<AuditEntry>().CountAsync(entry => entry.Action == action, TestContext.Current.CancellationToken));
}
