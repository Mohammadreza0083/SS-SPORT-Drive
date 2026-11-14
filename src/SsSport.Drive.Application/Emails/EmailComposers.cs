using System.Net;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Abstractions.Web;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Emails;

/// <summary>The text of the emails (English, decisions §1). Every value put into HTML is encoded.</summary>
public static class EmailContent
{
    /// <summary>The sign-off every email ends with.</summary>
    private const string SignOff = "SS-SPORT · Fetch Your Dreams";

    /// <summary>Builds the gate code email; the code stays out of the subject, so it does not show on lock screens.</summary>
    /// <param name="to">The address.</param>
    /// <param name="code">The 6-digit code; never logged.</param>
    /// <returns>The message.</returns>
    public static EmailMessage GateCode(Domain.Primitives.EmailAddress to, string code) => new(
        to,
        "Your SS-SPORT Drive code",
        $"Your SS-SPORT Drive code is {code}.\n\nIt works for 10 minutes. If you did not ask for it, you can ignore this email.\n\n{SignOff}",
        "<div style=\"font-family:Arial,sans-serif;color:#111;max-width:480px\">" +
        "<p>Your SS-SPORT Drive code is</p>" +
        $"<p style=\"font-size:32px;font-weight:700;letter-spacing:8px;margin:16px 0\">{WebUtility.HtmlEncode(code)}</p>" +
        "<p>It works for 10 minutes. If you did not ask for it, you can ignore this email.</p>" +
        $"<p style=\"color:#888\">{SignOff}</p></div>");

    /// <summary>Builds the "your link is open" email of a kept forward.</summary>
    /// <param name="to">The address.</param>
    /// <param name="link">The address of the personal link.</param>
    /// <returns>The message.</returns>
    public static EmailMessage ForwardKept(Domain.Primitives.EmailAddress to, Uri link) => new(
        to,
        "Your SS-SPORT Drive link is open",
        $"Good news: the link you opened is now open for you.\n\nOpen it here:\n{link.AbsoluteUri}\n\n{SignOff}",
        "<div style=\"font-family:Arial,sans-serif;color:#111;max-width:480px\">" +
        "<p>Good news: the link you opened is now open for you.</p>" +
        $"<p><a href=\"{WebUtility.HtmlEncode(link.AbsoluteUri)}\">Open the link</a></p>" +
        $"<p style=\"color:#888\">{SignOff}</p></div>");

    /// <summary>Builds the "your access is approved" email with the personal link (P-4).</summary>
    /// <param name="to">The address.</param>
    /// <param name="playerNames">The players the link shows, as text.</param>
    /// <param name="link">The address of the personal link.</param>
    /// <returns>The message.</returns>
    public static EmailMessage AccessApproved(Domain.Primitives.EmailAddress to, string playerNames, Uri link) => new(
        to,
        "Your SS-SPORT Drive access is approved",
        $"Your request was approved. You can now watch {playerNames}.\n\nOpen it here:\n{link.AbsoluteUri}\n\n{SignOff}",
        "<div style=\"font-family:Arial,sans-serif;color:#111;max-width:480px\">" +
        $"<p>Your request was approved. You can now watch {WebUtility.HtmlEncode(playerNames)}.</p>" +
        $"<p><a href=\"{WebUtility.HtmlEncode(link.AbsoluteUri)}\">Open the link</a></p>" +
        $"<p style=\"color:#888\">{SignOff}</p></div>");

    /// <summary>Builds a sign-in link email.</summary>
    /// <param name="to">The address.</param>
    /// <param name="link">The sign-in address with its one-time token.</param>
    /// <param name="validity">How long the link works, such as "15 minutes".</param>
    /// <returns>The message.</returns>
    public static EmailMessage SignIn(Domain.Primitives.EmailAddress to, Uri link, string validity) => new(
        to,
        "Your SS-SPORT Drive sign-in link",
        $"Use this link to sign in:\n{link.AbsoluteUri}\n\nIt works once, for {validity}. If you did not ask for it, you can ignore this email.\n\n{SignOff}",
        "<div style=\"font-family:Arial,sans-serif;color:#111;max-width:480px\">" +
        $"<p><a href=\"{WebUtility.HtmlEncode(link.AbsoluteUri)}\">Sign in to SS-SPORT Drive</a></p>" +
        $"<p>It works once, for {WebUtility.HtmlEncode(validity)}. If you did not ask for it, you can ignore this email.</p>" +
        $"<p style=\"color:#888\">{SignOff}</p></div>");
}

/// <summary>
/// Composes the gate code email (L-2): draws the code in the job, stores its salted hash, then returns the mail. A request
/// that was already used or is too old has nothing to send. A retried job draws a new code, which replaces the one before.
/// </summary>
/// <param name="challenges">The codes.</param>
/// <param name="unitOfWork">Saves the hash before the mail goes out.</param>
/// <param name="secrets">The gate's secrets.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GateCodeComposer(
    IRepository<EmailChallenge, Guid> challenges,
    IUnitOfWork unitOfWork,
    IViewerSecrets secrets,
    TimeProvider timeProvider) : IEmailComposer
{
    /// <inheritdoc />
    public string Template => EmailTemplates.GateCode;

    /// <inheritdoc />
    public async Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var challengeId = EmailData.GuidOf(email, "challengeId");
        var challenge = challengeId is { } id ? await challenges.GetByIdAsync(id, cancellationToken) : null;
        var code = secrets.NewCode();
        if (challenge is null || !challenge.IssueCode(secrets.HashCode(code), timeProvider.GetUtcNow()))
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmailContent.GateCode(email.To, code);
    }
}

/// <summary>Composes the "your link is open" email sent when an admin keeps a forward (L-3b).</summary>
/// <param name="links">The links.</param>
/// <param name="webLinks">Builds the address of the personal link.</param>
public sealed class ForwardKeptComposer(IRepository<ShareLink, Guid> links, IWebLinks webLinks) : IEmailComposer
{
    /// <inheritdoc />
    public string Template => EmailTemplates.ForwardKept;

    /// <inheritdoc />
    public async Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var linkId = EmailData.GuidOf(email, "shareLinkId");
        var link = linkId is { } id ? await links.GetByIdAsync(id, cancellationToken) : null;
        return link is null ? null : EmailContent.ForwardKept(email.To, webLinks.ViewerLink(link.Code.Value));
    }
}

/// <summary>Composes the "your access is approved" email sent when an admin approves an access request (P-4).</summary>
/// <param name="links">The links.</param>
/// <param name="players">The players.</param>
/// <param name="webLinks">Builds the address of the personal link.</param>
public sealed class AccessApprovedComposer(IRepository<ShareLink, Guid> links, IRepository<Player, Guid> players, IWebLinks webLinks) : IEmailComposer
{
    /// <inheritdoc />
    public string Template => EmailTemplates.AccessApproved;

    /// <inheritdoc />
    public async Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var linkId = EmailData.GuidOf(email, "shareLinkId");
        var link = linkId is { } id ? await links.GetByIdAsync(id, cancellationToken) : null;
        if (link is null)
        {
            return null;
        }

        var byId = (await players.ListAsync(new PlayersByIdsSpec(link.PlayerIds), cancellationToken)).ToDictionary(player => player.Id);
        var names = string.Join(", ", link.PlayerIds.Where(byId.ContainsKey).Select(playerId => byId[playerId].Name?.Value ?? "a player"));
        return EmailContent.AccessApproved(email.To, names, webLinks.ViewerLink(link.Code.Value));
    }
}
