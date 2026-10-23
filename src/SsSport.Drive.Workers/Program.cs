var builder = Host.CreateApplicationBuilder(args);

// Hangfire servers and background jobs are registered here as their features land (EPIC 3, EPIC 5).

var host = builder.Build();

await host.RunAsync();
