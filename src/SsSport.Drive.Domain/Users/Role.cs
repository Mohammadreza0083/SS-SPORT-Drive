using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Users;

/// <summary>
/// A team member's role. The list is closed but extensible: add a static instance and include it in <see cref="All"/>.
/// Names match the role claims the API checks (<c>IAuthorizedRequest.RequiredRoles</c>).
/// </summary>
public sealed record Role
{
    /// <summary>Initializes a role.</summary>
    /// <param name="name">The role name.</param>
    private Role(string name) => Name = name;

    /// <summary>Gets the analyst role: uploads and manages their own players.</summary>
    public static Role Analyst { get; } = new("Analyst");

    /// <summary>Gets the admin role: everything, including publishing, share links and users.</summary>
    public static Role Admin { get; } = new("Admin");

    /// <summary>Gets every known role.</summary>
    public static IReadOnlyList<Role> All { get; } = [Analyst, Admin];

    /// <summary>Gets the role name.</summary>
    public string Name { get; }

    /// <summary>Finds a known role by name, ignoring case.</summary>
    /// <param name="name">The role name.</param>
    /// <returns>The role.</returns>
    /// <exception cref="DomainException"><c>user.role_unknown</c> for unknown names.</exception>
    public static Role FromName(string? name) =>
        All.FirstOrDefault(role => string.Equals(role.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException("user.role_unknown", "Unknown role.");

    /// <summary>Returns the role name.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
