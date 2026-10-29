using System.Text.Json.Serialization;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api;
using SsSport.Drive.Api.Authentication;
using SsSport.Drive.Api.Endpoints;
using SsSport.Drive.Api.ErrorHandling;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Telegram;
using SsSport.Drive.Application;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Infrastructure;
using SsSport.Drive.Infrastructure.Persistence;
using SsSport.Drive.Infrastructure.Telegram;

// Local runs: load the git-ignored .env from the repository root. Real environment variables always win,
// and containers get their settings from Docker, so a missing .env is fine.
Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
// Local development only: Telegram__Mode=Polling fetches updates through the VPN proxy (no webhook needed).
builder.Services.AddHostedService<TelegramPollingService>();
builder.Services.AddHttpContextAccessor();
builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpContextCurrentUser>());
// Mini App callers sign in per request with Telegram-signed initData (T-3). The admin and viewer sessions join later.
builder.Services.AddAuthentication(TelegramMiniAppAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TelegramMiniAppAuthenticationHandler>(TelegramMiniAppAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddWebCors();
builder.Services.AddProblemDetails();
// Enums are written by name ("Waiting", not 0), so responses and Scalar stay readable.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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

// `dotnet SsSport.Drive.Api.dll telegram-set-webhook` registers the bot's webhook with Telegram and exits (deploy step).
if (args is ["telegram-set-webhook"])
{
    await TelegramWebhookRegistrar.RegisterAsync(app.Services);
    return;
}

// Unhandled exceptions become ProblemDetails: 400 validation, 401/403 access, 422 broken business rules, a generic 500 otherwise.
app.UseExceptionHandler();
app.UseCors(WebCors.PolicyName);
app.UseAuthentication();

app.MapApiDocumentation();
app.MapHealthEndpoints();
app.MapTelegramWebhook();
app.MapMiniApp();

await app.RunAsync();

/// <summary>
/// Entry point of the API host. Declared partial so integration tests can boot it with WebApplicationFactory.
/// </summary>
public partial class Program;
