using DotNetEnv;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SsSport.Drive.Api;
using SsSport.Drive.Infrastructure;

// Local runs: load the git-ignored .env from the repository root. Real environment variables always win,
// and containers get their settings from Docker, so a missing .env is fine.
Env.NoClobber().TraversePath().Load();

const string ReadyTag = "ready";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure();
builder.Services.AddWebCors();
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("database", tags: [ReadyTag]);

var app = builder.Build();

app.UseCors(WebCors.PolicyName);

// Liveness probe for Docker and the reverse proxy: answers as soon as the process serves HTTP.
app.MapGet("/health/live", () => Results.Ok())
    .ExcludeFromDescription();

// Readiness probe: the API can reach its dependencies (PostgreSQL).
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
    .ExcludeFromDescription();

await app.RunAsync();

/// <summary>
/// Entry point of the API host. Declared partial so integration tests can boot it with WebApplicationFactory.
/// </summary>
public partial class Program;
