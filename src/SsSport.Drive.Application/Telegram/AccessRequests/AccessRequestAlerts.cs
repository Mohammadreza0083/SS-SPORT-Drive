using System.Globalization;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Public;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.AccessRequests;

/// <summary>The callback data of the access-request alert's Approve and Decline buttons (P-4). Both fit Telegram's 64 bytes.</summary>
public static class AccessRequestCallbacks
{
    /// <summary>Prefix of Approve buttons.</summary>
    private const string ApprovePrefix = "areq:ok:";

    /// <summary>Prefix of Decline buttons.</summary>
    private const string DeclinePrefix = "areq:no:";

    /// <summary>Builds the Approve / Decline buttons of an access-request alert.</summary>
    /// <param name="requestId">The request.</param>
    /// <returns>The keyboard: Approve, then Decline, on one row.</returns>
    public static BotKeyboard Keyboard(Guid requestId)
    {
        var id = requestId.ToString("N", CultureInfo.InvariantCulture);
        return new BotKeyboard([[new BotButton(BotTexts.AccessRequestApproveButton, ApprovePrefix + id), new BotButton(BotTexts.AccessRequestDeclineButton, DeclinePrefix + id)]]);
    }

    /// <summary>Reads an Approve or Decline button.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="approve"><see langword="true"/> for Approve.</param>
    /// <param name="requestId">The request.</param>
    /// <returns><see langword="true"/> for an access-request button with a valid id.</returns>
    public static bool TryParse(string data, out bool approve, out Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(data);
        (approve, var id) = data switch
        {
            _ when data.StartsWith(ApprovePrefix, StringComparison.Ordinal) => (true, data[ApprovePrefix.Length..]),
            _ when data.StartsWith(DeclinePrefix, StringComparison.Ordinal) => (false, data[DeclinePrefix.Length..]),
            _ => (false, string.Empty),
        };
        return Guid.TryParseExact(id, "N", out requestId);
    }

    /// <summary>Builds the bot's answer to a decision.</summary>
    /// <param name="result">What happened.</param>
    /// <returns>The text that replaces the alert.</returns>
    public static string Describe(AccessRequestDecisionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            AccessRequestDecisionOutcome.Decided when result.Status == AccessRequestStatus.Approved =>
                BotTexts.FormatAccessRequestApproved(result.Email, result.PlayerName, result.DecidedBy, result.LinkCode),
            AccessRequestDecisionOutcome.Decided => BotTexts.FormatAccessRequestDeclined(result.Email, result.PlayerName, result.DecidedBy),
            AccessRequestDecisionOutcome.AlreadyDecided => BotTexts.FormatAccessRequestAlreadyDecided(
                result.Email,
                result.PlayerName,
                result.Status == AccessRequestStatus.Approved ? "approved" : "declined",
                result.DecidedBy.Length == 0 ? BotTexts.SomeAdmin : result.DecidedBy),
            _ => BotTexts.AccessRequestGone,
        };
    }
}

/// <summary>
/// Tells every active admin that someone asked for access (P-4), with the player, the full email, what they wrote and
/// Approve / Decline buttons. Sent at once: like forward alerts, it is not held by the notice rules (T-8), because the
/// requester is waiting. The email appears only in this bot message, never in logs.
/// </summary>
/// <param name="requests">The requests.</param>
/// <param name="players">The players.</param>
/// <param name="users">The users.</param>
/// <param name="bot">The messenger.</param>
public sealed class NotifyAdminsOfAccessRequest(
    IRepository<AccessRequest, Guid> requests,
    IRepository<Player, Guid> players,
    IRepository<User, Guid> users,
    ITelegramMessenger bot) : IBackgroundEventHandler<AccessRequested>
{
    /// <summary>Sends the alert to each admin, unless the request was decided in the meantime.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (nobody: the public form).</param>
    /// <param name="cancellationToken">Cancels the sends.</param>
    /// <returns>A task that completes when every admin has been messaged.</returns>
    public async Task HandleAsync(AccessRequested domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var request = await requests.GetByIdAsync(domainEvent.AccessRequestId, cancellationToken);
        if (request is not { Status: AccessRequestStatus.Pending })
        {
            return;
        }

        var player = await players.GetByIdAsync(request.PlayerId, cancellationToken);
        var text = BotTexts.FormatAccessRequestAlert(player?.Name?.Value ?? "?", request.Email.Value, request.About);
        foreach (var admin in await users.ListAsync(new ActiveAdminsSpec(), cancellationToken))
        {
            await bot.SendAsync(admin.TelegramId, text, AccessRequestCallbacks.Keyboard(request.Id), cancellationToken);
        }
    }
}
