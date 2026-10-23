using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SsSport.Drive.Application.Behaviors;

/// <summary>
/// Outermost behaviour: logs every request by type name with its duration. Request data is never logged,
/// because commands carry personal data (emails, OTP codes).
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
            LogFailed(logger, exception, requestName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    /// <summary>Logs a handled request.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="requestName">The request type name.</param>
    /// <param name="elapsedMilliseconds">How long the request took.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMilliseconds:0} ms")]
    private static partial void LogHandled(ILogger logger, string requestName, double elapsedMilliseconds);

    /// <summary>Logs a failed request.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="requestName">The request type name.</param>
    /// <param name="elapsedMilliseconds">How long the request ran before failing.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Request {RequestName} failed after {ElapsedMilliseconds:0} ms")]
    private static partial void LogFailed(ILogger logger, Exception exception, string requestName, double elapsedMilliseconds);
}
