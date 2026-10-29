using Riok.Mapperly.Abstractions;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Users;

/// <summary>Maps <see cref="User"/> to <see cref="UserDto"/> at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class UserMapper
{
    /// <summary>Maps a user to its DTO.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The DTO.</returns>
    public static partial UserDto ToDto(this User user);

    /// <summary>Maps a user to the identity sign-in needs.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The DTO.</returns>
    public static partial TeamMemberDto ToTeamMemberDto(this User user);

    /// <summary>Turns an email into the full address (the DTO is admin-only).</summary>
    /// <param name="email">The email.</param>
    /// <returns>The address.</returns>
    private static string FromEmail(EmailAddress email) => email.Value;

    /// <summary>Turns a role into its name.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The name.</returns>
    private static string FromRole(Role role) => role.Name;
}
