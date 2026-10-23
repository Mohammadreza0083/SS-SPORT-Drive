using DotNetEnv;
using SsSport.Drive.Api;
using SsSport.Drive.Api.Endpoints;
using SsSport.Drive.Api.ErrorHandling;
using SsSport.Drive.Infrastructure;

// Local runs: load the git-ignored .env from the repository root. Real environment variables always win,
// and containers get their settings from Docker, so a missing .env is fine.
Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure();
builder.Services.AddWebCors();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddApiDocumentation();
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

// Unhandled exceptions become ProblemDetails: 422 for broken business rules, a generic 500 otherwise.
app.UseExceptionHandler();
app.UseCors(WebCors.PolicyName);

app.MapApiDocumentation();
app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>
/// Entry point of the API host. Declared partial so integration tests can boot it with WebApplicationFactory.
/// </summary>
public partial class Program;
