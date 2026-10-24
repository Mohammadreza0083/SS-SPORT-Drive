using System.Globalization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Access;

/// <summary>Where someone who writes to the bot stands.</summary>
public enum BotUserState
{
    /// <summary>Not a team member and has not asked.</summary>
    Unknown,

    /// <summary>The configured owner, just created as the first admin (welcomed by the <c>UserApproved</c> event).</summary>
    BecameFirstAdmin,

    /// <summary>The configured owner, but <c>Telegram__AdminEmail</c> is missing.</summary>
    FirstAdminNeedsEmail,

    /// <summary>Waiting for an admin.</summary>
    Pending,

    /// <summary>An active team member.</summary>
    Active,

    /// <summary>Declined; may ask again after <see cref="BotUserStatus.CanRequestAgainAt"/>.</summary>
    Declined,

    /// <summary>Removed by an admin.</summary>
    Removed,
}

/// <summary>Someone's standing with the bot.</summary>
/// <param name="State">The state.</param>
/// <param name="DisplayName">Their display name.</param>
/// <param name="CanRequestAgainAt">When a declined user may ask again, if they must wait.</param>
public sealed record BotUserStatus(BotUserState State, string DisplayName, DateTimeOffset? CanRequestAgainAt = null);

/// <summary>Someone opened the bot (/start or any message): refresh their profile and report where they stand.</summary>
/// <param name="Sender">Who wrote.</param>
public sealed record StartBotCommand(TelegramSender Sender) : ICommand<BotUserStatus>;

/// <summary>What happened to an access request.</summary>
public enum AccessRequestOutcome
{
    /// <summary>The request was recorded and admins are being told.</summary>
    Requested,

    /// <summary>A request is already waiting.</summary>
    AlreadyPending,

    /// <summary>They already have access.</summary>
    AlreadyActive,

    /// <summary>Declined less than 24 hours ago.</summary>
    TooSoon,
}

/// <summary>The result of an access request.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="DisplayName">The requester's display name.</param>
/// <param name="CanRequestAgainAt">For <see cref="AccessRequestOutcome.TooSoon"/>: when they may ask again.</param>
public sealed record AccessRequestResult(AccessRequestOutcome Outcome, string DisplayName, DateTimeOffset? CanRequestAgainAt = null);

/// <summary>Someone pressed Request access. Admins are told through the <c>UserRequestedAccess</c> event.</summary>
/// <param name="Sender">Who asked.</param>
public sealed record RequestBotAccessCommand(TelegramSender Sender) : ICommand<AccessRequestResult>;

/// <summary>What happened to a review.</summary>
public enum ReviewOutcome
{
    /// <summary>Approved (the requester is welcomed through <c>UserApproved</c>).</summary>
    Approved,

    /// <summary>Declined (the requester is told through <c>UserDeclined</c>).</summary>
    Declined,

    /// <summary>Someone already handled it.</summary>
    AlreadyHandled,

    /// <summary>The presser is not an active admin.</summary>
    NotAllowed,

    /// <summary>Approve as admin, but the requester has no email.</summary>
    AdminNeedsEmail,

    /// <summary>The user no longer exists.</summary>
    NotFound,
}

/// <summary>The result of a review.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="RequesterName">The requester's display name.</param>
/// <param name="ReviewerName">The reviewer's display name, or who handled it before.</param>
/// <param name="Detail">The granted role, or the earlier decision (approved / declined / removed).</param>
public sealed record ReviewResult(ReviewOutcome Outcome, string RequesterName = "", string ReviewerName = "", string Detail = "");

/// <summary>An admin pressed Approve as Analyst / Approve as Admin / Decline on a request.</summary>
/// <param name="ReviewerTelegramId">Who pressed.</param>
/// <param name="UserId">The requesting user.</param>
/// <param name="Decision">The decision.</param>
public sealed record ReviewAccessRequestCommand(long ReviewerTelegramId, Guid UserId, AccessDecision Decision) : ICommand<ReviewResult>;

/// <summary>Handles <see cref="StartBotCommand"/>; creates the first admin from configuration on their first message.</summary>
/// <param name="users">The users.</param>
/// <param name="admin">The configured first admin.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class StartBotCommandHandler(IRepository<User, Guid> users, IBotAdminSettings admin, TimeProvider timeProvider)
    : ICommandHandler<StartBotCommand, BotUserStatus>
{
    /// <summary>Refreshes the profile and reports the state.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The state.</returns>
    public async Task<BotUserStatus> Handle(StartBotCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sender = request.Sender;
        var now = timeProvider.GetUtcNow();
        var user = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(sender.UserId), cancellationToken);
        if (user is null)
        {
            if (sender.UserId != admin.AdminTelegramId)
            {
                return new BotUserStatus(BotUserState.Unknown, sender.DisplayName);
            }

            if (!TryEmail(admin.AdminEmail, out var email))
            {
                return new BotUserStatus(BotUserState.FirstAdminNeedsEmail, sender.DisplayName);
            }

            var owner = User.CreateFirstAdmin(sender.UserId, sender.DisplayName, email, now);
            owner.UpdateProfile(sender.Username, sender.DisplayName);
            owner.RecordActivity(now);
            users.Add(owner);
            return new BotUserStatus(BotUserState.BecameFirstAdmin, owner.DisplayName);
        }

        user.UpdateProfile(sender.Username, sender.DisplayName);
        user.RecordActivity(now);
        return user.Status switch
        {
            UserStatus.Pending => new BotUserStatus(BotUserState.Pending, user.DisplayName),
            UserStatus.Active => new BotUserStatus(BotUserState.Active, user.DisplayName),
            UserStatus.Declined => new BotUserStatus(BotUserState.Declined, user.DisplayName, user.CanRequestAgainAt),
            _ => new BotUserStatus(BotUserState.Removed, user.DisplayName),
        };
    }

    /// <summary>Parses the configured email.</summary>
    /// <param name="raw">The configured value.</param>
    /// <param name="email">The email.</param>
    /// <returns><see langword="true"/> when it is a valid address.</returns>
    private static bool TryEmail(string? raw, out EmailAddress email)
    {
        try
        {
            email = EmailAddress.Create(raw);
            return true;
        }
        catch (DomainException)
        {
            email = null!;
            return false;
        }
    }
}

