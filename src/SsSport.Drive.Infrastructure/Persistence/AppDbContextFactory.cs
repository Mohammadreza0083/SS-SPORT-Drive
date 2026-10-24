using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting a host. Creating migrations needs no database;
/// commands that do (e.g. <c>database update</c>) read <c>ConnectionStrings__DefaultConnection</c> from the environment.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>A placeholder used when no connection string is set; enough for <c>migrations add</c>.</summary>
    private const string PlaceholderConnectionString = "Host=localhost;Database=ssdrive;Username=ssdrive";

    /// <summary>Creates the context for design-time tools.</summary>
    /// <param name="args">Tool arguments (unused).</param>
    /// <returns>The context.</returns>
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? PlaceholderConnectionString;
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(connectionString).Options);
    }
}
