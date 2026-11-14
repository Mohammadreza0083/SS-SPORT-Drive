using System.Globalization;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Public;

/// <summary>The admin inbox of access requests (P-4): the shared rules and the stable codes.</summary>
public static class AccessRequestInboxRules
{
    /// <summary>The audit target type of a request.</summary>
    public const string AuditTarget = nameof(AccessRequest);

    /// <summary>The audit action of an approval.</summary>
    public const string ApprovedAction = "access_request.approved";

    /// <summary>The audit action of a decline.</summary>
    public const string DeclinedAction = "access_request.declined";

    /// <summary>The code of a decision that was made before.</summary>
    public const string AlreadyDecidedCode = "access_request.already_decided";

    /// <summary>The days an approval lasts when the admin does not choose (proposed, P-4).</summary>
    public const int DefaultExpiryDays = 30;

    /// <summary>Reads a status by name, case-insensitively; numbers are not names.</summary>
    /// <param name="value">The value sent; left out means <see cref="AccessRequestStatus.Pending"/>.</param>
    /// <param name="status">The status.</param>
    /// <returns><see langword="true"/> when the value names a status, or is left out.</returns>
    public static bool TryParseStatus(string? value, out AccessRequestStatus status)
    {
        status = AccessRequestStatus.Pending;
        return string.IsNullOrEmpty(value)
            || (!value.Any(char.IsDigit) && Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status));
    }
}

/// <summary>The link an approval created.</summary>
public sealed record AccessRequestLinkDto
{
    /// <summary>Gets the link id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the six-character code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the link's path on the viewer site, <c>/r/{code}</c>.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets when the link stops opening.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>An access request as admins see it (the full email is admin-only).</summary>
public sealed record AccessRequestDto
{
    /// <summary>Gets the request id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the player asked for.</summary>
    public ShareLinkPlayerDto Player { get; init; } = new();

    /// <summary>Gets the requester's verified email.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets what the requester wrote about themselves.</summary>
    public string About { get; init; } = string.Empty;

    /// <summary>Gets how the email was proven: <c>EmailCode</c> or <c>SignedIn</c>.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets <c>Pending</c>, <c>Approved</c> or <c>Declined</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets when it was made (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when it was decided (UTC), or <see langword="null"/> while pending.</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>Gets the display name of the admin who decided, or <see langword="null"/> while pending.</summary>
    public string? DecidedBy { get; init; }

    /// <summary>Gets the personal link an approval created, or <see langword="null"/>.</summary>
    public AccessRequestLinkDto? ShareLink { get; init; }
}

/// <summary>The body of <c>POST /admin/access-requests/{id}/approve</c>: every field is optional ("approve with changes").</summary>
public sealed record ApproveAccessRequestDto
{
    /// <summary>Gets 7, 30 or 90 days; 30 when left out.</summary>
    public int? ExpiresInDays { get; init; }

    /// <summary>Gets the players to show, in page order; the requested player when left out.</summary>
    public IReadOnlyList<Guid>? PlayerIds { get; init; }

    /// <summary>Gets the personal note (at most 500 characters), or <see langword="null"/>.</summary>
    public string? Note { get; init; }

    /// <summary>Turns the form into its command.</summary>
    /// <param name="id">The request.</param>
    /// <returns>The command.</returns>
    public ApproveAccessRequestCommand ToCommand(Guid id) => new(id, ExpiresInDays, PlayerIds, Note);
}

/// <summary>What happened to a decision.</summary>
public enum AccessRequestDecisionOutcome
{
    /// <summary>The presser is not an active admin; nothing changed.</summary>
    NotAllowed,

    /// <summary>The request no longer exists.</summary>
    NotFound,

    /// <summary>The decision was made now.</summary>
    Decided,

