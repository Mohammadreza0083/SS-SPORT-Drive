using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Behaviors;

/// <summary>
/// Outermost behaviour: logs every request by type name with its duration. Request data is never logged,
/// because commands carry personal data (emails, OTP codes).
/// Refusals the API answers on purpose (401, 403, 400, 404, 409, 422) are logged at Information with their reason, an
/// outside service being down at Warning, and only real failures at Error, so error alerts stay meaningful (D-13).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="logger">The logger.</param>
public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Runs the rest of the pipeline and logs the outcome.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The handler's response.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(cancellationToken);
            LogHandled(logger, requestName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            switch (exception)
            {
                case DomainException rule:
                    LogRefused(logger, requestName, rule.Code, elapsed);
                    break;
                case UnauthenticatedException or ForbiddenAccessException or RequestValidationException or NotFoundException or ConcurrencyConflictException:
                    LogRefused(logger, requestName, exception.GetType().Name, elapsed);
                    break;
                case ServiceUnavailableException unavailable:
                    LogUnavailable(logger, requestName, unavailable.Code, elapsed);
                    break;
                default:
                    LogFailed(logger, exception, requestName, elapsed);
                    break;
            }

            throw;
        }
    }

    /// <summary>Logs a handled request.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="requestName">The request type name.</param>
    /// <param name="elapsedMilliseconds">How long the request took.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMilliseconds:0} ms")]
    private static partial void LogHandled(ILogger logger, string requestName, double elapsedMilliseconds);

    /// <summary>Logs a request refused on purpose (sign-in, role, validation, not found, conflict, business rule).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="requestName">The request type name.</param>
    /// <param name="reason">The refusal: the rule code, or the refusal type.</param>
    /// <param name="elapsedMilliseconds">How long the request ran.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Refused {RequestName} ({Reason}) after {ElapsedMilliseconds:0} ms")]
    private static partial void LogRefused(ILogger logger, string requestName, string reason, double elapsedMilliseconds);

    /// <summary>Logs a request that could not finish because an outside service is down.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="requestName">The request type name.</param>
    /// <param name="reason">The service code, e.g. <c>drive.unavailable</c>.</param>
    /// <param name="elapsedMilliseconds">How long the request ran.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Request {RequestName} could not finish ({Reason}) after {ElapsedMilliseconds:0} ms")]
    private static partial void LogUnavailable(ILogger logger, string requestName, string reason, double elapsedMilliseconds);

    /// <summary>Logs a failed request.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="requestName">The request type name.</param>
    /// <param name="elapsedMilliseconds">How long the request ran before failing.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Request {RequestName} failed after {ElapsedMilliseconds:0} ms")]
    private static partial void LogFailed(ILogger logger, Exception exception, string requestName, double elapsedMilliseconds);
}
