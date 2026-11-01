using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// SQL is no longer logged command by command (D-14), so slow commands are logged instead: a command that takes at
/// least the threshold is a Warning with its duration and SQL text, never its parameter values.
/// </summary>
/// <param name="database">The migrated PostgreSQL database.</param>
public sealed class SlowQueryInterceptorTests(AppDbContextFixture database) : IClassFixture<AppDbContextFixture>
{
    /// <summary>The threshold these tests use; the sleeps are well above and the fast queries well below it.</summary>
    private static readonly TimeSpan _threshold = TimeSpan.FromMilliseconds(200);

    /// <summary>Records what the interceptor logs.</summary>
    private readonly FakeLogger<SlowQueryInterceptor> _logger = new();

    /// <summary>A slow command and a slow query are both logged as Warnings with their duration and SQL.</summary>
    [Fact]
    public async Task Slow_commands_are_logged_as_warnings()
    {
        await using var context = database.CreateContext(new SlowQueryInterceptor(_logger, Options.Create(new DatabaseOptions { SlowQueryThresholdMs = 200 })));

        await context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.3)", TestContext.Current.CancellationToken);
        await context.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM pg_sleep(0.3)").ToListAsync(TestContext.Current.CancellationToken);

        var slow = _logger.Collector.GetSnapshot();
        slow.Should().HaveCount(2).And.AllSatisfy(record =>
        {
            record.Level.Should().Be(LogLevel.Warning);
            record.Message.Should().Contain("pg_sleep");
            record.StructuredState!.Should().Contain(pair => pair.Key == "DurationMs" && double.Parse(pair.Value!, System.Globalization.CultureInfo.InvariantCulture) >= _threshold.TotalMilliseconds);
        });
    }

    /// <summary>A fast command logs nothing.</summary>
    [Fact]
    public async Task Fast_commands_log_nothing()
    {
        await using var context = database.CreateContext(new SlowQueryInterceptor(_logger, Options.Create(new DatabaseOptions { SlowQueryThresholdMs = 200 })));

        await context.Database.ExecuteSqlRawAsync("SELECT 1", TestContext.Current.CancellationToken);

        _logger.Collector.Count.Should().Be(0);
    }

    /// <summary>The SQL text is logged with its parameter placeholders; the values never are.</summary>
    [Fact]
    public async Task Parameter_values_are_never_logged()
    {
        await using var context = database.CreateContext(new SlowQueryInterceptor(_logger, Options.Create(new DatabaseOptions { SlowQueryThresholdMs = 200 })));

        await context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.3) WHERE {0} <> ''", ["secret-otp-123456"], TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().Should().ContainSingle().Which;
        record.Message.Should().Contain("@p0").And.NotContain("secret-otp-123456");
    }

    /// <summary>The application's own context logs slow commands: the interceptor is wired in, not just defined.</summary>
    [Fact]
    public void App_context_uses_the_slow_query_log()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=none" })
                .Build())
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();

        options.FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>()!.Interceptors
            .Should().ContainSingle(interceptor => interceptor is SlowQueryInterceptor);
    }

    /// <summary>The threshold is 500 ms unless <c>Database__SlowQueryThresholdMs</c> sets another.</summary>
    /// <param name="setting">The configured value, or <see langword="null"/>.</param>
    /// <param name="expected">The threshold in milliseconds.</param>
    [Theory]
    [InlineData(null, 500)]
    [InlineData("1500", 1500)]
    public void Threshold_comes_from_configuration(string? setting, int expected)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=none" };
        if (setting is not null)
        {
            values["Database:SlowQueryThresholdMs"] = setting;
        }

        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();

        services.GetRequiredService<IOptions<DatabaseOptions>>().Value.SlowQueryThresholdMs.Should().Be(expected);
    }
}
