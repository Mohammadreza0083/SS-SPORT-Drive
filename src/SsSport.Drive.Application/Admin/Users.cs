using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Telegram.Access;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Admin;

/// <summary>The names and codes of the team and audit pages (A-8).</summary>
public static class AdminUserRules
{
    /// <summary>The audit action of a role change.</summary>
    public const string RoleChangedAction = "user.role_changed";

    /// <summary>The audit action of an email change.</summary>
    public const string EmailChangedAction = "user.email_changed";

    /// <summary>The audit action of a removal.</summary>
    public const string RemovedAction = "user.removed";

    /// <summary>The audit target type of a team member.</summary>
    public const string Target = nameof(User);

    /// <summary>Gets the statuses the team list can be filtered by.</summary>
    public static IReadOnlyList<string> StatusFilters { get; } = ["active", "pending", "declined", "removed"];

    /// <summary>How a member signs in: admins by an email link.</summary>
    public const string SignInEmail = "Email";

    /// <summary>How a member signs in: analysts by the Telegram bot.</summary>
    public const string SignInTelegram = "Telegram";

    /// <summary>Gets a value indicating whether a role name is one of the roles.</summary>
    /// <param name="role">The role name.</param>
    /// <returns><see langword="true"/> when it is Analyst or Admin.</returns>
    public static bool IsRole(string? role) => Role.All.Any(item => string.Equals(item.Name, role?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A team member as the console shows them: the email is masked.</summary>
public sealed record AdminUserDto
{
    /// <summary>Gets the user id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the Telegram username, or <see langword="null"/>.</summary>
    public string? Username { get; init; }

    /// <summary>Gets the masked email, or <see langword="null"/>.</summary>
    public string? Email { get; init; }

    /// <summary>Gets how the member signs in: <c>Email</c> (admins) or <c>Telegram</c> (analysts).</summary>
    public string SignIn { get; init; } = string.Empty;

    /// <summary>Gets the role.</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Gets the status: <c>Pending</c>, <c>Active</c>, <c>Declined</c> or <c>Removed</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets how many players the member is responsible for.</summary>
    public int PlayerCount { get; init; }

    /// <summary>Gets when the member last did something, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastActiveAt { get; init; }

    /// <summary>Gets when access was requested.</summary>
    public DateTimeOffset RequestedAt { get; init; }
}

/// <summary>Maps team members; shared by the list and the actions.</summary>
public static class AdminUserMapper
{
    /// <summary>Builds the console's view of a member.</summary>
    /// <param name="user">The member.</param>
    /// <param name="playerCount">How many players they are responsible for.</param>
    /// <returns>The DTO, with the email masked.</returns>
    public static AdminUserDto ToAdminDto(this User user, int playerCount) => new()
    {
        Id = user.Id,
        DisplayName = user.DisplayName,
        Username = user.Username,
        Email = user.Email?.ToString(),
        SignIn = user.Role == Role.Admin ? AdminUserRules.SignInEmail : AdminUserRules.SignInTelegram,
        Role = user.Role.Name,
        Status = user.Status.ToString(),
        PlayerCount = playerCount,
        LastActiveAt = user.LastActiveAt,
        RequestedAt = user.RequestedAt,
    };
}

/// <summary>The team members, optionally of one status; paged by display name.</summary>
public sealed class AdminUsersSpec : Specification<User>
{
    /// <summary>Filters by status when one is given.</summary>
    /// <param name="status">The status, or <see langword="null"/> for all.</param>
    public AdminUsersSpec(UserStatus? status)
    {
        if (status is { } value)
        {
            Query.Where(user => user.Status == value);
        }

        Query.OrderBy(user => user.DisplayName);
    }
}

/// <summary>The players an analyst is responsible for.</summary>
public sealed class PlayersOfAnalystSpec : Specification<Player>
{
    /// <summary>Selects the players.</summary>
    /// <param name="analystId">The analyst.</param>
    public PlayersOfAnalystSpec(Guid analystId) => Query.Where(player => player.AnalystId == analystId);
}

/// <summary>Reads the team (<c>GET /admin/users</c>), for admins only.</summary>
/// <param name="Paging">The page.</param>
/// <param name="Status">A status filter, or <see langword="null"/> for all.</param>
public sealed record GetAdminUsersQuery(PageRequest Paging, string? Status) : IPagedQuery<AdminUserDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetAdminUsersQuery"/> on the server (decisions §1b rule 3); an unknown status is a 400.</summary>
public sealed class GetAdminUsersQueryValidator : AbstractValidator<GetAdminUsersQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAdminUsersQueryValidator()
    {
        RuleFor(query => query.Paging).ValidPaging();
        RuleFor(query => query.Status)
            .Must(status => AdminUserRules.StatusFilters.Contains(status!, StringComparer.OrdinalIgnoreCase))
            .When(query => !string.IsNullOrEmpty(query.Status))
            .WithMessage("Use active, pending, declined or removed.")
            .OverridePropertyName("status");
    }
}

/// <summary>Handles <see cref="GetAdminUsersQuery"/>.</summary>
/// <param name="pages">The paged reader.</param>
/// <param name="lookup">Counts each member's players for the page.</param>
public sealed class GetAdminUsersQueryHandler(IPagedReader pages, IAdminLookup lookup) : IQueryHandler<GetAdminUsersQuery, PagedResult<AdminUserDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<PagedResult<AdminUserDto>> Handle(GetAdminUsersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        UserStatus? status = string.IsNullOrEmpty(request.Status) ? null : Enum.Parse<UserStatus>(request.Status, ignoreCase: true);
        var page = await pages.PageAsync(new AdminUsersSpec(status), request.Paging, user => user, cancellationToken);
        var counts = await lookup.PlayerCountsAsync([.. page.Items.Select(user => user.Id)], cancellationToken);
        return PagedResult<AdminUserDto>.Create([.. page.Items.Select(user => user.ToAdminDto(counts.GetValueOrDefault(user.Id)))], request.Paging, page.TotalCount);
    }
}

/// <summary>The body of the approve call.</summary>
public sealed record ApproveUserDto
{
    /// <summary>Gets the role to approve as: <c>Analyst</c> or <c>Admin</c>.</summary>
    public string? Role { get; init; }
}

/// <summary>Approves a pending request (<c>POST /admin/users/{id}/approve</c>), for admins only.</summary>
/// <param name="UserId">The user.</param>
/// <param name="Role">The role to approve as.</param>
public sealed record ApproveUserCommand(Guid UserId, string? Role) : ICommand<AdminUserDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="ApproveUserCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class ApproveUserCommandValidator : AbstractValidator<ApproveUserCommand>
{
    /// <summary>Builds the rules.</summary>
    public ApproveUserCommandValidator()
    {
        RuleFor(command => command.UserId).ValidId().OverridePropertyName("userId");
        RuleFor(command => command.Role).Must(AdminUserRules.IsRole).WithMessage("Use Analyst or Admin.").OverridePropertyName("role");
    }
}

/// <summary>Handles <see cref="ApproveUserCommand"/>.</summary>
/// <param name="users">The team.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ApproveUserCommandHandler(IRepository<User, Guid> users, IRepository<AuditEntry, Guid> audit, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<ApproveUserCommand, AdminUserDto>
{
    /// <summary>Approves the request.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The member.</returns>
    /// <exception cref="NotFoundException">There is no such user.</exception>
    /// <exception cref="DomainException"><c>user.admin_needs_email</c> or a state error.</exception>
    public async Task<AdminUserDto> Handle(ApproveUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetByIdAsync(request.UserId, cancellationToken) ?? throw new NotFoundException("User");
        var role = Role.FromName(request.Role);
        user.Approve(role);
        audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), ReviewAccessRequestCommandHandler.ApprovedAction, AdminUserRules.Target, user.Id.ToString(), $$"""{"role":"{{role.Name}}"}""", timeProvider.GetUtcNow()));
        return user.ToAdminDto(0);
    }
}

