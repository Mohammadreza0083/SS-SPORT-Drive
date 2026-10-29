using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using SsSport.Drive.Infrastructure.Telegram;
using Telegram.Bot;

namespace SsSport.Drive.Infrastructure;

/// <summary>
/// Registers the Infrastructure layer's services with the dependency-injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Name of the PostgreSQL connection string (<c>ConnectionStrings__DefaultConnection</c> in .env).</summary>
    public const string ConnectionStringName = "DefaultConnection";

    /// <summary>
    /// Adds the shared PostgreSQL data source, the EF Core context, the persistence ports
    /// (unit of work + generic repository, one per request scope) and the Telegram adapters. The connection string is
    /// read lazily from configuration, so hosts and tests can supply it from any configuration source.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(provider => NpgsqlDataSource.Create(ReadConnectionString(provider.GetRequiredService<IConfiguration>())));
        services.AddSingleton<PostgreSqlHealthCheck>();

        services.AddDbContext<AppDbContext>((provider, options) => options.UseAppDatabase(provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<,>), typeof(Repository<,>));
        services.AddScoped<IPagedReader, PagedReader>();

        services.AddTelegram();
        return services;
    }

    /// <summary>
    /// Binds and checks the <c>Telegram</c> settings at start-up, and registers the update inbox and a Bot API client
    /// (through <c>Telegram__Proxy</c> when set). The client is created on first use, so hosts without a bot token start.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    private static void AddTelegram(this IServiceCollection services)
    {
        services.AddOptions<TelegramOptions>()
            .BindConfiguration(TelegramOptions.SectionName)
            .Validate(options => TelegramOptions.IsValidSecret(options.WebhookSecret), "Telegram__WebhookSecret must be 1-256 characters of A-Z, a-z, 0-9, _ or -.")
            .Validate(options => options.Proxy.Length == 0 || Uri.TryCreate(options.Proxy, UriKind.Absolute, out _), "Telegram__Proxy must be an absolute URL, e.g. http://127.0.0.1:10808.")
            .Validate(options => TelegramOptions.IsValidMiniAppUrl(options.MiniAppUrl), "Telegram__MiniAppUrl must be empty or an absolute HTTPS URL.")
            .Validate(options => options.IsPolling || string.Equals(options.Mode, TelegramOptions.WebhookMode, StringComparison.OrdinalIgnoreCase), "Telegram__Mode must be Webhook or Polling.")
            .ValidateOnStart();

        services.AddScoped<ITelegramUpdateInbox, TelegramUpdateInbox>();
        services.AddSingleton<IBotAdminSettings, TelegramBotAdminSettings>();
        services.AddSingleton<ITelegramMessenger, TelegramMessenger>();
        services.AddSingleton<ITelegramBotClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TelegramOptions>>().Value;
            var handler = new SocketsHttpHandler();
            if (options.Proxy.Length > 0)
            {
                handler.Proxy = new WebProxy(new Uri(options.Proxy));
                handler.UseProxy = true;
            }

            return new TelegramBotClient(options.BotToken, new HttpClient(handler));
        });
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
