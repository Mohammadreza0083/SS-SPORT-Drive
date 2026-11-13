using DotNetEnv;
using SsSport.Drive.Application;
using SsSport.Drive.Infrastructure;

// Local runs: load the git-ignored .env from the repository root. Real environment variables always win,
// and containers get their settings from Docker, so a missing .env is fine.
Env.NoClobber().TraversePath().Load();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// The ingest servers (Epic 3): Drive sync and copies on the ingest queue, ffmpeg and rendering on the media queue.
builder.Services.AddIngestServer();

var host = builder.Build();

await host.RunAsync();
