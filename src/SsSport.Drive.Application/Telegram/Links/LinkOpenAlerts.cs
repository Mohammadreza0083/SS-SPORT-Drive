using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Links;

/// <summary>
/// Tells every active admin in the bot when an email opens a watched link for the first time (L-7, owner-approved
/// 2026-11-12). Only links with "notify on open" on alert. The notice goes through the notice rules (T-8: throttle, quiet
/// hours, grouping), unlike a forward alert, which the viewer is waiting on. The email appears only in the bot, never in a log.
/// </summary>
/// <param name="opens">The first-open records.</param>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="players">The players.</param>
/// <param name="users">The users.</param>
/// <param name="outbox">Holds and sends notices under the notice rules.</param>
/// <param name="logger">The logger.</param>
public sealed partial class NotifyAdminsOfLinkOpen(
    IRepository<LinkOpen, Guid> opens,
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<Player, Guid> players,
    IRepository<User, Guid> users,
    IBotNoticeOutbox outbox,
    ILogger<NotifyAdminsOfLinkOpen> logger) : IBackgroundEventHandler<LinkOpened>
{
    /// <summary>Queues the alert for each admin when the link is watched.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (nobody: the gate is public).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when every admin has the notice queued or sent.</returns>
    public async Task HandleAsync(LinkOpened domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var open = await opens.GetByIdAsync(domainEvent.LinkOpenId, cancellationToken);
        var link = open is null ? null : await links.GetByIdAsync(open.ShareLinkId, cancellationToken);
        if (open is null || link is not { NotifyOnOpen: true })
        {
            return;
        }

        var recipient = await recipients.GetByIdAsync(link.RecipientId, cancellationToken);
        var names = (await players.ListAsync(new PlayersByIdsSpec(link.PlayerIds), cancellationToken))
            .OrderBy(player => link.PlayerIds.ToList().IndexOf(player.Id))
            .Select(player => player.Name?.Value ?? "?");
        var text = BotTexts.FormatLinkOpenedAlert(link.Code.Value, recipient?.Name ?? "?", open.Email.Value, string.Join(", ", names))
            + (open.IsForward ? BotTexts.LinkOpenedForwardSuffix : string.Empty);
        foreach (var admin in await users.ListAsync(new ActiveAdminsSpec(), cancellationToken))
        {
            await outbox.QueueAsync(admin, NoticeKind.LinkOpened, playerId: null, text, cancellationToken);
        }

        LogAdminsTold(logger, link.Id);
    }

    /// <summary>Logs that admins were told about an open.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="ShareLinkId">The link.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Admins told that link {ShareLinkId} was opened")]
    private static partial void LogAdminsTold(ILogger logger, Guid ShareLinkId);
}