    /// <summary>Another decision was made before; nothing changed.</summary>
    AlreadyDecided,
}

/// <summary>The result of a decision made from the bot, for its answer.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Status">The request's status after the press.</param>
/// <param name="Email">The requester's email, or empty when not found or not allowed.</param>
/// <param name="PlayerName">The player asked for, or empty.</param>
/// <param name="LinkCode">The code of the link an approval made, or empty.</param>
/// <param name="DecidedBy">The admin whose decision stands, or empty.</param>
public sealed record AccessRequestDecisionResult(
    AccessRequestDecisionOutcome Outcome,
    AccessRequestStatus Status = AccessRequestStatus.Pending,
    string Email = "",
    string PlayerName = "",
    string LinkCode = "",
    string DecidedBy = "");

/// <summary>Maps access requests to DTOs.</summary>
public static class AccessRequestMapper
{
    /// <summary>Maps a request with the names around it.</summary>
    /// <param name="request">The request.</param>
    /// <param name="player">The player asked for.</param>
    /// <param name="decidedBy">The deciding admin's display name, or <see langword="null"/>.</param>
    /// <param name="link">The link an approval created, or <see langword="null"/>.</param>
    /// <returns>The DTO.</returns>
    public static AccessRequestDto ToDto(this AccessRequest request, Player player, string? decidedBy, ShareLink? link)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(player);
        return new AccessRequestDto
        {
            Id = request.Id,
            Player = new ShareLinkPlayerDto { Id = player.Id, Name = player.Name?.Value, Slug = player.Slug?.Value },
            Email = request.Email.Value,
            About = request.About,
            Source = request.Source.ToString(),
            Status = request.Status.ToString(),
            CreatedAt = request.CreatedAt,
            DecidedAt = request.DecidedAt,
            DecidedBy = decidedBy,
            ShareLink = link is null ? null : link.ToLinkDto(),
        };
    }

    /// <summary>Maps the link an approval created.</summary>
    /// <param name="link">The link.</param>
    /// <returns>The DTO.</returns>
    public static AccessRequestLinkDto ToLinkDto(this ShareLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return new AccessRequestLinkDto { Id = link.Id, Code = link.Code.Value, Path = $"/r/{link.Code.Value}", ExpiresAt = link.ExpiresAt };
    }
}

