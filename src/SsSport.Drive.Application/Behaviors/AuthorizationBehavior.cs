using MediatR;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Application.Behaviors;

/// <summary>
/// Rejects <see cref="IAuthorizedRequest"/>s from anonymous callers (401) or callers without a required role (403),
/// before validation, so unauthorized callers learn nothing about a request's rules.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="currentUser">The caller.</param>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Checks the caller, then runs the rest of the pipeline.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The handler's response.</returns>
    /// <exception cref="UnauthenticatedException">The request needs a signed-in caller.</exception>
    /// <exception cref="ForbiddenAccessException">The caller lacks every required role.</exception>
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IAuthorizedRequest authorized)
        {
            if (!currentUser.IsAuthenticated)
            {
                throw new UnauthenticatedException();
            }

            if (authorized.RequiredRoles.Count > 0 && !authorized.RequiredRoles.Any(currentUser.IsInRole))
            {
                throw new ForbiddenAccessException();
            }
        }

        return next(cancellationToken);
    }
}
