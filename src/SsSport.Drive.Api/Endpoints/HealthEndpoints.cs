using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SsSport.Drive.Api.Endpoints;

/// <summary>Liveness and readiness probes for Docker and the reverse proxy.</summary>
public static class HealthEndpoints
{
    /// <summary>Tag of the health checks that must pass before the API is ready (e.g. PostgreSQL).</summary>
    public const string ReadyTag = "ready";

    /// <summary>Maps <c>GET /health/live</c> and <c>GET /health/ready</c> under the "Health" tag. No authentication.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var health = app.MapGroup("/health").WithTags("Health").AllowAnonymous();

        health.MapGet("/live", Live)
            .WithName("GetLiveness")
            .WithSummary("Liveness probe")
            .WithDescription("Answers 200 OK as soon as the API process serves HTTP. It checks no dependencies, so a failing database never restarts the container.");

        health.MapGet("/ready", ReadyAsync)
            .WithName("GetReadiness")
            .WithSummary("Readiness probe")
            .WithDescription("Answers 200 OK when every dependency tagged \"ready\" (PostgreSQL) is reachable. Otherwise answers 503 with a ProblemDetails body, without connection details.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>Reports that the process is alive.</summary>
    /// <returns>200 OK.</returns>
    private static Ok Live() => TypedResults.Ok();

    /// <summary>Runs the readiness health checks.</summary>
    /// <param name="healthChecks">The health check service.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>200 OK when healthy, otherwise a 503 ProblemDetails.</returns>
    private static async Task<Results<Ok, ProblemHttpResult>> ReadyAsync(HealthCheckService healthChecks, CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(check => check.Tags.Contains(ReadyTag), cancellationToken);

        return report.Status == HealthStatus.Healthy
            ? TypedResults.Ok()
            : TypedResults.Problem(
                title: "The API is not ready.",
                detail: $"Unhealthy dependencies: {string.Join(", ", report.Entries.Where(entry => entry.Value.Status != HealthStatus.Healthy).Select(entry => entry.Key))}.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
