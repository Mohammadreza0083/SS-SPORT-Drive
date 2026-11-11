using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SsSport.Drive.Application;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Database connections ran out under load (D-15): every process opened up to Npgsql's default of 100 connections,
/// which is also PostgreSQL's whole limit. Each process now caps its pool (<c>Database__MaxPoolSize</c>) and waits only
/// briefly for a free connection (<c>Database__PoolTimeoutSeconds</c>), whatever the connection string says.
/// </summary>
public sealed class ConnectionPoolSettingsTests
{
    /// <summary>A connection string as hosts and .env give it, with no pool settings.</summary>
    private const string ConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    /// <summary>Without settings, a process opens at most 20 connections and waits at most 5 seconds for one.</summary>
    [Fact]
    public void Pool_is_capped_by_default()
    {
        using var services = BuildServices(new Dictionary<string, string?>());

        var pool = new NpgsqlConnectionStringBuilder(services.GetRequiredService<NpgsqlDataSource>().ConnectionString);

        pool.MaxPoolSize.Should().Be(DatabaseOptions.DefaultMaxPoolSize).And.Be(20);
        pool.Timeout.Should().Be(DatabaseOptions.DefaultPoolTimeoutSeconds).And.Be(5);
    }

    /// <summary>The settings win over pool sizes written into the connection string, so one place decides the budget.</summary>
    [Fact]
    public void Settings_override_the_connection_string()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString + ";Maximum Pool Size=100;Timeout=15",
            ["Database:MaxPoolSize"] = "40",
            ["Database:PoolTimeoutSeconds"] = "3",
        });

        var pool = new NpgsqlConnectionStringBuilder(services.GetRequiredService<NpgsqlDataSource>().ConnectionString);

        pool.MaxPoolSize.Should().Be(40);
        pool.Timeout.Should().Be(3);
    }

    /// <summary>A pool size or wait outside the sane range is refused with a clear message.</summary>
    /// <param name="key">The setting.</param>
    /// <param name="value">The bad value.</param>
    [Theory]
    [InlineData("MaxPoolSize", "0")]
    [InlineData("MaxPoolSize", "91")]
    [InlineData("PoolTimeoutSeconds", "0")]
    [InlineData("PoolTimeoutSeconds", "61")]
    public void Out_of_range_settings_are_refused(string key, string value)
    {
        using var services = BuildServices(new Dictionary<string, string?> { [$"Database:{key}"] = value });

        var read = () => services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage($"*Database__{key}*");
    }

    /// <summary>Builds the Infrastructure services over the given settings, with a connection string unless one is given.</summary>
    /// <param name="values">The settings.</param>
    /// <returns>The provider; the caller disposes it.</returns>
    private static ServiceProvider BuildServices(Dictionary<string, string?> values)
    {
        values.TryAdd("ConnectionStrings:DefaultConnection", ConnectionString);
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();
    }
}

/// <summary>
/// Against a real PostgreSQL (D-15): a full pool fails fast instead of hanging, and both "pool full" and "server
/// full" are recognised as a busy database, so the API can answer 503 instead of 500.
/// </summary>
public sealed class ConnectionPoolTests : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts PostgreSQL.</summary>
    /// <returns>A task that completes when the server is ready.</returns>
    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>With every pooled connection in use, the next open gives up after the wait and is a busy database.</summary>
    [Fact]
    public async Task Full_pool_fails_fast_as_busy()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Database:MaxPoolSize"] = "2",
                ["Database:PoolTimeoutSeconds"] = "1",
            }).Build())
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        await using var first = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var second = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var started = TimeProvider.System.GetTimestamp();

        var open = async () => await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var failure = (await open.Should().ThrowAsync<Exception>()).Which;
        TimeProvider.System.GetElapsedTime(started).Should().BeLessThan(TimeSpan.FromSeconds(5), "a full pool must not hang requests");
        DatabaseBusy.IsBusy(failure).Should().BeTrue();
    }

    /// <summary>PostgreSQL refusing a connection because it is full ("too many clients", class 53300) is a busy database.</summary>
    [Fact]
    public async Task Server_refusing_connections_is_busy()
    {
        await using (var owner = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await owner.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("CREATE ROLE limited LOGIN PASSWORD 'limited-password' CONNECTION LIMIT 1", owner);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var limited = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = "limited", Password = "limited-password", Pooling = false }.ConnectionString;
        await using var held = new NpgsqlConnection(limited);
        await held.OpenAsync(TestContext.Current.CancellationToken);
        await using var refused = new NpgsqlConnection(limited);

        var open = async () => await refused.OpenAsync(TestContext.Current.CancellationToken);

        var failure = (await open.Should().ThrowAsync<PostgresException>()).Which;
        failure.SqlState.Should().Be(PostgresErrorCodes.TooManyConnections);
        DatabaseBusy.IsBusy(failure).Should().BeTrue();
        DatabaseBusy.IsBusy(new InvalidOperationException("wrapped", failure)).Should().BeTrue("EF Core wraps provider errors");
    }

    /// <summary>Other database failures are not mistaken for a busy database.</summary>
    [Fact]
    public async Task Other_failures_are_not_busy()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT * FROM no_such_table", connection);

        var run = async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        DatabaseBusy.IsBusy((await run.Should().ThrowAsync<PostgresException>()).Which).Should().BeFalse();
        DatabaseBusy.IsBusy(new TimeoutException()).Should().BeFalse();
    }
}
