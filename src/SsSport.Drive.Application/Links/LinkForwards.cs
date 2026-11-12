using System.Globalization;
using Ardalis.Specification;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Links;

/// <summary>The forward record of one email on one link (one row per pair).</summary>
public sealed class LinkForwardSpec : Specification<LinkForward>, ISingleResultSpecification<LinkForward>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The forwarded email.</param>
    public LinkForwardSpec(Guid shareLinkId, EmailAddress email) =>
        Query.Where(forward => forward.ShareLinkId == shareLinkId && forward.Email == email);
}

/// <summary>The first-open record of one email on one link (L-7), if it has opened it before.</summary>
public sealed class LinkOpenSpec : Specification<LinkOpen>, ISingleResultSpecification<LinkOpen>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The viewer's email.</param>
    public LinkOpenSpec(Guid shareLinkId, EmailAddress email) =>
        Query.Where(open => open.ShareLinkId == shareLinkId && open.Email == email);
}

/// <summary>The devices an email is trusted on for one link: a handful per person, removed when a forward is blocked.</summary>
public sealed class TrustedDevicesOfEmailSpec : Specification<TrustedDevice>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The email.</param>
    public TrustedDevicesOfEmailSpec(Guid shareLinkId, EmailAddress email) =>
        Query.Where(device => device.ShareLinkId == shareLinkId && device.Email == email);
}

/// <summary>The sessions an email has on one link (the expired ones are purged daily), removed when a forward is blocked.</summary>
public sealed class ViewerSessionsOfEmailSpec : Specification<ViewerSession>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The email.</param>
    public ViewerSessionsOfEmailSpec(Guid shareLinkId, EmailAddress email) =>
        Query.Where(session => session.ShareLinkId == shareLinkId && session.Email == email);
}

/// <summary>What happened to an admin's Keep or Block.</summary>
public enum ForwardDecisionOutcome
{
    /// <summary>The presser is not an active admin; nothing changed.</summary>
    NotAllowed,

    /// <summary>The forward no longer exists.</summary>
    NotFound,

    /// <summary>The decision was made now.</summary>
    Decided,

    /// <summary>Another decision was made before; nothing changed.</summary>
    AlreadyDecided,
}

/// <summary>The result of <see cref="DecideForwardCommand"/>, for the bot's answer.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Status">The forward's status after the press.</param>
/// <param name="Email">The forwarded email, or empty when not found or not allowed.</param>
/// <param name="LinkCode">The link's code, or empty.</param>
/// <param name="DecidedBy">The admin who made the decision that stands, or empty.</param>
public sealed record ForwardDecisionResult(ForwardDecisionOutcome Outcome, ForwardStatus Status = ForwardStatus.Pending, string Email = "", string LinkCode = "", string DecidedBy = "");

/// <summary>An admin keeps or blocks a forwarded link from the bot's alert (L-3).</summary>
/// <param name="AdminTelegramId">Who pressed.</param>
/// <param name="ForwardId">The forward.</param>
/// <param name="Keep"><see langword="true"/> for Keep, <see langword="false"/> for Block.</param>
public sealed record DecideForwardCommand(long AdminTelegramId, Guid ForwardId, bool Keep) : ICommand<ForwardDecisionResult>;

/// <summary>
/// Handles <see cref="DecideForwardCommand"/>: only active admins decide, the first decision is final, and each one is
/// audited. A block also forgets the email's devices and sessions on the link, so it is shut out at once.
/// </summary>
/// <param name="users">The users.</param>
/// <param name="forwards">The forwards.</param>
/// <param name="links">The links.</param>
/// <param name="devices">The trusted devices.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="audit">The audit log.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class DecideForwardCommandHandler(
    IRepository<User, Guid> users,
    IRepository<LinkForward, Guid> forwards,
    IRepository<ShareLink, Guid> links,
    IRepository<TrustedDevice, Guid> devices,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<AuditEntry, Guid> audit,
    TimeProvider timeProvider) : ICommandHandler<DecideForwardCommand, ForwardDecisionResult>
{
    /// <summary>The audit action of a keep.</summary>
    public const string KeptAction = "share_link.forward_kept";

    /// <summary>The audit action of a block.</summary>
    public const string BlockedAction = "share_link.forward_blocked";

    /// <summary>Checks the admin, applies the decision, audits it and, for a block, removes the email's access.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened.</returns>
    public async Task<ForwardDecisionResult> Handle(DecideForwardCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var admin = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(request.AdminTelegramId), cancellationToken);
        if (admin is not { IsAdmin: true })
        {
            return new ForwardDecisionResult(ForwardDecisionOutcome.NotAllowed);
        }

        var forward = await forwards.GetByIdAsync(request.ForwardId, cancellationToken);
        var link = forward is null ? null : await links.GetByIdAsync(forward.ShareLinkId, cancellationToken);
        if (forward is null || link is null)
        {
            return new ForwardDecisionResult(ForwardDecisionOutcome.NotFound);
        }

        if (!forward.Decide(request.Keep, admin.Id, timeProvider.GetUtcNow()))
        {
            var decider = forward.DecidedByUserId is { } id ? await users.GetByIdAsync(id, cancellationToken) : null;
            return new ForwardDecisionResult(ForwardDecisionOutcome.AlreadyDecided, forward.Status, forward.Email.Value, link.Code.Value, decider?.DisplayName ?? string.Empty);
        }

        if (!request.Keep)
        {
            await ShutOutAsync(forward, cancellationToken);
        }

        var data = string.Create(CultureInfo.InvariantCulture, $$"""{"shareLinkId":"{{link.Id:D}}"}""");
        audit.Add(AuditEntry.Record(admin.Id, request.Keep ? KeptAction : BlockedAction, "LinkForward", forward.Id.ToString("D", CultureInfo.InvariantCulture), data, forward.DecidedAt!.Value));
        return new ForwardDecisionResult(ForwardDecisionOutcome.Decided, forward.Status, forward.Email.Value, link.Code.Value, admin.DisplayName);
    }

    /// <summary>Removes the blocked email's trusted devices and sessions on the link, in the same save as the decision.</summary>
    /// <param name="forward">The blocked forward.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>A task that completes when the rows are staged for deletion.</returns>
    private async Task ShutOutAsync(LinkForward forward, CancellationToken cancellationToken)
    {
        foreach (var device in await devices.ListAsync(new TrustedDevicesOfEmailSpec(forward.ShareLinkId, forward.Email), cancellationToken))
        {
            devices.Remove(device);
        }

        foreach (var session in await sessions.ListAsync(new ViewerSessionsOfEmailSpec(forward.ShareLinkId, forward.Email), cancellationToken))
        {
            sessions.Remove(session);
        }
    }
}