/// <summary>Declines a pending request (<c>POST /admin/users/{id}/decline</c>), for admins only.</summary>
/// <param name="UserId">The user.</param>
public sealed record DeclineUserCommand(Guid UserId) : ICommand<AdminUserDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="DeclineUserCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class DeclineUserCommandValidator : AbstractValidator<DeclineUserCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeclineUserCommandValidator() => RuleFor(command => command.UserId).ValidId().OverridePropertyName("userId");
}

/// <summary>Handles <see cref="DeclineUserCommand"/>.</summary>
/// <param name="users">The team.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class DeclineUserCommandHandler(IRepository<User, Guid> users, IRepository<AuditEntry, Guid> audit, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<DeclineUserCommand, AdminUserDto>
{
    /// <summary>Declines the request.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The member.</returns>
    /// <exception cref="NotFoundException">There is no such user.</exception>
    public async Task<AdminUserDto> Handle(DeclineUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetByIdAsync(request.UserId, cancellationToken) ?? throw new NotFoundException("User");
        var now = timeProvider.GetUtcNow();
        user.Decline(now);
        audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), ReviewAccessRequestCommandHandler.DeclinedAction, AdminUserRules.Target, user.Id.ToString(), null, now));
        return user.ToAdminDto(0);
    }
}

/// <summary>The body of the edit call.</summary>
public sealed record UpdateUserDto
{
    /// <summary>Gets the new email, or <see langword="null"/> to leave it.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the new role, or <see langword="null"/> to leave it.</summary>
    public string? Role { get; init; }
}

