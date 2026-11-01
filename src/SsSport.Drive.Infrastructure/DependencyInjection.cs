using System.Net;
using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Images;
using SsSport.Drive.Application.Abstractions.Media;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Infrastructure.BackgroundJobs;
using SsSport.Drive.Infrastructure.Drive;
using SsSport.Drive.Infrastructure.Edge;
using SsSport.Drive.Infrastructure.Images;
using SsSport.Drive.Infrastructure.Persistence;
using SsSport.Drive.Infrastructure.Storage;
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
    /// (unit of work + generic repository, one per request scope), the media bucket, the photo processor and the
    /// Telegram adapters and the HLS edge-token issuer. The connection string is
    /// read lazily from configuration, so hosts and tests can supply it from any configuration source.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(provider => NpgsqlDataSource.Create(ReadConnectionString(provider.GetRequiredService<IConfiguration>())));
        services.AddSingleton<PostgreSqlHealthCheck>();

        // SQL is not logged command by command (D-14); slow commands are logged instead. Logging is added if the host
        // has not (a bare container); hosts already have it, and AddLogging only adds what is missing.
        services.AddLogging();
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .Validate(options => options.SlowQueryThresholdMs > 0, "Database__SlowQueryThresholdMs must be a positive number of milliseconds.")
            .ValidateOnStart();
        services.AddSingleton<SlowQueryInterceptor>();
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseAppDatabase(provider.GetRequiredService<NpgsqlDataSource>())
            .AddInterceptors(provider.GetRequiredService<SlowQueryInterceptor>()));
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<,>), typeof(Repository<,>));
        services.AddScoped<IPagedReader, PagedReader>();
        services.AddScoped<IPlayerListReader, PlayerListReader>();

        // Background jobs (T-6c): Hangfire on PostgreSQL, built lazily from the same data source, so a host without a
        // database still starts. The schema comes from `migrate`, never from start-up. Everything Hangfire needs is
        // registered per container; AddHangfire is not used, because it points Hangfire's process-wide statics
        // (JobStorage.Current, JobActivator.Current, the log provider) at the last host built. With several hosts in
        // one process (the test suite) that let one host's jobs land in another's database, run in another's
        // services, or log through a disposed logger factory.
        HangfireStorage.UseSerializerSettings();
        services.AddSingleton<JobStorage>(provider =>
            new PostgreSqlStorage(new DataSourceConnectionFactory(provider.GetRequiredService<NpgsqlDataSource>()), HangfireStorage.Options()));
        services.AddSingleton<JobActivator>(provider => new AspNetCoreJobActivator(provider.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<IBackgroundJobClient>(provider => new BackgroundJobClient(provider.GetRequiredService<JobStorage>()));
        services.AddSingleton<IBackgroundEventQueue, HangfireBackgroundEventQueue>();

        services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName);
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddSingleton<IPhotoProcessor, MagickPhotoProcessor>();

        services.AddOptions<GoogleDriveOptions>()
            .BindConfiguration(GoogleDriveOptions.SectionName)
            .Validate(options => options.Proxy.Length == 0 || Uri.TryCreate(options.Proxy, UriKind.Absolute, out _), "GoogleDrive__Proxy must be an absolute URL, e.g. http://127.0.0.1:10808.")
            .ValidateOnStart();
        services.AddSingleton<IDriveApi, GoogleDriveApi>();
        services.AddSingleton<IDriveFolderReader, GoogleDriveFolderReader>();

        services.AddOptions<EdgeOptions>()
            .BindConfiguration(EdgeOptions.SectionName)
            .Validate(options => options.HlsSigningKey.Length == 0 || EdgeOptions.IsValidSigningKey(options.HlsSigningKey), "Edge__HlsSigningKey must be base64 of at least 32 random bytes (or empty to turn HLS tokens off).")
            .ValidateOnStart();
        services.AddSingleton<IHlsTokenIssuer, HmacHlsTokenIssuer>();

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
        services.AddSingleton<IMiniAppLinks, TelegramMiniAppLinks>();
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

    /// <summary>
    /// Runs the Hangfire server that sends bot notices (T-6c) inside this host. The API calls it (owner, 2026-10-31);
    /// it serves only the notices queue and does not start without a database.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddBotNoticeServer(this IServiceCollection services)
    {
        services.AddHostedService<BotNoticeServer>();
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
