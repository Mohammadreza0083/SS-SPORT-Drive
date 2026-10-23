using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace SsSport.Drive.Infrastructure;

/// <summary>
/// Readiness check: healthy when a trivial query against the application's PostgreSQL database succeeds.
/// </summary>
/// <param name="dataSource">The shared PostgreSQL data source.</param>
public sealed class PostgreSqlHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    /// <summary>Runs <c>SELECT 1</c> against the database.</summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Healthy when the query succeeds, otherwise unhealthy without exposing connection details.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "PostgreSQL is unreachable.", exception);
        }
    }
}
