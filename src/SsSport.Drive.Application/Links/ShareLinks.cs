using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Links;

/// <summary>Who may manage links, and the expiry choices of the New share link modal (brief §11).</summary>
public static class ShareLinkRules
{
    /// <summary>The audit target type of a link.</summary>
    public const string AuditTarget = nameof(ShareLink);

    /// <summary>Gets the roles allowed to manage links: admins only (brief §12).</summary>
    public static IReadOnlyCollection<string> AdminRoles { get; } = [Role.Admin.Name];

    /// <summary>Gets the expiry choices in days. Every link expires (owner, 2026-11-13).</summary>
    public static IReadOnlyCollection<int> ExpiryDays { get; } = [7, 30, 90];

    /// <summary>The message for an expiry that is not one of the choices.</summary>
    public const string ExpiryMessage = "Use 7, 30 or 90 days; every link expires.";

    /// <summary>Checks an expiry choice.</summary>
    /// <param name="days">The days; <see langword="null"/> is refused.</param>
    /// <returns><see langword="true"/> when it is one of the choices.</returns>
    public static bool IsExpiryChoice(int? days) => days is { } value && ExpiryDays.Contains(value);

    /// <summary>Turns an expiry choice into a moment.</summary>
    /// <param name="days">The days (validated first).</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>The expiry, or <see langword="null"/> when no days were given (refused by the domain).</returns>
    public static DateTimeOffset? ExpiryFrom(int? days, DateTimeOffset now) => days is { } value ? now.AddDays(value) : null;

    /// <summary>Reads the caller's user id for the audit log.</summary>
    /// <param name="currentUser">The caller.</param>
    /// <returns>The id, or <see langword="null"/>.</returns>
    public static Guid? ActorId(ICurrentUser currentUser) => Guid.TryParse(currentUser.UserId, out var id) ? id : null;

    /// <summary>
    /// Finds the link behind a <c>/r/{code}</c> code and checks that it opens now. Unknown, malformed, paused, revoked
    /// and expired codes all fail the same way, so codes cannot be probed.
    /// </summary>
    /// <param name="links">The links.</param>
    /// <param name="rawCode">The code from the URL.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The open link.</returns>
    /// <exception cref="ShareLinkUnavailableException">The link does not open.</exception>
    public static async Task<ShareLink> OpenLinkAsync(this IRepository<ShareLink, Guid> links, string rawCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ShareLinkCode code;
        try
        {
            code = ShareLinkCode.Create(rawCode);
        }
        catch (DomainException)
        {
            throw new ShareLinkUnavailableException();
        }

        var link = await links.FirstOrDefaultAsync(new ShareLinkByCodeSpec(code), cancellationToken);
        return link is not null && link.AccessAt(now) == ShareLinkAccess.Open ? link : throw new ShareLinkUnavailableException();
    }

    /// <summary>Loads a link's recipient and players and maps it.</summary>
    /// <param name="link">The link.</param>
    /// <param name="recipients">The recipients.</param>
    /// <param name="players">The players.</param>
    /// <param name="now">The moment the access is judged at.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The DTO.</returns>
    public static async Task<ShareLinkDto> ToDtoAsync(
        this ShareLink link,
        IRepository<Recipient, Guid> recipients,
        IRepository<Player, Guid> players,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipient = await recipients.GetByIdAsync(link.RecipientId, cancellationToken) ?? throw new NotFoundException("Recipient");
        var linked = await players.ListAsync(new PlayersByIdsSpec(link.PlayerIds), cancellationToken);
        return link.ToDto(recipient, linked, now);
    }
}

/// <summary>A new recipient, typed into the New share link modal.</summary>
public sealed record NewRecipientDto
{
    /// <summary>Gets the person's name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the club or agency, or <see langword="null"/>.</summary>
    public string? Organisation { get; init; }

    /// <summary>Gets the organisation's two-letter country code, or <see langword="null"/>.</summary>
    public string? CountryCode { get; init; }

