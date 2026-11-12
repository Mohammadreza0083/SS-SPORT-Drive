using System.Text.Json.Serialization;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api;
using SsSport.Drive.Api.Accounts;
using SsSport.Drive.Api.Authentication;
using SsSport.Drive.Api.Emails;
using SsSport.Drive.Api.Endpoints;
using SsSport.Drive.Api.ErrorHandling;
using SsSport.Drive.Api.Links;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Api.Telegram;
using SsSport.Drive.Api.Tracking;
using SsSport.Drive.Application;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Infrastructure;
using SsSport.Drive.Infrastructure.Persistence;
using SsSport.Drive.Infrastructure.Telegram;

// Local runs: load the git-ignored .env from the repository root. Real environment variables always win,
// and containers get their settings from Docker, so a missing .env is fine.
Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
// Do not advertise the server software.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
// Bot notices are sent by Hangfire jobs after the request (T-6c); their server runs here, on the notices queue only.
builder.Services.AddBotNoticeServer();
// Local development only: Telegram__Mode=Polling fetches updates through the VPN proxy (no webhook needed).
builder.Services.AddHostedService<TelegramPollingService>();
builder.Services.AddHttpContextAccessor();
builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpContextCurrentUser>());
// Mini App callers sign in per request with Telegram-signed initData (T-3); the admin console sends its magic-link session
// token in X-Admin-Session (A-1). One policy scheme picks the right handler, so every /admin/* endpoint accepts either.
builder.Services.AddAuthentication("Combined")
    .AddPolicyScheme("Combined", "Mini App signature or admin console session", options =>
        options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey(AdminSessionAuthenticationHandler.HeaderName)
            ? AdminSessionAuthenticationHandler.SchemeName
            : TelegramMiniAppAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TelegramMiniAppAuthenticationHandler>(TelegramMiniAppAuthenticationHandler.SchemeName, configureOptions: null)
    .AddScheme<AuthenticationSchemeOptions, AdminSessionAuthenticationHandler>(AdminSessionAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddWebCors();
// Security baseline (decisions §1b): HTTPS + HSTS behind the trusted proxy, and per-client rate limits.
builder.Services.AddHttpsEnforcement();
builder.Services.AddApiRateLimiting();
builder.Services.AddProblemDetails();
// Enums are written by name ("Waiting", not 0), so responses and Scalar stay readable.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<DatabaseBusyExceptionHandler>();
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

// The client's real scheme and address first, then the security headers, so every later answer carries them.
app.UseHttpsEnforcement();
app.UseSecurityHeaders();
// Unhandled exceptions become ProblemDetails: 400 validation, 401/403 access, 422 broken business rules, a generic 500 otherwise.
app.UseExceptionHandler();
app.UseCors(WebCors.PolicyName);
// Before authentication, so floods of bad sign-ins are refused cheaply.
app.UseRateLimiter();
app.UseAuthentication();

app.MapApiDocumentation();
app.MapHealthEndpoints();
app.MapTelegramWebhook();
app.MapMiniApp();
app.MapShareLinks();
app.MapEmailQueue();
app.MapSignIn();
app.MapViewerGate();
app.MapViewerContent();
app.MapTracking();

await app.RunAsync();

/// <summary>
/// Entry point of the API host. Declared partial so integration tests can boot it with WebApplicationFactory.
/// </summary>
public partial class Program;
