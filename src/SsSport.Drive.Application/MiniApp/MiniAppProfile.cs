using Ardalis.Specification;
using Riok.Mapperly.Abstractions;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.MiniApp;

/// <summary>The signed-in team member, as the Mini App shows them. Never carries the email.</summary>
public sealed record MiniAppProfileDto
{
    /// <summary>Gets the user id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the Telegram user id.</summary>
    public long TelegramId { get; init; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the Telegram username without <c>@</c>, or <see langword="null"/>.</summary>
    public string? Username { get; init; }

    /// <summary>Gets the role name (<c>Analyst</c> or <c>Admin</c>).</summary>
    public string Role { get; init; } = string.Empty;
}

/// <summary>Maps <see cref="User"/> to <see cref="MiniAppProfileDto"/> at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class MiniAppProfileMapper
{
    /// <summary>Maps a user to their Mini App profile.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The DTO.</returns>
    public static partial MiniAppProfileDto ToMiniAppProfileDto(this User user);

    /// <summary>Turns a role into its name.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The name.</returns>
    private static string FromRole(Role role) => role.Name;
}

/// <summary>An active team member by id, read untracked.</summary>
public sealed class ActiveUserByIdSpec : Specification<User>, ISingleResultSpecification<User>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="id">The user id.</param>
    public ActiveUserByIdSpec(Guid id) => Query.Where(user => user.Id == id && user.Status == UserStatus.Active).AsNoTracking();
}

/// <summary>Who may use the Mini App.</summary>
public static class MiniAppAccess
{
    /// <summary>Gets the roles of every active team member: analysts and admins.</summary>
    public static IReadOnlyCollection<string> TeamRoles { get; } = [Role.Analyst.Name, Role.Admin.Name];
}

/// <summary>Who is using the Mini App (<c>GET /miniapp/me</c>); for active analysts and admins only.</summary>
public sealed record GetMiniAppProfileQuery : IQuery<MiniAppProfileDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Handles <see cref="GetMiniAppProfileQuery"/>.</summary>
/// <param name="currentUser">The caller.</param>
/// <param name="users">The users.</param>
public sealed class GetMiniAppProfileQueryHandler(ICurrentUser currentUser, IRepository<User, Guid> users) : IQueryHandler<GetMiniAppProfileQuery, MiniAppProfileDto>
{
    /// <summary>Reads the caller's profile.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ForbiddenAccessException">The caller was removed after signing in.</exception>
    public async Task<MiniAppProfileDto> Handle(GetMiniAppProfileQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.UserId, out var id))
        {
            throw new UnauthenticatedException();
        }

        var user = await users.FirstOrDefaultAsync(new ActiveUserByIdSpec(id), cancellationToken) ?? throw new ForbiddenAccessException();
        return user.ToMiniAppProfileDto();
    }
}