/// <summary>Changes a member's email and role (<c>PATCH /admin/users/{id}</c>), for admins only.</summary>
/// <param name="UserId">The user.</param>
/// <param name="Email">The new email, or <see langword="null"/>.</param>
/// <param name="Role">The new role, or <see langword="null"/>.</param>
public sealed record UpdateUserCommand(Guid UserId, string? Email, string? Role) : ICommand<AdminUserDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="UpdateUserCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpdateUserCommandValidator()
    {
        RuleFor(command => command.UserId).ValidId().OverridePropertyName("userId");
        RuleFor(command => command.Role).Must(AdminUserRules.IsRole).When(command => command.Role is not null).WithMessage("Use Analyst or Admin.").OverridePropertyName("role");
        RuleFor(command => command.Email)
            .Must(email => email!.Length <= 254 && IsEmail(email))
            .When(command => command.Email is not null)
            .WithMessage("Use a valid email address.")
            .OverridePropertyName("email");
    }

    /// <summary>Checks that a text is an email address the domain accepts.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is valid.</returns>
    private static bool IsEmail(string text)
    {
        try
        {
            _ = EmailAddress.Create(text);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}

/// <summary>Handles <see cref="UpdateUserCommand"/>: the email first, then the role; the last active admin is never demoted.</summary>
/// <param name="users">The team.</param>
/// <param name="audit">The audit log.</param>
/// <param name="lookup">Counts the member's players.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class UpdateUserCommandHandler(IRepository<User, Guid> users, IRepository<AuditEntry, Guid> audit, IAdminLookup lookup, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<UpdateUserCommand, AdminUserDto>
{
    /// <summary>Applies the change.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The member.</returns>
    /// <exception cref="NotFoundException">There is no such user.</exception>
    /// <exception cref="DomainException"><c>user.last_admin</c>, <c>user.admin_needs_email</c> or a state error.</exception>
    public async Task<AdminUserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetByIdAsync(request.UserId, cancellationToken) ?? throw new NotFoundException("User");
        var actor = ShareLinkRules.ActorId(currentUser);
        var now = timeProvider.GetUtcNow();
        if (request.Email is not null)
        {
            var email = EmailAddress.Create(request.Email);
            if (email != user.Email)
            {
                user.ChangeEmail(email);
                audit.Add(AuditEntry.Record(actor, AdminUserRules.EmailChangedAction, AdminUserRules.Target, user.Id.ToString(), null, now));
            }
        }

        if (request.Role is not null)
        {
            var role = Role.FromName(request.Role);
            if (role != user.Role)
            {
                if (user.IsAdmin && role != Role.Admin)
                {
                    await EnsureAnotherAdminAsync(cancellationToken);
                }

                var previous = user.Role.Name;
                user.ChangeRole(role);
                audit.Add(AuditEntry.Record(actor, AdminUserRules.RoleChangedAction, AdminUserRules.Target, user.Id.ToString(), $$"""{"from":"{{previous}}","to":"{{role.Name}}"}""", now));
            }
        }

        var counts = await lookup.PlayerCountsAsync([user.Id], cancellationToken);
        return user.ToAdminDto(counts.GetValueOrDefault(user.Id));
    }

    /// <summary>Refuses when this is the last active admin.</summary>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>A task that completes when another admin exists.</returns>
    /// <exception cref="DomainException"><c>user.last_admin</c>.</exception>
    private async Task EnsureAnotherAdminAsync(CancellationToken cancellationToken)
    {
        if (await users.CountAsync(new ActiveAdminsSpec(), cancellationToken) <= 1)
        {
            throw new DomainException("user.last_admin", "The last active admin cannot be demoted or removed.");
        }
    }
}

/// <summary>The body of the remove call.</summary>
public sealed record RemoveUserDto
{
    /// <summary>Gets the admin that takes over the member's players, when they have any.</summary>
    public Guid? ReassignTo { get; init; }
}

/// <summary>Removes a member and hands their players over (<c>POST /admin/users/{id}/remove</c>), for admins only.</summary>
/// <param name="UserId">The member.</param>
/// <param name="ReassignTo">The admin who takes over their players, or <see langword="null"/>.</param>
public sealed record RemoveUserCommand(Guid UserId, Guid? ReassignTo) : ICommand<AdminUserDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="RemoveUserCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class RemoveUserCommandValidator : AbstractValidator<RemoveUserCommand>
{
    /// <summary>Builds the rules.</summary>
    public RemoveUserCommandValidator() => RuleFor(command => command.UserId).ValidId().OverridePropertyName("userId");
}

