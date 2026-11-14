using System.Net;
using System.Text.Json;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// Users &amp; roles and the audit log (A-8, canvas <c>AdminUsers</c>, decisions §7): the team with masked sign-in
/// details, pending bot requests to approve or decline, role and email changes, removal with the analyst's players
/// handed to an admin, and the rule that the last active admin is never demoted or removed. Every change is audited;
/// the audit log is a cursor page, newest first.
/// </summary>
public sealed class AdminUsersTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// The team list shows name, how they sign in (admins by email link, analysts by the bot), role, status, players and
    /// last activity. Emails are masked; no full address leaves the API on this page.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_team_is_listed_with_masked_sign_in()
    {
        await _host.AddWaitingPlayerAsync("Ali Hoshmand", analystId: _host.AnalystId);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/users?status=active&page=1&pageSize=20");

        var rows = body.RootElement.GetProperty("items").EnumerateArray().ToDictionary(row => row.GetProperty("id").GetGuid());
        rows.Should().ContainKeys(_host.AdminId, _host.AnalystId);
        var admin = rows[_host.AdminId];
        admin.GetProperty("displayName").GetString().Should().Be("Owner");
        admin.GetProperty("role").GetString().Should().Be("Admin");
        admin.GetProperty("status").GetString().Should().Be("Active");
        admin.GetProperty("signIn").GetString().Should().Be("Email");
        admin.GetProperty("email").GetString().Should().Be("o***@ss-sport.co");
        var analyst = rows[_host.AnalystId];
        analyst.GetProperty("role").GetString().Should().Be("Analyst");
        analyst.GetProperty("signIn").GetString().Should().Be("Telegram");
        analyst.GetProperty("username").GetString().Should().Be("analyst");
        analyst.GetProperty("playerCount").GetInt32().Should().Be(1);
        body.RootElement.GetRawText().Should().NotContain(BackendTestHost.AdminEmail).And.NotContain(BackendTestHost.AnalystEmail);
    }

    /// <summary>Pending requests come from the status filter; an unknown status is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Pending_requests_are_filtered()
    {
        var pending = await _host.AddPendingMemberAsync("Parsa M");

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/users?status=pending");
        body.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Equal(pending);

        using var bad = await _host.SendAsync(HttpMethod.Get, "/admin/users?status=banned", telegramId: BackendTestHost.AdminTelegramId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Approving as Analyst activates the user and is audited as <c>user.approved</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Approving_a_request_activates_the_user()
    {
        var pending = await _host.AddPendingMemberAsync("Parsa M");

        using var body = await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/users/{pending}/approve", new { role = "Analyst" });

        body.RootElement.GetProperty("status").GetString().Should().Be("Active");
        body.RootElement.GetProperty("role").GetString().Should().Be("Analyst");
        (await AuditActionsAsync()).Should().Contain("user.approved");
    }

    /// <summary>Approving as Admin needs an email (decisions §7): without one it is a 422 <c>user.admin_needs_email</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Approving_as_admin_needs_an_email()
    {
        var pending = await _host.AddPendingMemberAsync("Parsa M");

        using var response = await _host.SendAsync(HttpMethod.Post, $"/admin/users/{pending}/approve", new { role = "Admin" }, telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BackendTestHost.ProblemCodeAsync(response)).Should().Be("user.admin_needs_email");
    }

    /// <summary>Declining is audited as <c>user.declined</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Declining_a_request()
    {
        var pending = await _host.AddPendingMemberAsync("Parsa M");

        using var body = await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/users/{pending}/decline", new { });

        body.RootElement.GetProperty("status").GetString().Should().Be("Declined");
        (await AuditActionsAsync()).Should().Contain("user.declined");
    }

    /// <summary>An admin adds an email and promotes an analyst; the change is audited as <c>user.role_changed</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_analyst_is_promoted_with_an_email()
    {
        var member = await _host.AddMemberAsync("Ali R", Role.Analyst);

        using var body = await _host.AdminJsonAsync(HttpMethod.Patch, $"/admin/users/{member}", new { email = "ali.r@ss-sport.co", role = "Admin" });

        body.RootElement.GetProperty("role").GetString().Should().Be("Admin");
        body.RootElement.GetProperty("signIn").GetString().Should().Be("Email");
        body.RootElement.GetProperty("email").GetString().Should().Be("a***@ss-sport.co");
        (await AuditActionsAsync()).Should().Contain("user.role_changed");
    }

    /// <summary>The last active admin can be neither demoted nor removed: 422 <c>user.last_admin</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_last_admin_stays()
    {
        using var demote = await _host.SendAsync(HttpMethod.Patch, $"/admin/users/{_host.AdminId}", new { role = "Analyst" }, telegramId: BackendTestHost.AdminTelegramId);
        demote.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BackendTestHost.ProblemCodeAsync(demote)).Should().Be("user.last_admin");

        using var remove = await _host.SendAsync(HttpMethod.Post, $"/admin/users/{_host.AdminId}/remove", new { }, telegramId: BackendTestHost.AdminTelegramId);
        remove.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BackendTestHost.ProblemCodeAsync(remove)).Should().Be("user.last_admin");
    }

    /// <summary>With a second admin, the first can be demoted.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_admin_can_be_demoted_when_another_remains()
    {
        var second = await _host.AddMemberAsync("Ali R", Role.Admin, "ali.r@ss-sport.co");

        using var body = await _host.AdminJsonAsync(HttpMethod.Patch, $"/admin/users/{second}", new { role = "Analyst" });

        body.RootElement.GetProperty("role").GetString().Should().Be("Analyst");
    }

    /// <summary>
    /// Removing an analyst with players needs an admin to hand them to (400 without one); the players move to that admin,
    /// and the removal is audited as <c>user.removed</c>.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Removing_an_analyst_hands_over_their_players()
    {
        var player = await _host.AddWaitingPlayerAsync("Ali Hoshmand", analystId: _host.AnalystId);

        using var missing = await _host.SendAsync(HttpMethod.Post, $"/admin/users/{_host.AnalystId}/remove", new { }, telegramId: BackendTestHost.AdminTelegramId);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var body = await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/users/{_host.AnalystId}/remove", new { reassignTo = _host.AdminId }))
        {
            body.RootElement.GetProperty("status").GetString().Should().Be("Removed");
        }

        var stored = await _host.QueryAsync(db => Task.FromResult(db.Set<Player>().Single(item => item.Id == player)));
        stored.AnalystId.Should().Be(_host.AdminId);
        (await AuditActionsAsync()).Should().Contain("user.removed");
    }

    /// <summary>The audit log pages every entry newest first, with the actor's name and no email in the data.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_audit_log_pages_newest_first()
    {
        var first = await _host.AddPendingMemberAsync("Parsa M");
        var second = await _host.AddPendingMemberAsync("Nima B");
        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/users/{first}/approve", new { role = "Analyst" }))
        {
        }

        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/users/{second}/decline", new { }))
        {
        }

        using var page = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/audit?pageSize=1");

        var entry = page.RootElement.GetProperty("items").EnumerateArray().Single();
        entry.GetProperty("action").GetString().Should().Be("user.declined");
        entry.GetProperty("actorId").GetGuid().Should().Be(_host.AdminId);
        entry.GetProperty("actorName").GetString().Should().Be("Owner");
        entry.GetProperty("targetType").GetString().Should().Be("User");
        entry.GetProperty("targetId").GetString().Should().Be(second.ToString());
        entry.TryGetProperty("at", out _).Should().BeTrue();
        var cursor = page.RootElement.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();

        using var next = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/audit?pageSize=1&cursor={Uri.EscapeDataString(cursor!)}");
        next.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("action").GetString().Should().Be("user.approved");
    }

    /// <summary>The audit log filters by action.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_audit_log_filters_by_action()
    {
        var pending = await _host.AddPendingMemberAsync("Parsa M");
        await _host.CreateLinkAsync(await _host.AddPlayerAsync());
        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/users/{pending}/decline", new { }))
        {
        }

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/audit?action=user.declined");

        body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("action").GetString()).Should().Equal("user.declined");
        body.RootElement.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Only admins manage the team and read the audit log.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_manage_users()
    {
        var pending = await _host.AddPendingMemberAsync("Parsa M");

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/admin/users"),
            (HttpMethod.Get, "/admin/audit"),
            (HttpMethod.Post, $"/admin/users/{pending}/approve"),
            (HttpMethod.Post, $"/admin/users/{pending}/decline"),
            (HttpMethod.Patch, $"/admin/users/{pending}"),
            (HttpMethod.Post, $"/admin/users/{pending}/remove"),
        })
        {
            using var analyst = await _host.SendAsync(method, path, new { role = "Analyst" }, telegramId: BackendTestHost.AnalystTelegramId);
            analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    /// <summary>Reads every audit action stored so far.</summary>
    /// <returns>The actions.</returns>
    private Task<List<string>> AuditActionsAsync() =>
        _host.QueryAsync(db => Task.FromResult(db.Set<Domain.Audit.AuditEntry>().Select(entry => entry.Action).ToList()));
}
