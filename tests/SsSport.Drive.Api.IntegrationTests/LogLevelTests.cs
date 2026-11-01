using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>
/// SQL is not logged command by command (D-14): EF Core logs at Warning in the base settings of the API and the
/// workers, so failed commands are still Errors, and slow commands are Warnings from the slow-query log.
/// </summary>
/// <param name="factory">Boots the API with its real settings files.</param>
public sealed class LogLevelTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>EF Core's command category logs failures but not every command.</summary>
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    /// <summary>The API logs failed SQL commands and slow ones, but not every command.</summary>
    [Fact]
    public void Api_logs_failed_and_slow_sql_but_not_every_command()
    {
        var loggers = factory.Services.GetRequiredService<ILoggerFactory>();
        var commands = loggers.CreateLogger(CommandCategory);
        var slow = loggers.CreateLogger("SsSport.Drive.Infrastructure.Persistence.SlowQueryInterceptor");

        commands.IsEnabled(LogLevel.Information).Should().BeFalse("every SQL command was logged before D-14");
        commands.IsEnabled(LogLevel.Error).Should().BeTrue("failed commands are still logged");
        slow.IsEnabled(LogLevel.Warning).Should().BeTrue("slow commands are logged instead");
    }

    /// <summary>Both hosts' base settings keep EF Core at Warning, so every environment gets it.</summary>
    /// <param name="host">The host project.</param>
    [Theory]
    [InlineData("SsSport.Drive.Api")]
    [InlineData("SsSport.Drive.Workers")]
    public void Base_settings_keep_ef_core_at_warning(string host)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", host, "appsettings.json")));

        settings.RootElement.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Microsoft.EntityFrameworkCore").GetString()
            .Should().Be("Warning");
    }

    /// <summary>Finds the repository root (the folder with the solution file) above the test output.</summary>
    /// <returns>The root folder.</returns>
    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "SsSport.Drive.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new DirectoryNotFoundException("The repository root (SsSport.Drive.slnx) was not found.");
    }
}
