using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>Database settings (<c>Database__*</c>).</summary>
public sealed record DatabaseOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Database";

    /// <summary>The pool size when <c>Database__MaxPoolSize</c> is not set (D-15).</summary>
    public const int DefaultMaxPoolSize = 20;

    /// <summary>The wait for a free connection when <c>Database__PoolTimeoutSeconds</c> is not set (D-15).</summary>
    public const int DefaultPoolTimeoutSeconds = 5;

    /// <summary>Gets how long a SQL command may take before it is logged as slow, in milliseconds (D-14).</summary>
    public int SlowQueryThresholdMs { get; init; } = 500;

    /// <summary>
    /// Gets the most connections this process opens (D-15). Each host sets its share in its base settings, so the
    /// API and the workers together stay under PostgreSQL's <c>max_connections</c>. It wins over the connection string.
    /// </summary>
    public int MaxPoolSize { get; init; } = DefaultMaxPoolSize;

    /// <summary>
    /// Gets how long, in seconds, opening a connection may take, including the wait for a free pooled one (D-15).
    /// A full pool then fails fast as a busy database instead of holding the request. It wins over the connection string.
    /// </summary>
    public int PoolTimeoutSeconds { get; init; } = DefaultPoolTimeoutSeconds;
}

/// <summary>
/// Logs slow SQL commands (D-14). EF Core no longer logs every command, so a command that takes at least
/// <see cref="DatabaseOptions.SlowQueryThresholdMs"/> is logged as a Warning with its duration and SQL text. The text
/// carries parameter placeholders (<c>@p0</c>); parameter values are never logged.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="options">The threshold.</param>
public sealed partial class SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger, IOptions<DatabaseOptions> options) : DbCommandInterceptor
{
    /// <summary>The longest SQL text written to the log; longer commands are cut.</summary>
    private const int MaxSqlLength = 2000;

    /// <summary>Checks a query that returned rows.</summary>
    /// <param name="command">The command.</param>
    /// <param name="eventData">How long it took.</param>
    /// <param name="result">The reader.</param>
    /// <returns>The reader, unchanged.</returns>
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    /// <summary>Checks a query that returned rows.</summary>
    /// <param name="command">The command.</param>
    /// <param name="eventData">How long it took.</param>
    /// <param name="result">The reader.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The reader, unchanged.</returns>
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <summary>Checks a command that returned no rows.</summary>
    /// <param name="command">The command.</param>
    /// <param name="eventData">How long it took.</param>
    /// <param name="result">The affected row count.</param>
    /// <returns>The count, unchanged.</returns>
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    /// <summary>Checks a command that returned no rows.</summary>
    /// <param name="command">The command.</param>
    /// <param name="eventData">How long it took.</param>
    /// <param name="result">The affected row count.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The count, unchanged.</returns>
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <summary>Checks a command that returned one value.</summary>
    /// <param name="command">The command.</param>
    /// <param name="eventData">How long it took.</param>
    /// <param name="result">The value.</param>
    /// <returns>The value, unchanged.</returns>
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    /// <summary>Checks a command that returned one value.</summary>
    /// <param name="command">The command.</param>
    /// <param name="eventData">How long it took.</param>
    /// <param name="result">The value.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The value, unchanged.</returns>
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <summary>Logs the command when it took at least the threshold.</summary>
    /// <param name="command">The command; only its text is read, never its parameters' values.</param>
    /// <param name="eventData">How long it took.</param>
    private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
    {
        if (eventData.Duration.TotalMilliseconds < options.Value.SlowQueryThresholdMs)
        {
            return;
        }

        var sql = command.CommandText;
        LogSlow(logger, Math.Round(eventData.Duration.TotalMilliseconds, 1), sql.Length > MaxSqlLength ? sql[..MaxSqlLength] + "…" : sql);
    }

    /// <summary>Logs a slow command.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="DurationMs">How long it took, in milliseconds.</param>
    /// <param name="Sql">The SQL text, with placeholders.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Slow SQL command took {DurationMs} ms: {Sql}")]
    private static partial void LogSlow(ILogger logger, double DurationMs, string Sql);
}
