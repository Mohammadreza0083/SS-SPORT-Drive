using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Access;

/// <summary>
/// Tells every active admin about a new access request, with Approve as Analyst / Approve as Admin / Decline buttons.
/// Runs after the request is saved, so admins never see a request that was not stored.
/// </summary>
/// <param name="users">The users.</param>
/// <param name="bot">The messenger.</param>
public sealed class NotifyAdminsOfAccessRequest(IRepository<User, Guid> users, ITelegramMessenger bot) : IBackgroundEventHandler<UserRequestedAccess>
{
    /// <summary>Sends the request to each admin.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the sends.</param>
    /// <returns>A task that completes when every admin has been messaged.</returns>
    public async Task HandleAsync(UserRequestedAccess domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var requester = await users.GetByIdAsync(domainEvent.UserId, cancellationToken);
        if (requester is null)
        {
            return;
        }

        var text = BotTexts.FormatAdminRequest(requester.DisplayName, requester.Username is { } username ? $"@{username}" : BotTexts.NoUsername, requester.TelegramId);
        foreach (var admin in await users.ListAsync(new ActiveAdminsSpec(), cancellationToken))
        {
            await bot.SendAsync(admin.TelegramId, text, BotCallbacks.ReviewKeyboard(requester.Id), cancellationToken);
        }
    }
}

/// <summary>Welcomes an approved user (and the first admin) with the three steps and the main menu.</summary>
/// <param name="users">The users.</param>
/// <param name="bot">The messenger.</param>
public sealed class WelcomeApprovedUser(IRepository<User, Guid> users, ITelegramMessenger bot) : IBackgroundEventHandler<UserApproved>
{
    /// <summary>Sends the welcome.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the welcome is sent.</returns>
    public async Task HandleAsync(UserApproved domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (await users.GetByIdAsync(domainEvent.UserId, cancellationToken) is { } user)
        {
            await bot.SendAsync(user.TelegramId, BotTexts.FormatWelcomeApproved(user.DisplayName), BotCallbacks.MenuKeyboard, cancellationToken);
        }
    }
}

/// <summary>Tells a declined user they can ask again in 24 hours.</summary>
/// <param name="users">The users.</param>
/// <param name="bot">The messenger.</param>
public sealed class TellDeclinedUser(IRepository<User, Guid> users, ITelegramMessenger bot) : IBackgroundEventHandler<UserDeclined>
{
    /// <summary>Sends the notice.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the notice is sent.</returns>
    public async Task HandleAsync(UserDeclined domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (await users.GetByIdAsync(domainEvent.UserId, cancellationToken) is { } user)
        {
            await bot.SendAsync(user.TelegramId, BotTexts.DeclinedNotice, null, cancellationToken);
        }
    }
}
