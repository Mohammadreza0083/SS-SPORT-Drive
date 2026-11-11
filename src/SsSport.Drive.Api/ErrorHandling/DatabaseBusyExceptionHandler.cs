using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Api.ErrorHandling;

/// <summary>
/// Turns a busy database (D-15: the connection pool is full, or PostgreSQL refuses new connections) into a 503
/// ProblemDetails with the stable code <c>database.busy</c> and <c>Retry-After: 1</c>, so clients retry instead of
/// seeing a server error. It is logged as a Warning, not an Error. Every other exception falls through.
/// </summary>
/// <param name="problemDetails">Writes ProblemDetails responses.</param>
/// <param name="logger">Logs the busy database.</param>
public sealed partial class DatabaseBusyExceptionHandler(IProblemDetailsService problemDetails, ILogger<DatabaseBusyExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>The stable code clients map to a "try again" message.</summary>
    public const string Code = "database.busy";

    /// <summary>Seconds the client should wait before retrying.</summary>
    private const string RetryAfterSeconds = "1";

    /// <summary>Handles a busy database only.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="cancellationToken">Cancels writing the response.</param>
    /// <returns><see langword="true"/> when the database was busy and a response was written.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!DatabaseBusy.IsBusy(exception))
        {
            return false;
        }

        LogDatabaseBusy(logger, httpContext.Request.Method, httpContext.Request.Path.Value ?? "/");
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "The service is busy. Please try again in a moment.",
                Extensions = { ["code"] = Code },
            },
        });
    }

    /// <summary>Logs a request refused because the database was busy.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The request path.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database busy (no free connection); answered {Method} {Path} with 503")]
    private static partial void LogDatabaseBusy(ILogger logger, string method, string path);
}
