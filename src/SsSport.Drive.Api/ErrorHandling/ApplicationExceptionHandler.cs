using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Api.ErrorHandling;

/// <summary>
/// Turns failures raised by the MediatR pipeline into ProblemDetails: validation 400 (errors per field),
/// sign-in required 401 (a viewer who must pass the email gate with a <c>code</c>), missing role 403, not found 404,
/// a concurrent change 409, asking too often 429 (with <c>Retry-After</c>) and an outside service down 503 (all three
/// with a <c>code</c>).
/// Every other exception falls through to the next handler.
/// </summary>
/// <param name="problemDetails">Writes ProblemDetails responses.</param>
public sealed class ApplicationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <summary>Builds the 404 answer, with the stable code when the exception has one.</summary>
    /// <param name="notFound">The exception.</param>
    /// <returns>The problem.</returns>
    private static ProblemDetails NotFoundProblem(NotFoundException notFound)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = notFound.Message };
        if (notFound.Code is not null)
        {
            problem.Extensions["code"] = notFound.Code;
        }

        return problem;
    }

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
            NotFoundException notFound => NotFoundProblem(notFound),
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
            ViewerVerificationRequiredException verification => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = verification.Message,
                Extensions = { ["code"] = ViewerVerificationRequiredException.Code },
            },
            ForwardPendingException pending => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = pending.Message,
                Extensions = { ["code"] = ForwardPendingException.Code },
            },
            SignInInvalidException invalid => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = invalid.Message,
                Extensions = { ["code"] = SignInInvalidException.Code },
            },
            SignInRequiredException required => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = required.Message,
                Extensions = { ["code"] = SignInRequiredException.Code },
            },
            TooManyRequestsException tooMany => new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = tooMany.Message,
                Extensions = { ["code"] = tooMany.Code },
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
        if (exception is TooManyRequestsException limited)
        {
            httpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(limited.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
