using System.Net;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// Sync &amp; jobs and the email queue (A-9, canvas <c>AdminQueues</c>): the files still being ingested or failed, a summary
/// of the queues and the Drive sync, and retry buttons for a failed file and a failed email. The email list itself is
/// L-4's <c>GET /admin/emails</c> (tested in <c>EmailQueueEndpointTests</c>).
/// </summary>
public sealed class AdminJobsTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>The jobs list holds every file that is not ready yet, newest first, with its player and failure reason.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Jobs_list_the_files_not_ready_yet()
    {
        var player = await _host.AddPlayerAsync();
        await _host.AddFileAsync(player, "Stats.pdf", ProcessingState.Ready, 1);
        var queued = await _host.AddFileAsync(player, "Serve.mov", ProcessingState.Queued, 2);
        var failed = await _host.AddFileAsync(player, "Full_Match_02.mov", ProcessingState.Failed, 3);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/jobs?page=1&pageSize=20");

        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var rows = body.RootElement.GetProperty("items").EnumerateArray().ToDictionary(row => row.GetProperty("assetId").GetGuid());
        rows.Keys.Should().BeEquivalentTo([queued, failed]);
        var row = rows[failed];
        row.GetProperty("playerId").GetGuid().Should().Be(player);
        row.GetProperty("playerName").GetString().Should().Be("Arash Tehrani");
        row.GetProperty("fileName").GetString().Should().Be("Full_Match_02.mov");
        row.GetProperty("kind").GetString().Should().Be("Video");
        row.GetProperty("state").GetString().Should().Be("Failed");
        row.GetProperty("failureReason").GetString().Should().Be("ffmpeg exited with code 1");
        row.TryGetProperty("createdAt", out _).Should().BeTrue();
    }

    /// <summary>The state filter narrows the jobs; an unknown or the Ready state is a 400.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Jobs_filter_by_state()
    {
        var player = await _host.AddPlayerAsync();
        await _host.AddFileAsync(player, "Serve.mov", ProcessingState.Queued, 1);
        var failed = await _host.AddFileAsync(player, "Full_Match_02.mov", ProcessingState.Failed, 2);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/jobs?state=failed");
        body.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("assetId").GetGuid()).Should().Equal(failed);

        foreach (var state in new[] { "ready", "done" })
        {
            using var bad = await _host.SendAsync(HttpMethod.Get, $"/admin/jobs?state={state}", telegramId: BackendTestHost.AdminTelegramId);
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest, state);
        }
    }

    /// <summary>The summary counts the files per state, the Drive folders and their last sync, and the email outcomes.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_summary_counts_queues_sync_and_email()
    {
        var player = await _host.AddPlayerAsync();
        await _host.AddFileAsync(player, "A.mov", ProcessingState.Queued, 1);
        await _host.AddFileAsync(player, "B.mov", ProcessingState.Processing, 2);
        await _host.AddFileAsync(player, "C.mov", ProcessingState.Failed, 3);
        var lastSync = DateTimeOffset.UtcNow.AddMinutes(-4);
        await _host.AddFolderAsync(player, "SS-SPORT Arash Tehrani", syncedAt: lastSync.AddMinutes(-10));
        await _host.AddFolderAsync(player, "SS-SPORT Arash Tehrani 2", syncedAt: lastSync, accessLost: true);
        var sent = OutgoingEmail.Queue(EmailKind.General, "forward.kept", EmailAddress.Create("scout@other.com"), null, DateTimeOffset.UtcNow);
        sent.RecordSent("GoogleSmtp", DateTimeOffset.UtcNow);
        sent.ClearDomainEvents();
        await _host.SaveAsync(sent);
        await _host.SaveAsync(FailedEmail(null));

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/jobs/summary");

        var root = body.RootElement;
        root.GetProperty("queued").GetInt32().Should().Be(1);
        root.GetProperty("copying").GetInt32().Should().Be(0);
        root.GetProperty("processing").GetInt32().Should().Be(1);
        root.GetProperty("failed").GetInt32().Should().Be(1);
        root.GetProperty("folders").GetInt32().Should().Be(2);
        root.GetProperty("lostFolders").GetInt32().Should().Be(1);
        root.GetProperty("lastDriveSyncAt").GetDateTimeOffset().Should().BeCloseTo(lastSync, TimeSpan.FromSeconds(1));
        root.GetProperty("emailsSentLast24Hours").GetInt32().Should().Be(1);
        root.GetProperty("emailsFailed").GetInt32().Should().Be(1);
        root.GetProperty("emailsQueued").GetInt32().Should().Be(0);
    }

    /// <summary>A failed email retried by an admin goes out again and is audited as <c>email.retried</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_failed_email_can_be_retried()
    {
        var linkId = await _host.LinkIdAsync(await _host.CreateLinkAsync(await _host.AddPlayerAsync()));
        var failed = FailedEmail($$"""{"shareLinkId":"{{linkId}}"}""");
        await _host.SaveAsync(failed);

        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/emails/{failed.Id}/retry", new { }, HttpStatusCode.Accepted))
        {
        }

        var stored = await _host.QueryAsync(db => Task.FromResult(db.Set<OutgoingEmail>().Single(item => item.Id == failed.Id)));
        stored.Status.Should().Be(EmailStatus.Sent);
        stored.Provider.Should().Be("GoogleSmtp");
        _host.GoogleSmtp.Sent.Should().ContainSingle(mail => mail.To.Value == "scout@other.com");
        var actions = await _host.QueryAsync(db => Task.FromResult(db.Set<Domain.Audit.AuditEntry>().Select(entry => entry.Action).ToList()));
        actions.Should().Contain("email.retried");
    }

    /// <summary>Retrying an email that did not fail is a 422 <c>email.not_failed</c>; an unknown email is a 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_a_failed_email_can_be_retried()
    {
        var sent = OutgoingEmail.Queue(EmailKind.General, "forward.kept", EmailAddress.Create("scout@other.com"), null, DateTimeOffset.UtcNow);
        sent.RecordSent("GoogleSmtp", DateTimeOffset.UtcNow);
        sent.ClearDomainEvents();
        await _host.SaveAsync(sent);

        using var notFailed = await _host.SendAsync(HttpMethod.Post, $"/admin/emails/{sent.Id}/retry", new { }, telegramId: BackendTestHost.AdminTelegramId);
        notFailed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BackendTestHost.ProblemCodeAsync(notFailed)).Should().Be("email.not_failed");

        using var unknown = await _host.SendAsync(HttpMethod.Post, $"/admin/emails/{Guid.NewGuid()}/retry", new { }, telegramId: BackendTestHost.AdminTelegramId);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Only admins see the jobs and retry.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_see_the_jobs()
    {
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/admin/jobs"),
            (HttpMethod.Get, "/admin/jobs/summary"),
            (HttpMethod.Post, $"/admin/emails/{Guid.NewGuid()}/retry"),
        })
        {
            using var analyst = await _host.SendAsync(method, path, new { }, telegramId: BackendTestHost.AnalystTelegramId);
            analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);

            using var anonymous = await _host.SendAsync(method, path, new { });
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }
    }

    /// <summary>An email to <c>scout@other.com</c> that used up every attempt.</summary>
    /// <param name="data">The email's data, or <see langword="null"/>.</param>
    /// <returns>The failed email, with its events cleared.</returns>
    private static OutgoingEmail FailedEmail(string? data)
    {
        var email = OutgoingEmail.Queue(EmailKind.General, "forward.kept", EmailAddress.Create("scout@other.com"), data, DateTimeOffset.UtcNow.AddHours(-1));
        for (var attempt = 0; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            email.RecordFailure("SmtpException", DateTimeOffset.UtcNow.AddHours(-1));
        }

        email.ClearDomainEvents();
        return email;
    }
}
