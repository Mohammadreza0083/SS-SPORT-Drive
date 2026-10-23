using Microsoft.EntityFrameworkCore;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core context. Entity mappings live in <c>IEntityTypeConfiguration</c> classes
/// in this assembly and are picked up automatically (backlog D-8 adds them and the migrations).
/// </summary>
/// <param name="options">The context options (PostgreSQL via the shared data source).</param>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>Applies every entity configuration in this assembly, then the shared conventions.</summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.IgnoreDomainEvents();
    }
}
