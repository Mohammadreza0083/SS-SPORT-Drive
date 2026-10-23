using System.Security.Claims;
using SsSport.Drive.Application.Abstractions.Authentication;

namespace SsSport.Drive.Api.Authentication;

/// <summary>
/// The caller of the current HTTP request, read from its signed-in principal
/// (the admin and viewer sessions arrive with backlog A-1 and the viewer sign-in).
/// </summary>
/// <param name="httpContextAccessor">Gives access to the current request.</param>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <summary>Gets the signed-in principal, if any.</summary>
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    /// <summary>Gets a value indicating whether the request carries a signed-in principal.</summary>
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <summary>Gets the user id from the name-identifier claim, or <see langword="null"/> when anonymous.</summary>
    public string? UserId => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    /// <summary>Checks the principal's role claims.</summary>
    /// <param name="role">The role name.</param>
    /// <returns><see langword="true"/> when signed in with that role.</returns>
    public bool IsInRole(string role) => IsAuthenticated && Principal!.IsInRole(role);
}
