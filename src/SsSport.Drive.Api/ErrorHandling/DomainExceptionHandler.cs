using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Api.ErrorHandling;

/// <summary>
/// Turns a <see cref="DomainException"/> (a broken business rule) into a 422 ProblemDetails response.
/// The response carries the rule's stable <c>code</c> for clients to map to UI copy; the exception message
/// is developer-facing and never sent. Every other exception falls through to the default 500 handler.
/// </summary>
/// <param name="problemDetails">Writes ProblemDetails responses.</param>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <summary>Handles domain rule violations only.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="cancellationToken">Cancels writing the response.</param>
    /// <returns><see langword="true"/> when the exception was a <see cref="DomainException"/> and a response was written.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "A business rule was violated.",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.21",
                Extensions = { ["code"] = domainException.Code },
            },
        });
    }
}