/// <summary>
/// Approves and declines access requests (P-4). The console API and the bot's buttons both come here, so the rules live
/// once: the first decision is final; an approval creates a normal personal link for the requester's email, emails it
/// and is audited; a decline only records the decision (the requester is told nothing).
/// </summary>
/// <param name="requests">The requests.</param>
/// <param name="players">The players.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="issuer">Creates the personal link.</param>
/// <param name="audit">The audit log.</param>
/// <param name="emails">The email queue.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class AccessRequestDecider(
    IRepository<AccessRequest, Guid> requests,
    IRepository<Player, Guid> players,
    IRepository<Recipient, Guid> recipients,
    ShareLinkIssuer issuer,
    IRepository<AuditEntry, Guid> audit,
    IRepository<OutgoingEmail, Guid> emails,
    TimeProvider timeProvider)
{
    /// <summary>What a decision did: the outcome, and the request, player and link when there is one.</summary>
    /// <param name="Outcome">Decided, already decided or not found.</param>
    /// <param name="Request">The request, or <see langword="null"/> when not found.</param>
    /// <param name="Player">The player asked for, or <see langword="null"/> when not found.</param>
    /// <param name="Link">The link an approval created, or <see langword="null"/>.</param>
    public sealed record Decision(AccessRequestDecisionOutcome Outcome, AccessRequest? Request, Player? Player, ShareLink? Link);

    /// <summary>Approves a request and makes the requester's personal link.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="adminId">The deciding admin.</param>
    /// <param name="expiresInDays">7, 30 or 90, or <see langword="null"/> for the default.</param>
    /// <param name="playerIds">The players to show, or <see langword="null"/> for the requested player.</param>
    /// <param name="note">The personal note, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="NotFoundException">A chosen player does not exist.</exception>
    /// <exception cref="Domain.Primitives.DomainException">A broken link rule (for example a player who is not published).</exception>
    public async Task<Decision> ApproveAsync(Guid requestId, Guid adminId, int? expiresInDays, IReadOnlyList<Guid>? playerIds, string? note, CancellationToken cancellationToken)
    {
        var request = await requests.GetByIdAsync(requestId, cancellationToken);
        if (request is null)
        {
            return new Decision(AccessRequestDecisionOutcome.NotFound, null, null, null);
        }

        var asked = await players.GetByIdAsync(request.PlayerId, cancellationToken) ?? throw new NotFoundException("Player");
        if (request.Status != AccessRequestStatus.Pending)
        {
            return new Decision(AccessRequestDecisionOutcome.AlreadyDecided, request, asked, null);
        }

        var ids = (playerIds is { Count: > 0 } ? playerIds : [request.PlayerId]).Distinct().ToList();
        var found = await players.ListAsync(new PlayersByIdsSpec(ids), cancellationToken);
        if (found.Count != ids.Count)
        {
            throw new NotFoundException("Player");
        }

        var byId = found.ToDictionary(player => player.Id);
        var now = timeProvider.GetUtcNow();
        var recipient = Recipient.Create(NameOf(request.Email), null, null, [request.Email], now);
        recipients.Add(recipient);
        var link = await issuer.IssueAsync(
            recipient,
            [.. ids.Select(id => byId[id])],
            note,
            ShareLinkRules.ExpiryFrom(expiresInDays ?? AccessRequestInboxRules.DefaultExpiryDays, now),
            ShareLinkSettings.Default,
            adminId,
            now,
            cancellationToken);

        request.Approve(adminId, link.Id, now);
        var data = string.Create(CultureInfo.InvariantCulture, $$"""{"shareLinkId":"{{link.Id:D}}"}""");
        emails.Add(OutgoingEmail.Queue(EmailKind.General, EmailTemplates.AccessApproved, request.Email, data, now));
        audit.Add(AuditEntry.Record(adminId, AccessRequestInboxRules.ApprovedAction, AccessRequestInboxRules.AuditTarget, request.Id.ToString("D", CultureInfo.InvariantCulture), data, now));
        return new Decision(AccessRequestDecisionOutcome.Decided, request, asked, link);
    }

    /// <summary>Declines a request. The requester is not told.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="adminId">The deciding admin.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>What happened.</returns>
    public async Task<Decision> DeclineAsync(Guid requestId, Guid adminId, CancellationToken cancellationToken)
    {
        var request = await requests.GetByIdAsync(requestId, cancellationToken);
        if (request is null)
        {
            return new Decision(AccessRequestDecisionOutcome.NotFound, null, null, null);
        }

        var asked = await players.GetByIdAsync(request.PlayerId, cancellationToken) ?? throw new NotFoundException("Player");
        var now = timeProvider.GetUtcNow();
        if (!request.Decline(adminId, now))
        {
            return new Decision(AccessRequestDecisionOutcome.AlreadyDecided, request, asked, null);
        }

        audit.Add(AuditEntry.Record(adminId, AccessRequestInboxRules.DeclinedAction, AccessRequestInboxRules.AuditTarget, request.Id.ToString("D", CultureInfo.InvariantCulture), null, now));
        return new Decision(AccessRequestDecisionOutcome.Decided, request, asked, null);
    }

    /// <summary>Gets the recipient's name for a requester: the part of the email before the <c>@</c>, cut to fit.</summary>
    /// <param name="email">The requester's email.</param>
    /// <returns>The name.</returns>
    private static string NameOf(Domain.Primitives.EmailAddress email)
    {
        var local = email.Value[..email.Value.IndexOf('@', StringComparison.Ordinal)];
        return local.Length > Recipient.MaxNameLength ? local[..Recipient.MaxNameLength] : local;
    }
}

/// <summary>Shared by the two API commands: finishes a decision into the answer, or the right error.</summary>
public static class AccessRequestAnswers
{
    /// <summary>Turns a decision into the DTO, or throws the matching 404 or 409.</summary>
    /// <param name="decision">What the decider did.</param>
    /// <param name="decidedBy">Reads a user's display name.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The decided request.</returns>
    /// <exception cref="NotFoundException">The request does not exist.</exception>
    /// <exception cref="ConflictException"><c>access_request.already_decided</c>.</exception>
    public static async Task<AccessRequestDto> ToAnswerAsync(this AccessRequestDecider.Decision decision, IRepository<User, Guid> decidedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        switch (decision.Outcome)
        {
            case AccessRequestDecisionOutcome.NotFound:
                throw new NotFoundException("Access request");
            case AccessRequestDecisionOutcome.AlreadyDecided:
                throw new ConflictException(AccessRequestInboxRules.AlreadyDecidedCode, "This request was already approved or declined.");
        }

        var request = decision.Request!;
        var admin = request.DecidedByUserId is { } id ? await decidedBy.GetByIdAsync(id, cancellationToken) : null;
        return request.ToDto(decision.Player!, admin?.DisplayName, decision.Link);
    }
}

