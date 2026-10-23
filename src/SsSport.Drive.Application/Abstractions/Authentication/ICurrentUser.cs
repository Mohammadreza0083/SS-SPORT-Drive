namespace SsSport.Drive.Application.Abstractions.Authentication;

/// <summary>
/// The caller of the current request. The API host reads it from the signed-in session;
/// hosts without users (workers) see an anonymous caller.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Gets a value indicating whether the caller is signed in.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Gets the signed-in user's id, or <see langword="null"/> for anonymous callers.</summary>
    string? UserId { get; }

    /// <summary>Checks whether the caller has a role (for example <c>Admin</c>).</summary>
    /// <param name="role">The role name.</param>
    /// <returns><see langword="true"/> when the caller is signed in and has the role.</returns>
    bool IsInRole(string role);
}

/// <summary>The caller when no user is signed in or the host has no users at all.</summary>
internal sealed class AnonymousCurrentUser : ICurrentUser
{
    /// <summary>Gets <see langword="false"/>: nobody is signed in.</summary>
    public bool IsAuthenticated => false;

    /// <summary>Gets <see langword="null"/>: there is no user.</summary>
    public string? UserId => null;

    /// <summary>Always <see langword="false"/>.</summary>
    /// <param name="role">The role name.</param>
    /// <returns><see langword="false"/>.</returns>
    public bool IsInRole(string role) => false;
}
