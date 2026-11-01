using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Api.ErrorHandling;

/// <summary>
/// Turns failures raised by the MediatR pipeline into ProblemDetails: validation 400 (errors per field),
/// sign-in required 401, missing role 403, not found 404, a concurrent change 409 and an outside service down 503
/// (both with a <c>code</c>).
/// Every other exception falls through to the next handler.
/// </summary>
/// <param name="problemDetails">Writes ProblemDetails responses.</param>
public sealed class ApplicationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <summary>Handles validation, authentication and authorization failures.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="cancellationToken">Cancels writing the response.</param>
    /// <returns><see langword="true"/> when a response was written.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            RequestValidationException validation => new HttpValidationProblemDetails(
                validation.Errors.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            UnauthenticatedException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Sign-in is required.",
            },
            ForbiddenAccessException => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "You do not have access to this action.",
            },
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = notFound.Message,
            },
            ShareLinkUnavailableException unavailableLink => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Link not available.",
                Detail = unavailableLink.Message,
                Extensions = { ["code"] = ShareLinkUnavailableException.Code },
            },
            ConcurrencyConflictException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = conflict.Message,
                Extensions = { ["code"] = ConcurrencyConflictException.Code },
            },
            ServiceUnavailableException unavailable => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = unavailable.Message,
                Extensions = { ["code"] = unavailable.Code },
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
