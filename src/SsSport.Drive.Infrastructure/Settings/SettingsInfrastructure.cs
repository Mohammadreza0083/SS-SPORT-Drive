using System.Globalization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Settings;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Settings;
using SsSport.Drive.Infrastructure.BackgroundJobs;
using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Infrastructure.Settings;

/// <summary>
/// The <c>Settings</c> configuration section (<c>Settings__*</c>): the first values of the console's settings on a
/// fresh database (A-10). The quiet hours come from <c>Notifications__QuietHoursStart/End</c>. Once an admin saves the
/// settings, the database row wins.
/// </summary>
public sealed record SettingsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Settings";

    /// <summary>Gets the email shown on every player page.</summary>
    public string ContactEmail { get; init; } = "info@ss-sport.co";

    /// <summary>Gets the daily digest time as <c>HH:mm</c> in Tehran.</summary>
    public string DigestAt { get; init; } = "09:00";

    /// <summary>Gets the default share-link expiry in days.</summary>
    public int DefaultLinkExpiryDays { get; init; } = 30;

    /// <summary>Checks the values, for start-up validation.</summary>
    /// <returns><see langword="true"/> when they make valid settings.</returns>
    public bool IsValid()
    {
        try
        {
            _ = ToSettings(new NotificationOptions(), DateTimeOffset.UnixEpoch);
            return true;
        }
        catch (Exception exception) when (exception is DomainException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Builds the first settings from configuration.</summary>
    /// <param name="notifications">The notification section, for the quiet hours.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>The settings.</returns>
    /// <exception cref="FormatException">A time is not <c>HH:mm</c>.</exception>
    /// <exception cref="DomainException">A value breaks a settings rule.</exception>
    public AppSettings ToSettings(NotificationOptions notifications, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        return AppSettings.CreateDefault(
            EmailAddress.Create(ContactEmail),
            TimeOnly.ParseExact(DigestAt, "HH:mm", CultureInfo.InvariantCulture),
            TimeOnly.ParseExact(notifications.QuietHoursStart, "HH:mm", CultureInfo.InvariantCulture),
            TimeOnly.ParseExact(notifications.QuietHoursEnd, "HH:mm", CultureInfo.InvariantCulture),
            DefaultLinkExpiryDays,
            now);
    }
}

/// <summary>Reads and stores the single settings row; the configured first values stand in until it is saved.</summary>
/// <param name="context">The database.</param>
/// <param name="settings">The first values.</param>
/// <param name="notifications">The notification section, for the quiet hours.</param>
/// <param name="timeProvider">The clock.</param>
internal sealed class AppSettingsStore(
    DbContext context,
    IOptions<SettingsOptions> settings,
    IOptions<NotificationOptions> notifications,
    TimeProvider timeProvider) : IAppSettingsStore
{
    /// <inheritdoc />
    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default) =>
        await context.Set<AppSettings>().AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? Defaults();

    /// <inheritdoc />
    public AppSettings GetSnapshot() => context.Set<AppSettings>().AsNoTracking().FirstOrDefault() ?? Defaults();

    /// <inheritdoc />
    public async Task<AppSettings> GetForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.Set<AppSettings>().FirstOrDefaultAsync(cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = Defaults();
        context.Set<AppSettings>().Add(row);
        return row;
    }

    /// <summary>Builds the configured first values.</summary>
    /// <returns>The settings.</returns>
    private AppSettings Defaults() => settings.Value.ToSettings(notifications.Value, timeProvider.GetUtcNow());
}

/// <summary>
/// The bot's notification rules with the quiet hours from the stored settings (A-10): throttle and time zone still come
/// from configuration. The rules are cached for a minute, and a change drops the cache at once on this host; another host
/// (the workers) sees it within the minute.
/// </summary>
/// <param name="scopes">Opens a scope to read the settings.</param>
/// <param name="options">The notification section.</param>
/// <param name="timeProvider">The clock.</param>
internal sealed class StoredNotificationSettings(IServiceScopeFactory scopes, IOptions<NotificationOptions> options, TimeProvider timeProvider) : INotificationSettings, IAppSettingsCache
{
    /// <summary>How long the rules are reused.</summary>
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(1);

    /// <summary>Guards the cached rules.</summary>
    private readonly object _gate = new();

    /// <summary>The cached rules, or <see langword="null"/>.</summary>
    private NotificationRules? _rules;

    /// <summary>When the cached rules stop being used.</summary>
    private DateTimeOffset _expiresAt;

    /// <inheritdoc />
    public NotificationRules Rules
    {
        get
        {
            lock (_gate)
            {
                var now = timeProvider.GetUtcNow();
                if (_rules is null || now >= _expiresAt)
                {
                    using var scope = scopes.CreateScope();
                    var stored = scope.ServiceProvider.GetRequiredService<IAppSettingsStore>().GetSnapshot();
                    _rules = options.Value.ToRules(stored.QuietHoursStart, stored.QuietHoursEnd);
                    _expiresAt = now + _lifetime;
                }

                return _rules;
            }
        }
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        lock (_gate)
        {
            _rules = null;
        }
    }
}

/// <summary>Moves the recurring daily digest job to the settings' time.</summary>
/// <param name="storage">Hangfire's storage.</param>
internal sealed class HangfireDigestSchedule(JobStorage storage) : IDigestSchedule
{
    /// <inheritdoc />
    public void Apply(TimeOnly digestAt) =>
        new RecurringJobManager(storage).AddOrUpdate<DailyDigestJob>(
            DailyDigestJob.RecurringJobId,
            BotNoticeJob.Queue,
            job => job.RunAsync(CancellationToken.None),
            HangfireStorage.DigestCron(digestAt));
}
