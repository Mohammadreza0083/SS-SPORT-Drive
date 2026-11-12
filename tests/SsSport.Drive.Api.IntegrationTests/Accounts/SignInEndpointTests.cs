using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Api.IntegrationTests.Accounts;

/// <summary>
/// Email magic-link sign-in (A-1 admin console, 7 days; P-5 viewers, 1 day; decisions §7a). Asking for a link always
/// answers 202, so nobody can probe who is an admin; only an active admin's email gets an admin link, any email gets a
/// viewer link. The link works once, for 15 minutes, and becomes a session token the web app keeps in a cookie and
/// forwards in <c>X-Admin-Session</c> / <c>X-Viewer-Account</c> (the API never reads cookies). An admin session opens
/// the admin APIs like the Mini App's signature does.
/// </summary>
public sealed class SignInEndpointTests : IAsyncLifetime
{
    /// <summary>The admin session header.</summary>
    private const string AdminHeader = "X-Admin-Session";

    /// <summary>The viewer account session header.</summary>
    private const string ViewerHeader = "X-Viewer-Account";

    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>An active admin gets a one-time link to the console's sign-in page, through the code providers.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_admin_gets_a_sign_in_link()
    {
        using var response = await _host.SendAsync(HttpMethod.Post, "/auth/admin/link", new { email = "Owner@SS-Sport.co" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var mail = _host.Resend.Sent.Should().ContainSingle().Subject;
        mail.To.Value.Should().Be(BackendTestHost.AdminEmail);
        mail.Text.Should().Contain($"{BackendTestHost.PublicBaseUrl}/admin/sign-in?token=");
        mail.Html.Should().Contain($"{BackendTestHost.PublicBaseUrl}/admin/sign-in?token=");
        RecordingEmailTransport.TokenIn(mail).Length.Should().BeGreaterThanOrEqualTo(32);

        var row = await _host.QueryAsync(db => db.Set<OutgoingEmail>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        row.Template.Should().Be("auth.admin_sign_in");
        row.Kind.Should().Be(EmailKind.Code);
        row.Data.Should().NotContain(RecordingEmailTransport.TokenIn(mail));
    }

    /// <summary>An analyst or an unknown email gets the same 202 and no email: nobody can probe for admins.</summary>
    /// <param name="email">The email typed.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(BackendTestHost.AnalystEmail)]
    [InlineData("stranger@example.com")]
    public async Task Others_get_no_admin_link(string email)
    {
        using var response = await _host.SendAsync(HttpMethod.Post, "/auth/admin/link", new { email });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _host.Resend.Sent.Should().BeEmpty();
        _host.Brevo.Sent.Should().BeEmpty();
        _host.GoogleSmtp.Sent.Should().BeEmpty();
    }

    /// <summary>A malformed email is a 400; a body that is not JSON is refused (the CSRF check).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Bad_requests_are_refused()
    {
        using var bad = await _host.SendAsync(HttpMethod.Post, "/auth/admin/link", new { email = "not-an-email" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var client = _host.Api.CreateClient();
        using var form = await client.PostAsync(
            new Uri("/auth/admin/link", UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("email", BackendTestHost.AdminEmail)]),
            TestContext.Current.CancellationToken);
        form.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        _host.Resend.Sent.Should().BeEmpty();
    }

    /// <summary>The link's token becomes a 7-day admin session, once; the session opens the admin APIs.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_link_becomes_a_seven_day_admin_session()
    {
        var token = await RequestTokenAsync("admin", BackendTestHost.AdminEmail);

        using var response = await _host.SendAsync(HttpMethod.Post, "/auth/admin/session", new { token });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        var session = body.RootElement.GetProperty("sessionToken").GetString()!;
        session.Should().NotBe(token);
        body.RootElement.GetProperty("email").GetString().Should().Be(BackendTestHost.AdminEmail);
        body.RootElement.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));

        using var me = await _host.SendAsync(HttpMethod.Get, "/auth/admin/me", headers: Header(AdminHeader, session));
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        using var meBody = await BackendTestHost.ReadJsonAsync(me);
        meBody.RootElement.GetProperty("email").GetString().Should().Be(BackendTestHost.AdminEmail);
        meBody.RootElement.GetProperty("displayName").GetString().Should().Be("Owner");

        using var links = await _host.SendAsync(HttpMethod.Get, "/admin/links", headers: Header(AdminHeader, session));
        links.StatusCode.Should().Be(HttpStatusCode.OK);

        using var again = await _host.SendAsync(HttpMethod.Post, "/auth/admin/session", new { token });
        again.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BackendTestHost.ProblemCodeAsync(again)).Should().Be("sign_in.invalid");
    }

    /// <summary>Unknown tokens and viewer tokens do not open an admin session.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Wrong_tokens_are_refused()
    {
        var viewerToken = await RequestTokenAsync("viewer", BackendTestHost.AdminEmail);

        using var unknown = await _host.SendAsync(HttpMethod.Post, "/auth/admin/session", new { token = "unknown-token-unknown-token-unknown-token" });
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BackendTestHost.ProblemCodeAsync(unknown)).Should().Be("sign_in.invalid");

        using var crossed = await _host.SendAsync(HttpMethod.Post, "/auth/admin/session", new { token = viewerToken });
        crossed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Without a live admin session, the console APIs and <c>me</c> are 401.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task No_session_is_unauthorized()
    {
        using var me = await _host.SendAsync(HttpMethod.Get, "/auth/admin/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BackendTestHost.ProblemCodeAsync(me)).Should().Be("sign_in.required");

        using var bogus = await _host.SendAsync(HttpMethod.Get, "/admin/links", headers: Header(AdminHeader, "not-a-session-token-at-all-aaaaaaaaaaaaaaaa"));
        bogus.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A demoted or removed admin's session stops opening the console at once.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_demoted_admin_loses_the_console()
    {
        var session = await SignInAsync("admin", BackendTestHost.AdminEmail);
        await _host.QueryAsync(async db =>
        {
            var admin = await db.Set<User>().SingleAsync(user => user.Id == _host.AdminId, TestContext.Current.CancellationToken);
            admin.ChangeRole(Role.Analyst);
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        using var links = await _host.SendAsync(HttpMethod.Get, "/admin/links", headers: Header(AdminHeader, session));
        links.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        using var me = await _host.SendAsync(HttpMethod.Get, "/auth/admin/me", headers: Header(AdminHeader, session));
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Signing out ends the session; signing out without one is still 204.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Sign_out_ends_the_session()
    {
        var session = await SignInAsync("admin", BackendTestHost.AdminEmail);

        using var signOut = await _host.SendAsync(HttpMethod.Post, "/auth/admin/sign-out", new { }, headers: Header(AdminHeader, session));
        signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var me = await _host.SendAsync(HttpMethod.Get, "/auth/admin/me", headers: Header(AdminHeader, session));
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var nobody = await _host.SendAsync(HttpMethod.Post, "/auth/admin/sign-out", new { });
        nobody.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Any email gets a viewer link; it becomes a one-day viewer session and makes a viewer account. Viewer sessions never
    /// open the admin APIs.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_viewer_signs_in_for_one_day()
    {
        using (var request = await _host.SendAsync(HttpMethod.Post, "/auth/viewer/link", new { email = "Jan@Aurora.nl" }))
        {
            request.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var mail = _host.Resend.Sent.Should().ContainSingle().Subject;
        mail.To.Value.Should().Be("jan@aurora.nl");
        mail.Text.Should().Contain($"{BackendTestHost.PublicBaseUrl}/sign-in?token=");
        var row = await _host.QueryAsync(db => db.Set<OutgoingEmail>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        row.Template.Should().Be("auth.viewer_sign_in");

        using var response = await _host.SendAsync(HttpMethod.Post, "/auth/viewer/session", new { token = RecordingEmailTransport.TokenIn(mail) });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        var session = body.RootElement.GetProperty("sessionToken").GetString()!;
        body.RootElement.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(1), TimeSpan.FromMinutes(1));

        using var me = await _host.SendAsync(HttpMethod.Get, "/auth/viewer/me", headers: Header(ViewerHeader, session));
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        using var meBody = await BackendTestHost.ReadJsonAsync(me);
        meBody.RootElement.GetProperty("email").GetString().Should().Be("jan@aurora.nl");

        (await _host.QueryAsync(db => db.Set<ViewerAccount>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);

        using var admin = await _host.SendAsync(HttpMethod.Get, "/admin/links", headers: Header(AdminHeader, session));
        admin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var adminMe = await _host.SendAsync(HttpMethod.Get, "/auth/admin/me", headers: Header(AdminHeader, session));
        adminMe.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A second sign-in by the same viewer reuses their account.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_returning_viewer_keeps_one_account()
    {
        await SignInAsync("viewer", "jan@aurora.nl");
        await SignInAsync("viewer", "jan@aurora.nl");

        (await _host.QueryAsync(db => db.Set<ViewerAccount>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    /// <summary>
    /// At most 5 links per email and audience per hour: the 6th request still answers 202 (no probing) but sends nothing.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Link_requests_are_capped_per_email()
    {
        for (var i = 0; i < 6; i++)
        {
            using var response = await _host.SendAsync(HttpMethod.Post, "/auth/viewer/link", new { email = "jan@aurora.nl" });
            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        _host.Resend.Sent.Should().HaveCount(5);
    }

    /// <summary>The sign-in endpoints are in the OpenAPI document (Scalar), under their own tag.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_endpoints_are_documented()
    {
        using var client = _host.Api.CreateClient();
        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        foreach (var path in new[] { "/auth/admin/link", "/auth/admin/session", "/auth/admin/me", "/auth/admin/sign-out", "/auth/viewer/link", "/auth/viewer/session", "/auth/viewer/me", "/auth/viewer/sign-out", "/admin/emails" })
        {
            document.Should().Contain($"\"{path}\"", path);
        }
    }

    /// <summary>Asks for a link and returns the token from its email.</summary>
    /// <param name="audience"><c>admin</c> or <c>viewer</c>.</param>
    /// <param name="email">The email.</param>
    /// <returns>The token.</returns>
    private async Task<string> RequestTokenAsync(string audience, string email)
    {
        var before = _host.Resend.Sent.Count;
        using var response = await _host.SendAsync(HttpMethod.Post, $"/auth/{audience}/link", new { email });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _host.Resend.Sent.Should().HaveCount(before + 1);
        return RecordingEmailTransport.TokenIn(_host.Resend.Sent[^1]);
    }

    /// <summary>Signs in end to end and returns the session token.</summary>
    /// <param name="audience"><c>admin</c> or <c>viewer</c>.</param>
    /// <param name="email">The email.</param>
    /// <returns>The session token.</returns>
    private async Task<string> SignInAsync(string audience, string email)
    {
        var token = await RequestTokenAsync(audience, email);
        using var response = await _host.SendAsync(HttpMethod.Post, $"/auth/{audience}/session", new { token });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SessionBody>(TestContext.Current.CancellationToken);
        return body!.SessionToken;
    }

    /// <summary>One header.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The headers.</returns>
    private static Dictionary<string, string> Header(string name, string value) => new() { [name] = value };

    /// <summary>The session answer.</summary>
    /// <param name="SessionToken">The session token.</param>
    private sealed record SessionBody(string SessionToken);
}