    /// <summary>Gets one to five email addresses.</summary>
    public IReadOnlyList<string>? Emails { get; init; }
}

/// <summary>The New share link modal (brief §11): a recipient (new, or an existing one by id), players, note, expiry and switches.</summary>
public sealed record CreateShareLinkDto
{
    /// <summary>Gets an existing recipient, or <see langword="null"/> when <see cref="Recipient"/> is sent.</summary>
    public Guid? RecipientId { get; init; }

    /// <summary>Gets a new recipient, or <see langword="null"/> when <see cref="RecipientId"/> is sent.</summary>
    public NewRecipientDto? Recipient { get; init; }

    /// <summary>Gets one to twenty published players, in page order.</summary>
    public IReadOnlyList<Guid>? PlayerIds { get; init; }

    /// <summary>Gets the personal note (at most 500 characters), or <see langword="null"/>.</summary>
    public string? Note { get; init; }

    /// <summary>Gets 7, 30 or 90 days; required (owner, 2026-11-13).</summary>
    public int? ExpiresInDays { get; init; }

    /// <summary>Gets whether another device must verify an email first (default <see langword="true"/>).</summary>
    public bool? VerifyOnNewDevice { get; init; }

    /// <summary>Gets whether a forwarded link may open after verifying (default <see langword="true"/>).</summary>
    public bool? AllowForwarding { get; init; }

    /// <summary>Gets <c>AnyEmail</c> (default) or <c>SameDomain</c>.</summary>
    public string? ForwardingScope { get; init; }

    /// <summary>Gets whether admins are told when it is opened (default <see langword="false"/>).</summary>
    public bool? NotifyOnOpen { get; init; }

    /// <summary>Turns the modal into its command.</summary>
    /// <returns>The command.</returns>
    public CreateShareLinkCommand ToCommand() =>
        new(RecipientId, Recipient, PlayerIds ?? [], Note, ExpiresInDays, VerifyOnNewDevice, AllowForwarding, ForwardingScope, NotifyOnOpen);
}

/// <summary>Creates a personal link (<c>POST /admin/links</c>, L-1), for admins only.</summary>
/// <param name="RecipientId">An existing recipient, or <see langword="null"/>.</param>
/// <param name="Recipient">A new recipient, or <see langword="null"/>.</param>
/// <param name="PlayerIds">The players, in page order.</param>
/// <param name="Note">The personal note, or <see langword="null"/>.</param>
/// <param name="ExpiresInDays">7, 30 or 90; required.</param>
/// <param name="VerifyOnNewDevice">The switch, or <see langword="null"/> for the default.</param>
/// <param name="AllowForwarding">The switch, or <see langword="null"/> for the default.</param>
/// <param name="ForwardingScope">The scope name, or <see langword="null"/> for the default.</param>
/// <param name="NotifyOnOpen">The switch, or <see langword="null"/> for the default.</param>
public sealed record CreateShareLinkCommand(
    Guid? RecipientId,
    NewRecipientDto? Recipient,
    IReadOnlyList<Guid> PlayerIds,
    string? Note,
    int? ExpiresInDays,
    bool? VerifyOnNewDevice,
    bool? AllowForwarding,
    string? ForwardingScope,
    bool? NotifyOnOpen) : ICommand<ShareLinkDto>, IAuthorizedRequest
{
    /// <summary>The audit action.</summary>
    public const string CreatedAction = "share_link.created";

    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;

    /// <summary>Reads a forwarding scope by name, case-insensitively; numbers are not names.</summary>
    /// <param name="value">The value sent.</param>
    /// <param name="scope">The scope.</param>
    /// <returns><see langword="true"/> when the value names a scope, or is left out.</returns>
    public static bool TryParseScope(string? value, out ForwardingScope scope)
    {
        scope = Domain.Links.ForwardingScope.AnyEmail;
        return string.IsNullOrEmpty(value)
            || (!value.Any(char.IsDigit) && Enum.TryParse(value, ignoreCase: true, out scope) && Enum.IsDefined(scope));
    }
}

