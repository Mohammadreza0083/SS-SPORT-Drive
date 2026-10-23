var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness probe for Docker and the reverse proxy: answers as soon as the process serves HTTP.
app.MapGet("/health/live", () => Results.Ok())
    .ExcludeFromDescription();

await app.RunAsync();

/// <summary>
/// Entry point of the API host. Declared partial so integration tests can boot it with WebApplicationFactory.
/// </summary>
public partial class Program;
