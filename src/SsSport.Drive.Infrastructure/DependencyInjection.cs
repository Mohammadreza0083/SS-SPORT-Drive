using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure;

/// <summary>
/// Registers the Infrastructure layer's services with the dependency-injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Name of the PostgreSQL connection string (<c>ConnectionStrings__DefaultConnection</c> in .env).</summary>
    public const string ConnectionStringName = "DefaultConnection";

    /// <summary>
    /// Adds the shared PostgreSQL data source, the EF Core context and the persistence ports
    /// (unit of work + generic repository, one per request scope). The connection string is read lazily
    /// from configuration, so hosts and tests can supply it from any configuration source.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(provider => NpgsqlDataSource.Create(ReadConnectionString(provider.GetRequiredService<IConfiguration>())));
        services.AddSingleton<PostgreSqlHealthCheck>();

        services.AddDbContext<AppDbContext>((provider, options) => options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<,>), typeof(Repository<,>));
        return services;
    }

    /// <summary>Reads the PostgreSQL connection string and fails with a clear message when it is missing.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    private static string ReadConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings__{ConnectionStringName} in .env.");
        }

        return connectionString;
    }
}