/// <summary>Exactly one recipient, at least one player, an expiry choice and a known forwarding scope.</summary>
public sealed class CreateShareLinkCommandValidator : AbstractValidator<CreateShareLinkCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateShareLinkCommandValidator()
    {
        RuleFor(command => command)
            .Must(command => command.RecipientId is null != command.Recipient is null)
            .WithMessage("Send either recipientId or a new recipient, not both.")
            .OverridePropertyName("recipient");
        RuleFor(command => command.PlayerIds)
            .NotEmpty()
            .WithMessage("Choose at least one player.")
            .OverridePropertyName("playerIds");
        RuleFor(command => command.ExpiresInDays)
            .Must(ShareLinkRules.IsExpiryChoice)
            .WithMessage(ShareLinkRules.ExpiryMessage)
            .OverridePropertyName("expiresInDays");
        RuleFor(command => command.ForwardingScope)
            .Must(scope => CreateShareLinkCommand.TryParseScope(scope, out _))
            .WithMessage("Use AnyEmail or SameDomain.")
            .OverridePropertyName("forwardingScope");
    }
}

/// <summary>Handles <see cref="CreateShareLinkCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="players">The players.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class CreateShareLinkCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<Player, Guid> players,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateShareLinkCommand, ShareLinkDto>
{
    /// <summary>How many fresh codes to try before giving up; a clash is already one in billions.</summary>
    private const int CodeAttempts = 5;

    /// <summary>Finds or creates the recipient, loads the players, draws an unused code, then creates and audits the link.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The new link.</returns>
    /// <exception cref="NotFoundException">The recipient or a player does not exist.</exception>
    /// <exception cref="DomainException">A broken recipient or link rule (for example a waiting player).</exception>
    public async Task<ShareLinkDto> Handle(CreateShareLinkCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var recipient = await RecipientAsync(request, now, cancellationToken);
        var ids = request.PlayerIds.Distinct().ToList();
        var found = await players.ListAsync(new PlayersByIdsSpec(ids), cancellationToken);
        if (found.Count != ids.Count)
        {
            throw new NotFoundException("Player");
        }

        var byId = found.ToDictionary(player => player.Id);
        CreateShareLinkCommand.TryParseScope(request.ForwardingScope, out var scope);
        var defaults = ShareLinkSettings.Default;
        var settings = new ShareLinkSettings(
            request.VerifyOnNewDevice ?? defaults.VerifyOnNewDevice,
            request.AllowForwarding ?? defaults.AllowForwarding,
            scope,
            request.NotifyOnOpen ?? defaults.NotifyOnOpen);
        var actorId = ShareLinkRules.ActorId(currentUser);
        var link = ShareLink.Create(
            await UnusedCodeAsync(cancellationToken),
            recipient,
            [.. ids.Select(id => byId[id])],
            request.Note,
            ShareLinkRules.ExpiryFrom(request.ExpiresInDays, now),
            settings,
            actorId,
            now);

        links.Add(link);
        audit.Add(AuditEntry.Record(actorId, CreateShareLinkCommand.CreatedAction, ShareLinkRules.AuditTarget, link.Id.ToString(), null, now));
        return link.ToDto(recipient, found, now);
    }

    /// <summary>Loads the chosen recipient, or creates the new one.</summary>
    /// <param name="request">The command.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The recipient.</returns>
    /// <exception cref="NotFoundException">The chosen recipient does not exist.</exception>
    private async Task<Recipient> RecipientAsync(CreateShareLinkCommand request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (request.RecipientId is { } recipientId)
        {
            return await recipients.GetByIdAsync(recipientId, cancellationToken) ?? throw new NotFoundException("Recipient");
        }

        var form = request.Recipient!;
        var country = string.IsNullOrWhiteSpace(form.CountryCode) ? null : Nationality.Create(form.CountryCode);
        var recipient = Recipient.Create(form.Name, form.Organisation, country, (form.Emails ?? []).Select(EmailAddress.Create), now);
        recipients.Add(recipient);
        return recipient;
    }

    /// <summary>Draws codes until one is not used by any link.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The unused code.</returns>
    /// <exception cref="InvalidOperationException">Every attempt clashed.</exception>
    private async Task<ShareLinkCode> UnusedCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var code = ShareLinkCode.Generate();
            if (!await links.AnyAsync(new ShareLinkByCodeSpec(code), cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException($"No unused link code after {CodeAttempts} attempts.");
    }
}

/// <summary>Reads one link with its recipient and players (<c>GET /admin/links/{id}</c>), for admins only.</summary>
/// <param name="Id">The link.</param>
public sealed record GetShareLinkQuery(Guid Id) : IQuery<ShareLinkDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Handles <see cref="GetShareLinkQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="players">The players.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetShareLinkQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<Player, Guid> players,
    TimeProvider timeProvider) : IQueryHandler<GetShareLinkQuery, ShareLinkDto>
{
    /// <summary>Loads and maps the link.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The link.</returns>
    /// <exception cref="NotFoundException">The link does not exist.</exception>
    public async Task<ShareLinkDto> Handle(GetShareLinkQuery request, CancellationToken cancellationToken)
    {
        var link = await links.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Share link");
        return await link.ToDtoAsync(recipients, players, timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>Which links a list shows.</summary>
/// <param name="Status">Only links with this status, or <see langword="null"/> for all.</param>
/// <param name="PlayerId">Only links showing this player, or <see langword="null"/>.</param>
/// <param name="RecipientId">Only links for this recipient, or <see langword="null"/>.</param>
public sealed record ShareLinkListCriteria(ShareLinkStatus? Status, Guid? PlayerId, Guid? RecipientId);

/// <summary>Lists share links (<c>GET /admin/links</c>), newest first, one page at a time, for admins only.</summary>
/// <param name="Paging">The page to read.</param>
/// <param name="Status"><c>active</c>, <c>paused</c> or <c>revoked</c> (any case), or <see langword="null"/> for all.</param>
/// <param name="PlayerId">Only links showing this player, or <see langword="null"/>.</param>
/// <param name="RecipientId">Only links for this recipient, or <see langword="null"/>.</param>
public sealed record GetShareLinksQuery(PageRequest Paging, string? Status, Guid? PlayerId, Guid? RecipientId) : IPagedQuery<ShareLinkListItemDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;

    /// <summary>Reads a status by name, case-insensitively; numbers are not names.</summary>
    /// <param name="value">The value sent.</param>
    /// <param name="status">The status, or <see langword="null"/> when left out.</param>
    /// <returns><see langword="true"/> when the value names a status, or is left out.</returns>
    public static bool TryParseStatus(string? value, out ShareLinkStatus? status)
    {
        status = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (value.Any(char.IsDigit) || !Enum.TryParse<ShareLinkStatus>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        status = parsed;
        return true;
    }
}

/// <summary>The status names a known status.</summary>
public sealed class GetShareLinksQueryValidator : AbstractValidator<GetShareLinksQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetShareLinksQueryValidator() =>
        RuleFor(query => query.Status)
            .Must(status => GetShareLinksQuery.TryParseStatus(status, out _))
            .WithMessage("Use active, paused or revoked.")
            .OverridePropertyName("status");
}

/// <summary>Handles <see cref="GetShareLinksQuery"/>.</summary>
/// <param name="reader">The share links read model.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetShareLinksQueryHandler(IShareLinkListReader reader, TimeProvider timeProvider) : IQueryHandler<GetShareLinksQuery, PagedResult<ShareLinkListItemDto>>
{
    /// <summary>Reads the page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public Task<PagedResult<ShareLinkListItemDto>> Handle(GetShareLinksQuery request, CancellationToken cancellationToken)
    {
        GetShareLinksQuery.TryParseStatus(request.Status, out var status);
        return reader.PageAsync(new ShareLinkListCriteria(status, request.PlayerId, request.RecipientId), request.Paging, timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>A change to a link's status.</summary>
public enum ShareLinkStatusChange
{
    /// <summary>Stop it for now.</summary>
    Pause,

    /// <summary>Let a paused link open again.</summary>
    Resume,

    /// <summary>Stop it for good.</summary>
    Revoke,
}

/// <summary>Pauses, resumes or revokes a link (<c>POST /admin/links/{id}/pause|resume|revoke</c>), for admins only.</summary>
/// <param name="Id">The link.</param>
/// <param name="Change">What to do.</param>
public sealed record ChangeShareLinkStatusCommand(Guid Id, ShareLinkStatusChange Change) : ICommand<ShareLinkDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;

    /// <summary>Gets the audit action of the change, e.g. <c>share_link.paused</c>.</summary>
    public string Action => Change switch
    {
        ShareLinkStatusChange.Pause => "share_link.paused",
        ShareLinkStatusChange.Resume => "share_link.resumed",
        _ => "share_link.revoked",
    };
}

/// <summary>Handles <see cref="ChangeShareLinkStatusCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="players">The players.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ChangeShareLinkStatusCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<Player, Guid> players,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ChangeShareLinkStatusCommand, ShareLinkDto>
{
    /// <summary>Applies the change and audits it when something really changed.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The link after the change.</returns>
    /// <exception cref="NotFoundException">The link does not exist.</exception>
    /// <exception cref="DomainException"><c>share_link.revoked</c> when pausing or resuming a revoked link.</exception>
    public async Task<ShareLinkDto> Handle(ChangeShareLinkStatusCommand request, CancellationToken cancellationToken)
    {
        var link = await links.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Share link");
        var now = timeProvider.GetUtcNow();
        var changed = request.Change switch
        {
            ShareLinkStatusChange.Pause => link.Pause(),
            ShareLinkStatusChange.Resume => link.Resume(),
            _ => link.Revoke(now),
        };

        if (changed)
        {
            audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), request.Action, ShareLinkRules.AuditTarget, link.Id.ToString(), null, now));
        }

        return await link.ToDtoAsync(recipients, players, now, cancellationToken);
    }
}

/// <summary>A new expiry: 7, 30 or 90 days from now. It cannot be removed.</summary>
public sealed record ChangeShareLinkExpiryDto
{
    /// <summary>Gets 7, 30 or 90 days from now; required.</summary>
    public int? ExpiresInDays { get; init; }
}

/// <summary>Moves or removes a link's expiry (<c>PATCH /admin/links/{id}/expiry</c>), for admins only.</summary>
/// <param name="Id">The link.</param>
/// <param name="ExpiresInDays">7, 30 or 90 days from now; required.</param>
public sealed record ChangeShareLinkExpiryCommand(Guid Id, int? ExpiresInDays) : ICommand<ShareLinkDto>, IAuthorizedRequest
{
    /// <summary>The audit action.</summary>
    public const string ExpiryChangedAction = "share_link.expiry_changed";

    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>The expiry is one of the choices.</summary>
public sealed class ChangeShareLinkExpiryCommandValidator : AbstractValidator<ChangeShareLinkExpiryCommand>
{
    /// <summary>Builds the rules.</summary>
    public ChangeShareLinkExpiryCommandValidator() =>
        RuleFor(command => command.ExpiresInDays)
            .Must(ShareLinkRules.IsExpiryChoice)
            .WithMessage(ShareLinkRules.ExpiryMessage)
            .OverridePropertyName("expiresInDays");
}

/// <summary>Handles <see cref="ChangeShareLinkExpiryCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="players">The players.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ChangeShareLinkExpiryCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<Player, Guid> players,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ChangeShareLinkExpiryCommand, ShareLinkDto>
{
    /// <summary>Changes the expiry and audits it when it really changed.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The link after the change.</returns>
    /// <exception cref="NotFoundException">The link does not exist.</exception>
    /// <exception cref="DomainException"><c>share_link.revoked</c>.</exception>
    public async Task<ShareLinkDto> Handle(ChangeShareLinkExpiryCommand request, CancellationToken cancellationToken)
    {
        var link = await links.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Share link");
        var now = timeProvider.GetUtcNow();
        if (link.ChangeExpiry(ShareLinkRules.ExpiryFrom(request.ExpiresInDays, now), now))
        {
            audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), ChangeShareLinkExpiryCommand.ExpiryChangedAction, ShareLinkRules.AuditTarget, link.Id.ToString(), null, now));
        }

        return await link.ToDtoAsync(recipients, players, now, cancellationToken);
    }
}

