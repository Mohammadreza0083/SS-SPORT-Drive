using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Notices;

/// <summary>
/// Tells the owning analyst that an admin published their player (decisions §5, T-8). Runs in a background job after
/// the save, through the notification rules. Archive players without an analyst, inactive analysts and an analyst who
/// published the player themself are not told.
/// </summary>
/// <param name="players">The players.</param>
/// <param name="users">The team.</param>
/// <param name="outbox">Holds and sends notices under the notification rules.</param>
public sealed class NotifyAnalystOfPublish(
    IRepository<Player, Guid> players,
    IRepository<User, Guid> users,
    IBotNoticeOutbox outbox) : IBackgroundEventHandler<PlayerPublished>
{
    /// <summary>Hands the "published" notice for the analyst to the outbox.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who published.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the notice is held or sent, or nobody needed to be told.</returns>
    public async Task HandleAsync(PlayerPublished domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(context);
        if (await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(domainEvent.PlayerId), cancellationToken) is not { AnalystId: { } ownerId } player
            || ownerId == context.ActorUserId
            || await users.FirstOrDefaultAsync(new UserByIdReadOnlySpec(ownerId), cancellationToken) is not { IsActive: true } owner)
        {
            return;
        }

        var text = BotTexts.FormatPlayerPublishedNotice(player.Name?.Value ?? player.Id.ToString());
        await outbox.QueueAsync(owner, NoticeKind.PlayerPublished, player.Id, text, cancellationToken);
    }
}
