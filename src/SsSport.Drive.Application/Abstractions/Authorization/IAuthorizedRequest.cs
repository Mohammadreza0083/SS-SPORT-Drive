namespace SsSport.Drive.Application.Abstractions.Authorization;

/// <summary>
/// Marks a command or query that needs a signed-in caller. When <see cref="RequiredRoles"/> is not empty,
/// the caller also needs at least one of those roles. Checked by the authorization behaviour before validation.
/// </summary>
public interface IAuthorizedRequest
{
    /// <summary>Gets the roles allowed to send the request (any one is enough); empty means any signed-in user.</summary>
    IReadOnlyCollection<string> RequiredRoles { get; }
}