/// <summary>Handles <see cref="RemoveUserCommand"/>.</summary>
/// <param name="users">The team.</param>
/// <param name="players">The players.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class RemoveUserCommandHandler(IRepository<User, Guid> users, IRepository<Player, Guid> players, IRepository<AuditEntry, Guid> audit, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<RemoveUserCommand, AdminUserDto>
{
    /// <summary>Removes the member and moves their players.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The member.</returns>
    /// <exception cref="NotFoundException">There is no such user.</exception>
    /// <exception cref="RequestValidationException">The member has players and no admin to take them.</exception>
    /// <exception cref="DomainException"><c>user.last_admin</c> for the last active admin.</exception>
    public async Task<AdminUserDto> Handle(RemoveUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetByIdAsync(request.UserId, cancellationToken) ?? throw new NotFoundException("User");
        if (user.IsAdmin && await users.CountAsync(new ActiveAdminsSpec(), cancellationToken) <= 1)
        {
            throw new DomainException("user.last_admin", "The last active admin cannot be demoted or removed.");
        }

        var owned = await players.ListAsync(new PlayersOfAnalystSpec(user.Id), cancellationToken);
        if (owned.Count > 0)
        {
            var target = request.ReassignTo is { } id ? await users.GetByIdAsync(id, cancellationToken) : null;
            if (target is not { IsAdmin: true } || target.Id == user.Id)
            {
                throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["reassignTo"] = ["Choose an active admin to take over the players."] });
            }

            foreach (var player in owned)
            {
                player.AssignAnalyst(target.Id);
            }
        }

        user.Remove();
        audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), AdminUserRules.RemovedAction, AdminUserRules.Target, user.Id.ToString(), $$"""{"players":{{owned.Count}}}""", timeProvider.GetUtcNow()));
        return user.ToAdminDto(0);
    }
}

/// <summary>One entry of the audit log as the console shows it.</summary>
public sealed record AdminAuditEntryDto
{
    /// <summary>Gets the entry id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets when it happened.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>Gets the acting member, or <see langword="null"/> for the system.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>Gets the acting member's name, or <see langword="null"/>.</summary>
    public string? ActorName { get; init; }

    /// <summary>Gets the action code, such as <c>user.approved</c>.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Gets what it was done to: the type.</summary>
    public string TargetType { get; init; } = string.Empty;

    /// <summary>Gets what it was done to: the id.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Gets the extra facts as JSON, or <see langword="null"/>; never an email.</summary>
    public string? Data { get; init; }
}

/// <summary>Every audit entry, optionally of one action; the cursor orders them newest first.</summary>
public sealed class AuditEntriesSpec : Specification<AuditEntry>
{
    /// <summary>Filters by action when one is given.</summary>
    /// <param name="action">The action code, or <see langword="null"/> for all.</param>
    public AuditEntriesSpec(string? action)
    {
        if (!string.IsNullOrEmpty(action))
        {
            Query.Where(entry => entry.Action == action);
        }
    }
}

/// <summary>Reads the audit log, newest first (<c>GET /admin/audit</c>), for admins only.</summary>
/// <param name="Action">An action code filter, or <see langword="null"/>.</param>
/// <param name="Paging">The cursor page.</param>
public sealed record GetAuditLogQuery(string? Action, CursorRequest Paging) : ICursorQuery<AdminAuditEntryDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetAuditLogQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetAuditLogQueryValidator : AbstractValidator<GetAuditLogQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAuditLogQueryValidator()
    {
        RuleFor(query => query.Paging).ValidCursorPaging();
        RuleFor(query => query.Action).MaximumLength(64).Matches("^[a-z][a-z0-9_.]*$").When(query => !string.IsNullOrEmpty(query.Action)).OverridePropertyName("action");
    }
}

/// <summary>Handles <see cref="GetAuditLogQuery"/>.</summary>
/// <param name="pages">The paged reader.</param>
/// <param name="lookup">Reads the actors' names for the page.</param>
public sealed class GetAuditLogQueryHandler(IPagedReader pages, IAdminLookup lookup) : IQueryHandler<GetAuditLogQuery, CursorResult<AdminAuditEntryDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page and the cursor for the next one.</returns>
    public async Task<CursorResult<AdminAuditEntryDto>> Handle(GetAuditLogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await pages.CursorPageAsync(new AuditEntriesSpec(request.Action), entry => entry.At, CursorDirection.Descending, request.Paging, entry => entry, cancellationToken);
        var names = await lookup.UserNamesAsync([.. page.Items.Where(entry => entry.ActorUserId is not null).Select(entry => entry.ActorUserId!.Value).Distinct()], cancellationToken);
        var rows = page.Items.Select(entry => new AdminAuditEntryDto
        {
            Id = entry.Id,
            At = entry.At,
            ActorId = entry.ActorUserId,
            ActorName = entry.ActorUserId is { } actor ? names.GetValueOrDefault(actor) : null,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            Data = entry.Data,
        }).ToList();
        return new CursorResult<AdminAuditEntryDto>(rows, page.NextCursor);
    }
}
