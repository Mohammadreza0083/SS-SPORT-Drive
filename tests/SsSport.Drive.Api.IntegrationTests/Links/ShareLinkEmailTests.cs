using System.Net;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Links;

namespace SsSport.Drive.Api.IntegrationTests.Links;

/// <summary>
/// A-6b (found 2026-11-15): <b>Create &amp; email</b> in the console's New share link modal emails the new link to every
/// address of the recipient, through the email queue, like an approved access request (P-4).
/// </summary>
public sealed class ShareLinkEmailTests : IAsyncLifetime
{
    /// <summary>The API and database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the API.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the API.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Each address of a new recipient gets the link and the note.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Creating_a_link_emails_it_to_the_recipient()
    {
        var player = await _host.AddPlayerAsync();

        using var response = await _host.SendAsync(HttpMethod.Post, "/admin/links", new
        {
            recipient = new { name = "Marco Rossi", organisation = "Lube Volley", emails = new[] { "marco@lube.it", "scouting@lube.it" } },
            playerIds = new[] { player },
            note = "Watch the second set.",
            expiresInDays = 30,
        }, telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        var code = body.RootElement.GetProperty("code").GetString()!;
        var rows = await _host.QueryAsync(db => db.Set<OutgoingEmail>().AsNoTracking()
            .Where(email => email.Template == EmailTemplates.LinkShared)
            .ToListAsync(TestContext.Current.CancellationToken));
        rows.Select(row => row.To.Value).Should().BeEquivalentTo(["marco@lube.it", "scouting@lube.it"]);
        rows.Should().OnlyContain(row => row.Kind == EmailKind.General);
        var mails = Sent().Where(mail => mail.Text.Contains($"/r/{code}", StringComparison.Ordinal)).ToList();
        mails.Select(mail => mail.To.Value).Should().BeEquivalentTo(["marco@lube.it", "scouting@lube.it"]);
        mails.Should().OnlyContain(mail => mail.Text.Contains("Watch the second set.", StringComparison.Ordinal));
    }

    /// <summary>A link for an existing recipient is emailed to that recipient too.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_link_for_an_existing_recipient_is_emailed()
    {
        var first = await _host.AddPlayerAsync();
        var second = await _host.AddPlayerAsync();
        await _host.CreateLinkAsync(first);
        var recipientId = await _host.QueryAsync(db => db.Set<Recipient>().AsNoTracking()
            .Select(recipient => recipient.Id).SingleAsync(TestContext.Current.CancellationToken));

        using var response = await _host.SendAsync(HttpMethod.Post, "/admin/links", new
        {
            recipientId,
            playerIds = new[] { second },
            expiresInDays = 7,
        }, telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        var code = body.RootElement.GetProperty("code").GetString()!;
        Sent().Should().ContainSingle(mail => mail.Text.Contains($"/r/{code}", StringComparison.Ordinal))
            .Which.To.Value.Should().Be(BackendTestHost.RecipientEmail);
    }

    /// <summary>A refused link (a waiting player) emails nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_refused_link_emails_nothing()
    {
        var waiting = await _host.AddWaitingPlayerAsync("Link Email Waiting");

        using var response = await _host.SendAsync(HttpMethod.Post, "/admin/links", new
        {
            recipient = new { name = "Marco Rossi", emails = new[] { "marco@lube.it" } },
            playerIds = new[] { waiting },
            expiresInDays = 30,
        }, telegramId: BackendTestHost.AdminTelegramId);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await _host.QueryAsync(db => db.Set<OutgoingEmail>().CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    /// <summary>Every email any provider sent.</summary>
    /// <returns>The emails.</returns>
    private IEnumerable<EmailMessage> Sent() => _host.Resend.Sent.Concat(_host.Brevo.Sent).Concat(_host.GoogleSmtp.Sent);
}
