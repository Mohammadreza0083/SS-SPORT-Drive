using DotNetEnv;
using SsSport.Drive.Application;
using SsSport.Drive.Infrastructure;

// Local runs: load the git-ignored .env from the repository root. Real environment variables always win,
// and containers get their settings from Docker, so a missing .env is fine.
Env.NoClobber().TraversePath().Load();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Hangfire servers and background jobs are registered here as their features land (EPIC 3, EPIC 5).

var host = builder.Build();

await host.RunAsync();
