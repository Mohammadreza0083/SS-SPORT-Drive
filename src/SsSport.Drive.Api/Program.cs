using DotNetEnv;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api;
using SsSport.Drive.Api.Authentication;
using SsSport.Drive.Api.Endpoints;
using SsSport.Drive.Api.ErrorHandling;
using SsSport.Drive.Application;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Infrastructure;
using SsSport.Drive.Infrastructure.Persistence;

// Local runs: load the git-ignored .env from the repository root. Real environment variables always win,
// and containers get their settings from Docker, so a missing .env is fine.
Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddHttpContextAccessor();
builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpContextCurrentUser>());
builder.Services.AddWebCors();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddApiDocumentation();
builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

// `dotnet SsSport.Drive.Api.dll migrate` applies the database migrations and exits (one-shot job / pipeline step).
if (args is ["migrate"])
{
    await DatabaseMigrator.MigrateAsync(app.Services);
    return;
}

// Unhandled exceptions become ProblemDetails: 400 validation, 401/403 access, 422 broken business rules, a generic 500 otherwise.
app.UseExceptionHandler();
app.UseCors(WebCors.PolicyName);

app.MapApiDocumentation();
app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>
/// Entry point of the API host. Declared partial so integration tests can boot it with WebApplicationFactory.
/// </summary>
public partial class Program;