/// <summary>
/// Tells anyone whether a code opens (<c>GET /links/{code}</c>). Unknown, malformed, paused, revoked and expired codes all
/// raise the same <see cref="ShareLinkUnavailableException"/>, so codes cannot be probed.
/// </summary>
/// <param name="Code">The code from the URL.</param>
public sealed record ResolveShareLinkQuery(string Code) : IQuery<ResolvedShareLinkDto>;

/// <summary>Handles <see cref="ResolveShareLinkQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="players">The players, for the gate card.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ResolveShareLinkQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<Player, Guid> players,
    TimeProvider timeProvider) : IQueryHandler<ResolveShareLinkQuery, ResolvedShareLinkDto>
{
    /// <summary>Finds the link and checks that it opens now.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>What the viewer site needs to show the email gate.</returns>
    /// <exception cref="ShareLinkUnavailableException">The link does not open.</exception>
    public async Task<ResolvedShareLinkDto> Handle(ResolveShareLinkQuery request, CancellationToken cancellationToken)
    {
        var link = await links.OpenLinkAsync(request.Code, timeProvider.GetUtcNow(), cancellationToken);
        var recipient = await recipients.GetByIdAsync(link.RecipientId, cancellationToken) ?? throw new ShareLinkUnavailableException();
        var player = link.Type == ShareLinkType.Personal ? await players.GetByIdAsync(link.PlayerIds[0], cancellationToken) : null;
        return new ResolvedShareLinkDto
        {
            Code = link.Code.Value,
            Type = link.Type.ToString(),
            PlayerCount = link.PlayerIds.Count,
            RecipientHint = recipient.Emails[0].ToString(),
            Player = player is { Name: { } name, Positions: { } positions, Nationality: { } nationality }
                ? new GatePlayerDto
                {
                    Name = name.Value,
                    Position = positions.Primary.Code(),
                    Nationality = new CountryDto { Code = nationality.Code, Name = nationality.CountryName },
                }
                : null,
        };
    }
}

/// <summary>Validates <see cref="ChangeShareLinkStatusCommand"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class ChangeShareLinkStatusCommandValidator : AbstractValidator<ChangeShareLinkStatusCommand>
{
    /// <summary>Builds the rules.</summary>
    public ChangeShareLinkStatusCommandValidator()
    {
        RuleFor(r => r.Id).ValidId().OverridePropertyName("id");
        RuleFor(r => r.Change).IsInEnum().OverridePropertyName("change");
    }
}

/// <summary>Validates <see cref="GetShareLinkQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetShareLinkQueryValidator : AbstractValidator<GetShareLinkQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetShareLinkQueryValidator()
    {
        RuleFor(r => r.Id).ValidId().OverridePropertyName("id");
    }
}

/// <summary>Validates <see cref="ResolveShareLinkQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class ResolveShareLinkQueryValidator : AbstractValidator<ResolveShareLinkQuery>
{
    /// <summary>Builds the rules.</summary>
    public ResolveShareLinkQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
    }
}