/// <summary>Handles <see cref="RequestBotAccessCommand"/>.</summary>
/// <param name="users">The users.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class RequestBotAccessCommandHandler(IRepository<User, Guid> users, TimeProvider timeProvider)
    : ICommandHandler<RequestBotAccessCommand, AccessRequestResult>
{
    /// <summary>Creates the pending user, or puts a declined/removed one back on the list.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened.</returns>
    public async Task<AccessRequestResult> Handle(RequestBotAccessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sender = request.Sender;
        var now = timeProvider.GetUtcNow();
        var user = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(sender.UserId), cancellationToken);
        if (user is null)
        {
            users.Add(User.RequestAccess(sender.UserId, sender.Username, sender.DisplayName, now));
            return new AccessRequestResult(AccessRequestOutcome.Requested, sender.DisplayName);
        }

        user.UpdateProfile(sender.Username, sender.DisplayName);
        switch (user.Status)
        {
            case UserStatus.Pending:
                return new AccessRequestResult(AccessRequestOutcome.AlreadyPending, user.DisplayName);
            case UserStatus.Active:
                return new AccessRequestResult(AccessRequestOutcome.AlreadyActive, user.DisplayName);
            default:
                if (user.CanRequestAgainAt is { } allowedAt && now < allowedAt)
                {
                    return new AccessRequestResult(AccessRequestOutcome.TooSoon, user.DisplayName, allowedAt);
                }

                user.RequestAgain(now);
                return new AccessRequestResult(AccessRequestOutcome.Requested, user.DisplayName);
        }
    }
}

/// <summary>Handles <see cref="ReviewAccessRequestCommand"/> and records the decision in the audit log.</summary>
/// <param name="users">The users.</param>
/// <param name="audit">The audit log.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ReviewAccessRequestCommandHandler(IRepository<User, Guid> users, IRepository<AuditEntry, Guid> audit, TimeProvider timeProvider)
    : ICommandHandler<ReviewAccessRequestCommand, ReviewResult>
{
    /// <summary>The audit action of an approval.</summary>
    public const string ApprovedAction = "user.approved";

    /// <summary>The audit action of a decline.</summary>
    public const string DeclinedAction = "user.declined";

    /// <summary>Checks the reviewer, applies the decision and audits it.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened.</returns>
    public async Task<ReviewResult> Handle(ReviewAccessRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reviewer = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(request.ReviewerTelegramId), cancellationToken);
        if (reviewer is not { IsAdmin: true })
        {
            return new ReviewResult(ReviewOutcome.NotAllowed);
        }

        var requester = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (requester is null)
        {
            return new ReviewResult(ReviewOutcome.NotFound, ReviewerName: reviewer.DisplayName);
        }

        if (requester.Status != UserStatus.Pending)
        {
            return new ReviewResult(ReviewOutcome.AlreadyHandled, requester.DisplayName, await HandledByAsync(requester, cancellationToken), requester.Status switch
            {
                UserStatus.Active => "approved",
                UserStatus.Declined => "declined",
                _ => "removed",
            });
        }

        var now = timeProvider.GetUtcNow();
        if (BotCallbacks.RoleOf(request.Decision) is { } role)
        {
            if (role == Role.Admin && requester.Email is null)
            {
                return new ReviewResult(ReviewOutcome.AdminNeedsEmail, requester.DisplayName, reviewer.DisplayName);
            }

            requester.Approve(role);
            audit.Add(AuditEntry.Record(reviewer.Id, ApprovedAction, "User", Target(requester), $$"""{"role":"{{role.Name}}"}""", now));
            return new ReviewResult(ReviewOutcome.Approved, requester.DisplayName, reviewer.DisplayName, role.Name);
        }

        requester.Decline(now);
        audit.Add(AuditEntry.Record(reviewer.Id, DeclinedAction, "User", Target(requester), null, now));
        return new ReviewResult(ReviewOutcome.Declined, requester.DisplayName, reviewer.DisplayName);
    }

    /// <summary>Formats a user id as an audit target id.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The target id.</returns>
    private static string Target(User user) => user.Id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Finds who made the last decision about a user, from the audit log.</summary>
    /// <param name="requester">The user.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>Their display name, or a generic "an admin".</returns>
    private async Task<string> HandledByAsync(User requester, CancellationToken cancellationToken)
    {
        var entry = await audit.FirstOrDefaultAsync(new LatestAuditEntrySpec("User", Target(requester), ApprovedAction, DeclinedAction), cancellationToken);
        var actor = entry?.ActorUserId is { } actorId ? await users.GetByIdAsync(actorId, cancellationToken) : null;
        return actor?.DisplayName ?? BotTexts.SomeAdmin;
    }
}