/// <summary>Approves an access request (<c>POST /admin/access-requests/{id}/approve</c>), for admins only.</summary>
/// <param name="Id">The request.</param>
/// <param name="ExpiresInDays">7, 30 or 90, or <see langword="null"/> for 30.</param>
/// <param name="PlayerIds">The players to show, or <see langword="null"/> for the requested player.</param>
/// <param name="Note">The personal note, or <see langword="null"/>.</param>
public sealed record ApproveAccessRequestCommand(Guid Id, int? ExpiresInDays, IReadOnlyList<Guid>? PlayerIds, string? Note) : ICommand<AccessRequestDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>An id, a known expiry choice, at most twenty players and a short note.</summary>
public sealed class ApproveAccessRequestCommandValidator : AbstractValidator<ApproveAccessRequestCommand>
{
    /// <summary>Builds the rules.</summary>
    public ApproveAccessRequestCommandValidator()
    {
        RuleFor(command => command.Id).ValidId().OverridePropertyName("id");
        RuleFor(command => command.ExpiresInDays)
            .Must(days => days is null || ShareLinkRules.IsExpiryChoice(days))
            .WithMessage(ShareLinkRules.ExpiryMessage)
            .OverridePropertyName("expiresInDays");
        RuleFor(command => command.PlayerIds)
            .Must(ids => ids is null || (ids.Count <= ShareLink.MaxPlayers && ids.All(id => id != Guid.Empty)))
            .WithMessage($"Choose at most {ShareLink.MaxPlayers} players.")
            .OverridePropertyName("playerIds");
        RuleFor(command => command.Note)
            .MaximumLength(ShareLink.MaxNoteLength)
            .OverridePropertyName("note");
    }
}

/// <summary>Handles <see cref="ApproveAccessRequestCommand"/>.</summary>
/// <param name="decider">Makes the decision.</param>
/// <param name="users">The users.</param>
/// <param name="currentUser">The caller.</param>
public sealed class ApproveAccessRequestCommandHandler(AccessRequestDecider decider, IRepository<User, Guid> users, ICurrentUser currentUser)
    : ICommandHandler<ApproveAccessRequestCommand, AccessRequestDto>
{
    /// <summary>Approves the request and answers it.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The decided request.</returns>
    /// <exception cref="NotFoundException">The request or a player does not exist.</exception>
    /// <exception cref="ConflictException">The request was decided before.</exception>
    public async Task<AccessRequestDto> Handle(ApproveAccessRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var adminId = ShareLinkRules.ActorId(currentUser) ?? throw new ForbiddenAccessException();
        var decision = await decider.ApproveAsync(request.Id, adminId, request.ExpiresInDays, request.PlayerIds, request.Note, cancellationToken);
        return await decision.ToAnswerAsync(users, cancellationToken);
    }
}

/// <summary>Declines an access request (<c>POST /admin/access-requests/{id}/decline</c>), for admins only.</summary>
/// <param name="Id">The request.</param>
public sealed record DeclineAccessRequestCommand(Guid Id) : ICommand<AccessRequestDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="DeclineAccessRequestCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class DeclineAccessRequestCommandValidator : AbstractValidator<DeclineAccessRequestCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeclineAccessRequestCommandValidator() => RuleFor(command => command.Id).ValidId().OverridePropertyName("id");
}

/// <summary>Handles <see cref="DeclineAccessRequestCommand"/>.</summary>
/// <param name="decider">Makes the decision.</param>
/// <param name="users">The users.</param>
/// <param name="currentUser">The caller.</param>
public sealed class DeclineAccessRequestCommandHandler(AccessRequestDecider decider, IRepository<User, Guid> users, ICurrentUser currentUser)
    : ICommandHandler<DeclineAccessRequestCommand, AccessRequestDto>
{
    /// <summary>Declines the request and answers it.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The decided request.</returns>
    /// <exception cref="NotFoundException">The request does not exist.</exception>
    /// <exception cref="ConflictException">The request was decided before.</exception>
    public async Task<AccessRequestDto> Handle(DeclineAccessRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var adminId = ShareLinkRules.ActorId(currentUser) ?? throw new ForbiddenAccessException();
        var decision = await decider.DeclineAsync(request.Id, adminId, cancellationToken);
        return await decision.ToAnswerAsync(users, cancellationToken);
    }
}

