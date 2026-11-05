using Npgsql;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Recognises a busy database (D-15): this process's pool had no free connection within
/// <see cref="DatabaseOptions.PoolTimeoutSeconds"/>, or PostgreSQL refused a new connection because it is full
/// ("too many clients", SQL state 53300). Both pass once load drops, so callers answer "try again" instead of failing.
/// </summary>
public static class DatabaseBusy
{
    /// <summary>Tells whether the exception, or one it wraps (EF Core wraps provider errors), is a busy database.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> for a full pool or a full server.</returns>
    public static bool IsBusy(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException { SqlState: PostgresErrorCodes.TooManyConnections }:
                    return true;

                // Npgsql reports a pool wait that ran out as an NpgsqlException wrapping a TimeoutException; it is
                // raised before any connection to the server, so it is never a slow command.
                case NpgsqlException { InnerException: TimeoutException } when current.Message.Contains("pool", StringComparison.OrdinalIgnoreCase):
                    return true;
            }
        }

        return false;
    }
}
