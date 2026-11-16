using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Domain.Emails;

namespace SsSport.Drive.Api.IntegrationTests.Emails;

/// <summary>
/// The email queue (L-4, decisions §8): every email is an <c>outgoing_emails</c> row sent by a background job. Gate
/// codes go Resend first and Brevo when Resend fails; the code is drawn in the job and never stored. Admins page through
/// the queue at <c>GET /admin/emails</c>, newest first, with masked addresses; nobody else may.
/// </summary>
public sealed class EmailQueueEndpointTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>A gate code goes through the queue to Resend; its row is sent, of kind Code, with no code in it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_gate_code_is_queued_and_sent_by_resend()
    {
        var code = await _host.CreateLinkAsync(await _host.AddPlayerAsync());

        using var response = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/email-code", new { email = BackendTestHost.RecipientEmail });

        response.IsSuccessStatusCode.Should().BeTrue();
        var mail = _host.Resend.Sent.Where(IsGateCode).Should().ContainSingle().Subject;
        mail.To.Value.Should().Be(BackendTestHost.RecipientEmail);
        var sentCode = RecordingEmailTransport.CodeIn(mail);
        sentCode.Should().MatchRegex(@"^\d{6}$");
        _host.Brevo.Sent.Where(IsGateCode).Should().BeEmpty();
        _host.GoogleSmtp.Sent.Where(IsGateCode).Should().BeEmpty();

        var row = await _host.QueryAsync(db => db.Set<OutgoingEmail>().AsNoTracking().SingleAsync(email => email.Template == "gate.code", TestContext.Current.CancellationToken));
        row.Kind.Should().Be(EmailKind.Code);
        row.Template.Should().Be("gate.code");
        row.Status.Should().Be(EmailStatus.Sent);
        row.Provider.Should().Be("Resend");
        row.Attempts.Should().Be(1);
        row.Data.Should().Contain("challengeId").And.NotContain(sentCode).And.NotContain("@");
    }

    /// <summary>When Resend is down, Brevo sends the code and the row says so.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Brevo_sends_the_code_when_resend_is_down()
    {
        _host.Resend.Fails = true;
        var code = await _host.CreateLinkAsync(await _host.AddPlayerAsync());

        using var response = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/email-code", new { email = BackendTestHost.RecipientEmail });

        response.IsSuccessStatusCode.Should().BeTrue();
        _host.Brevo.Sent.Where(IsGateCode).Should().ContainSingle();
        var row = await _host.QueryAsync(db => db.Set<OutgoingEmail>().AsNoTracking().SingleAsync(email => email.Template == "gate.code", TestContext.Current.CancellationToken));
        row.Status.Should().Be(EmailStatus.Sent);
        row.Provider.Should().Be("Brevo");
    }

    /// <summary>The code typed from the queued email opens the gate, as before L-4.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_queued_code_opens_the_gate()
    {
        var code = await _host.CreateLinkAsync(await _host.AddPlayerAsync());
        using (await _host.SendAsync(HttpMethod.Post, $"/links/{code}/email-code", new { email = BackendTestHost.RecipientEmail }))
        {
        }

        var sentCode = RecordingEmailTransport.CodeIn(_host.Resend.Sent.Single(IsGateCode));
        using var verify = await _host.SendAsync(HttpMethod.Post, $"/links/{code}/verify", new { email = BackendTestHost.RecipientEmail, code = sentCode });

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Admins page through the queue, newest first, with masked addresses and the outcome of each email.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Admins_page_through_the_queue()
    {
        var older = OutgoingEmail.Queue(EmailKind.General, "forward.kept", Domain.Primitives.EmailAddress.Create("scout@other.com"), null, DateTimeOffset.UtcNow.AddMinutes(-3));
        older.RecordSent("GoogleSmtp", DateTimeOffset.UtcNow.AddMinutes(-3));
        older.ClearDomainEvents();
        var middle = OutgoingEmail.Queue(EmailKind.Code, "gate.code", Domain.Primitives.EmailAddress.Create("marco@lube.it"), null, DateTimeOffset.UtcNow.AddMinutes(-2));
        middle.RecordFailure("HttpRequestException", DateTimeOffset.UtcNow.AddMinutes(-2));
        middle.ClearDomainEvents();
        var newest = OutgoingEmail.Queue(EmailKind.Code, "gate.code", Domain.Primitives.EmailAddress.Create("jan@aurora.nl"), null, DateTimeOffset.UtcNow.AddMinutes(-1));
        newest.Skip(DateTimeOffset.UtcNow.AddMinutes(-1));
        newest.ClearDomainEvents();
        foreach (var email in new[] { older, middle, newest })
        {
            await _host.SaveAsync(email);
        }

        using var first = await _host.SendAsync(HttpMethod.Get, "/admin/emails?pageSize=2", telegramId: BackendTestHost.AdminTelegramId);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstBody = await BackendTestHost.ReadJsonAsync(first);
        var items = firstBody.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(newest.Id, middle.Id);
        items[0].GetProperty("recipient").GetString().Should().Be("j***@aurora.nl");
        items[0].GetProperty("status").GetString().Should().Be("Skipped");
        items[1].GetProperty("kind").GetString().Should().Be("Code");
        items[1].GetProperty("template").GetString().Should().Be("gate.code");
        items[1].GetProperty("status").GetString().Should().Be("Queued");
        items[1].GetProperty("attempts").GetInt32().Should().Be(1);
        items[1].GetProperty("lastError").GetString().Should().Be("HttpRequestException");
        firstBody.RootElement.GetRawText().Should().NotContain("marco@lube.it").And.NotContain("jan@aurora.nl");
        var cursor = firstBody.RootElement.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();

        using var second = await _host.SendAsync(HttpMethod.Get, $"/admin/emails?pageSize=2&cursor={Uri.EscapeDataString(cursor!)}", telegramId: BackendTestHost.AdminTelegramId);
        using var secondBody = await BackendTestHost.ReadJsonAsync(second);
        var last = secondBody.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        last.GetProperty("id").GetGuid().Should().Be(older.Id);
        last.GetProperty("provider").GetString().Should().Be("GoogleSmtp");
        secondBody.RootElement.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>The status filter narrows the list.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_list_filters_by_status()
    {
        var sent = OutgoingEmail.Queue(EmailKind.General, "forward.kept", Domain.Primitives.EmailAddress.Create("scout@other.com"), null, DateTimeOffset.UtcNow);
        sent.RecordSent("GoogleSmtp", DateTimeOffset.UtcNow);
        sent.ClearDomainEvents();
        var skipped = OutgoingEmail.Queue(EmailKind.Code, "gate.code", Domain.Primitives.EmailAddress.Create("marco@lube.it"), null, DateTimeOffset.UtcNow);
        skipped.Skip(DateTimeOffset.UtcNow);
        skipped.ClearDomainEvents();
        await _host.SaveAsync(sent);
        await _host.SaveAsync(skipped);

        using var response = await _host.SendAsync(HttpMethod.Get, "/admin/emails?status=Sent", telegramId: BackendTestHost.AdminTelegramId);

        using var body = await BackendTestHost.ReadJsonAsync(response);
        body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Should().Equal(sent.Id);
    }

    /// <summary>Only admins see the queue: no sign-in is 401, an analyst 403.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_see_the_queue()
    {
        using var anonymous = await _host.SendAsync(HttpMethod.Get, "/admin/emails");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var analyst = await _host.SendAsync(HttpMethod.Get, "/admin/emails", telegramId: BackendTestHost.AnalystTelegramId);
        analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>A page larger than 100 is refused like every other list (decisions §2b).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Pages_are_capped()
    {
        using var response = await _host.SendAsync(HttpMethod.Get, "/admin/emails?pageSize=101", telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Tells the gate code email from others, such as the email of the new link itself (A-6b).</summary>
    /// <param name="mail">The email.</param>
    /// <returns><see langword="true"/> for a gate code email.</returns>
    private static bool IsGateCode(EmailMessage mail) => mail.Subject == "Your SS-SPORT Drive code";
}