/// <summary>An admin approves or declines a request from the bot's alert (P-4). Approving uses the defaults: 30 days, the requested player, no note.</summary>
/// <param name="AdminTelegramId">Who pressed.</param>
/// <param name="RequestId">The request.</param>
/// <param name="Approve"><see langword="true"/> for Approve, <see langword="false"/> for Decline.</param>
public sealed record DecideAccessRequestFromBotCommand(long AdminTelegramId, Guid RequestId, bool Approve) : ICommand<AccessRequestDecisionResult>;

/// <summary>Validates <see cref="DecideAccessRequestFromBotCommand"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class DecideAccessRequestFromBotCommandValidator : AbstractValidator<DecideAccessRequestFromBotCommand>
{
    /// <summary>Builds the rules.</summary>
    public DecideAccessRequestFromBotCommandValidator()
    {
        RuleFor(command => command.AdminTelegramId).ValidTelegramId().OverridePropertyName("adminTelegramId");
        RuleFor(command => command.RequestId).ValidId().OverridePropertyName("requestId");
    }
}

/// <summary>Handles <see cref="DecideAccessRequestFromBotCommand"/>: only active admins decide, and the first decision is final.</summary>
/// <param name="users">The users.</param>
/// <param name="decider">Makes the decision.</param>
public sealed class DecideAccessRequestFromBotCommandHandler(IRepository<User, Guid> users, AccessRequestDecider decider)
    : ICommandHandler<DecideAccessRequestFromBotCommand, AccessRequestDecisionResult>
{
    /// <summary>Checks the admin, decides, and describes what happened for the bot.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened.</returns>
    public async Task<AccessRequestDecisionResult> Handle(DecideAccessRequestFromBotCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var admin = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(request.AdminTelegramId), cancellationToken);
        if (admin is not { IsAdmin: true })
        {
            return new AccessRequestDecisionResult(AccessRequestDecisionOutcome.NotAllowed);
        }

        var decision = request.Approve
            ? await decider.ApproveAsync(request.RequestId, admin.Id, null, null, null, cancellationToken)
            : await decider.DeclineAsync(request.RequestId, admin.Id, cancellationToken);
        if (decision.Request is not { } decided)
        {
            return new AccessRequestDecisionResult(AccessRequestDecisionOutcome.NotFound);
        }

        var decidedBy = decided.DecidedByUserId == admin.Id
            ? admin
            : decided.DecidedByUserId is { } id ? await users.GetByIdAsync(id, cancellationToken) : null;
        return new AccessRequestDecisionResult(
            decision.Outcome,
            decided.Status,
            decided.Email.Value,
            decision.Player?.Name?.Value ?? string.Empty,
            decision.Link?.Code.Value ?? string.Empty,
            decidedBy?.DisplayName ?? string.Empty);
    }
}

/// <summary>Lists access requests (<c>GET /admin/access-requests</c>), newest first, one page at a time, for admins only.</summary>
/// <param name="Paging">The page to read.</param>
/// <param name="Status"><c>pending</c> (the default), <c>approved</c> or <c>declined</c> (any case).</param>
public sealed record GetAccessRequestsQuery(PageRequest Paging, string? Status) : IPagedQuery<AccessRequestDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>The status names a known status.</summary>
public sealed class GetAccessRequestsQueryValidator : AbstractValidator<GetAccessRequestsQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAccessRequestsQueryValidator() =>
        RuleFor(query => query.Status)
            .Must(status => AccessRequestInboxRules.TryParseStatus(status, out _))
            .WithMessage("Use pending, approved or declined.")
            .OverridePropertyName("status");
}

/// <summary>Handles <see cref="GetAccessRequestsQuery"/>.</summary>
/// <param name="reader">The inbox read model.</param>
public sealed class GetAccessRequestsQueryHandler(IAccessRequestListReader reader) : IQueryHandler<GetAccessRequestsQuery, PagedResult<AccessRequestDto>>
{
    /// <summary>Reads the page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public Task<PagedResult<AccessRequestDto>> Handle(GetAccessRequestsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AccessRequestInboxRules.TryParseStatus(request.Status, out var status);
        return reader.PageAsync(status, request.Paging, cancellationToken);
    }
}
