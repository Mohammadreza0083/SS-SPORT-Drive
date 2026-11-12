using System.Globalization;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Links;

/// <summary>The callback data of the forward alert's Keep and Block buttons (L-3). Both fit Telegram's 64 bytes.</summary>
public static class ForwardCallbacks
{
    /// <summary>Prefix of Keep buttons.</summary>
    private const string KeepPrefix = "fwd:keep:";

    /// <summary>Prefix of Block buttons.</summary>
    private const string BlockPrefix = "fwd:block:";

    /// <summary>Builds the Keep / Block buttons of a forward alert.</summary>
    /// <param name="forwardId">The forward.</param>
    /// <returns>The keyboard: Keep, then Block, on one row.</returns>
    public static BotKeyboard Keyboard(Guid forwardId)
    {
        var id = forwardId.ToString("N", CultureInfo.InvariantCulture);
        return new BotKeyboard([[new BotButton(BotTexts.ForwardKeepButton, KeepPrefix + id), new BotButton(BotTexts.ForwardBlockButton, BlockPrefix + id)]]);
    }

    /// <summary>Reads a Keep or Block button.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="keep"><see langword="true"/> for Keep.</param>
    /// <param name="forwardId">The forward.</param>
    /// <returns><see langword="true"/> for a forward button with a valid id.</returns>
    public static bool TryParse(string data, out bool keep, out Guid forwardId)
    {
        ArgumentNullException.ThrowIfNull(data);
        (keep, var id) = data switch
        {
            _ when data.StartsWith(KeepPrefix, StringComparison.Ordinal) => (true, data[KeepPrefix.Length..]),
            _ when data.StartsWith(BlockPrefix, StringComparison.Ordinal) => (false, data[BlockPrefix.Length..]),
            _ => (false, string.Empty),
        };
        return Guid.TryParseExact(id, "N", out forwardId);
    }

    /// <summary>Builds the bot's answer to a decision.</summary>
    /// <param name="result">What happened.</param>
    /// <returns>The text that replaces the alert.</returns>
    public static string Describe(ForwardDecisionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            ForwardDecisionOutcome.Decided when result.Status == ForwardStatus.Kept => BotTexts.FormatForwardKept(result.Email, result.LinkCode, result.DecidedBy),
            ForwardDecisionOutcome.Decided => BotTexts.FormatForwardBlocked(result.Email, result.LinkCode, result.DecidedBy),
            ForwardDecisionOutcome.AlreadyDecided => BotTexts.FormatForwardAlreadyDecided(
                result.Email,
                result.LinkCode,
                result.Status == ForwardStatus.Kept ? "kept" : "blocked",
                result.DecidedBy.Length == 0 ? BotTexts.SomeAdmin : result.DecidedBy),
            _ => BotTexts.ForwardGone,
        };
    }
}

/// <summary>
/// Tells every active admin that a link was forwarded (L-3), with the link, its recipient and players, the full email
/// and Keep / Block buttons. The email appears only in this bot message, never in logs. Sent at once: like access
/// requests, it is not held by the notice rules (T-8), because the viewer is waiting for it.
/// </summary>
/// <param name="forwards">The forwards.</param>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="players">The players.</param>
/// <param name="users">The users.</param>
/// <param name="bot">The messenger.</param>
public sealed class NotifyAdminsOfForward(
    IRepository<LinkForward, Guid> forwards,
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<Player, Guid> players,
    IRepository<User, Guid> users,
    ITelegramMessenger bot) : IBackgroundEventHandler<LinkForwarded>
{
    /// <summary>Sends the alert to each admin, unless the forward was decided in the meantime.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (nobody: the gate is public).</param>
    /// <param name="cancellationToken">Cancels the sends.</param>
    /// <returns>A task that completes when every admin has been messaged.</returns>
    public async Task HandleAsync(LinkForwarded domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var forward = await forwards.GetByIdAsync(domainEvent.ForwardId, cancellationToken);
        var link = forward is null ? null : await links.GetByIdAsync(forward.ShareLinkId, cancellationToken);
        if (forward is not { Status: ForwardStatus.Pending } || link is null)
        {
            return;
        }

        var recipient = await recipients.GetByIdAsync(link.RecipientId, cancellationToken);
        var names = (await players.ListAsync(new PlayersByIdsSpec(link.PlayerIds), cancellationToken))
            .OrderBy(player => link.PlayerIds.ToList().IndexOf(player.Id))
            .Select(player => player.Name?.Value ?? "?");
        var text = BotTexts.FormatForwardAlert(link.Code.Value, recipient?.Name ?? "?", string.Join(", ", names), forward.Email.Value);
        foreach (var admin in await users.ListAsync(new ActiveAdminsSpec(), cancellationToken))
        {
            await bot.SendAsync(admin.TelegramId, text, ForwardCallbacks.Keyboard(forward.Id), cancellationToken);
        }
    }
}
