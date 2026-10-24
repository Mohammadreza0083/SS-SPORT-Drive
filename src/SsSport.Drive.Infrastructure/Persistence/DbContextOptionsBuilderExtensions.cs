using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>The one place that decides how the application talks to PostgreSQL, shared by the host, tools and tests.</summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>Uses PostgreSQL through a shared data source, with snake_case names.</summary>
    /// <param name="options">The options builder.</param>
    /// <param name="dataSource">The shared Npgsql data source.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder UseAppDatabase(this DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseNpgsql(dataSource).UseSnakeCaseNamingConvention();
    }

    /// <summary>Uses PostgreSQL through a connection string, with snake_case names (tools and tests).</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="options">The options builder.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseAppDatabase<TContext>(this DbContextOptionsBuilder<TContext> options, string connectionString)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }
}
