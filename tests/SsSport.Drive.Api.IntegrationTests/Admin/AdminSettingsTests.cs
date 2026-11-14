using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Application.Settings;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Domain.Audit;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// Settings (A-10, canvas <c>AdminSettings</c>; owner 2026-11-14: one row in the database, configuration only gives the
/// first values). An admin edits the contact email, the daily digest time, the quiet hours and the default link expiry.
/// A change takes effect without a restart: the bot's quiet hours, the digest's schedule and the contact email on the
/// player page. Every change is audited with the names of the changed fields only (no email in audit data).
/// </summary>
public sealed class AdminSettingsTests : IAsyncLifetime
{
    /// <summary>Records every digest schedule the API applies.</summary>
    private readonly RecordingDigestSchedule _schedule = new();

    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host with configured first values and the recording digest schedule.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync(
        new Dictionary<string, string?>
        {
            ["Notifications:QuietHoursStart"] = "22:30",
            ["Notifications:QuietHoursEnd"] = "07:30",
        },
        services => services.Replace(ServiceDescriptor.Singleton<IDigestSchedule>(_schedule)));

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>On a fresh database the settings are configuration's values and the documented defaults.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_fresh_database_shows_the_configured_values()
    {
        using var body = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/settings");

        var root = body.RootElement;
        root.GetProperty("contactEmail").GetString().Should().Be("info@ss-sport.co");
        root.GetProperty("digestAt").GetString().Should().Be("09:00");
        root.GetProperty("quietHoursStart").GetString().Should().Be("22:30");
        root.GetProperty("quietHoursEnd").GetString().Should().Be("07:30");
        root.GetProperty("defaultLinkExpiryDays").GetInt32().Should().Be(30);
        root.GetProperty("linkExpiryChoices").EnumerateArray().Select(item => item.GetInt32()).Should().Equal(7, 30, 90);
        root.GetProperty("timeZone").GetString().Should().Be("Asia/Tehran");
    }

    /// <summary>Saving stores the values, answers them back and audits the changed field names.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Saving_stores_and_audits_the_change()
    {
        using (var saved = await _host.AdminJsonAsync(HttpMethod.Put, "/admin/settings", Form(contactEmail: "contact@ss-sport.co", defaultLinkExpiryDays: 90)))
        {
            saved.RootElement.GetProperty("contactEmail").GetString().Should().Be("contact@ss-sport.co");
            saved.RootElement.GetProperty("defaultLinkExpiryDays").GetInt32().Should().Be(90);
        }

        using (var read = await _host.AdminJsonAsync(HttpMethod.Get, "/admin/settings"))
        {
            read.RootElement.GetProperty("contactEmail").GetString().Should().Be("contact@ss-sport.co");
        }

        var entry = await _host.QueryAsync(db => Task.FromResult(db.Set<AuditEntry>().Single(item => item.Action == "settings.changed")));
        entry.ActorUserId.Should().Be(_host.AdminId);
        entry.Data.Should().Contain("contactEmail").And.Contain("defaultLinkExpiryDays").And.NotContain("@");
    }

    /// <summary>Bad values are a 400 per field: an email, HH:mm times and an expiry of 7, 30 or 90 days.</summary>
    /// <param name="field">The field to break.</param>
    /// <param name="value">The bad value.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("contactEmail", "not-an-email")]
    [InlineData("digestAt", "25:00")]
    [InlineData("quietHoursStart", "9am")]
    [InlineData("quietHoursEnd", "")]
    [InlineData("defaultLinkExpiryDays", "0")]
    [InlineData("defaultLinkExpiryDays", "365")]
    public async Task Bad_values_are_refused(string field, string value)
    {
        var form = new Dictionary<string, object?>
        {
            ["contactEmail"] = "info@ss-sport.co",
            ["digestAt"] = "09:00",
            ["quietHoursStart"] = "23:00",
            ["quietHoursEnd"] = "08:00",
            ["defaultLinkExpiryDays"] = 30,
        };
        form[field] = field == "defaultLinkExpiryDays" ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : value;

        using var response = await _host.SendAsync(HttpMethod.Put, "/admin/settings", form, telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        body.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(body.RootElement.GetRawText());
    }

    /// <summary>New quiet hours reach the bot's notice rules at once, without a restart.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task New_quiet_hours_reach_the_bot()
    {
        using (await _host.AdminJsonAsync(HttpMethod.Put, "/admin/settings", Form(quietHoursStart: "21:00", quietHoursEnd: "06:00")))
        {
        }

        using var scope = _host.Api.Services.CreateScope();
        var rules = scope.ServiceProvider.GetRequiredService<INotificationSettings>().Rules;
        rules.QuietStart.Should().Be(new TimeOnly(21, 0));
        rules.QuietEnd.Should().Be(new TimeOnly(6, 0));
    }

    /// <summary>A new digest time reschedules the digest job; saving without changing it does not.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_new_digest_time_reschedules_the_digest()
    {
        _schedule.Applied.Clear();

        using (await _host.AdminJsonAsync(HttpMethod.Put, "/admin/settings", Form(digestAt: "10:15")))
        {
        }

        _schedule.Applied.Should().Equal(new TimeOnly(10, 15));

        using (await _host.AdminJsonAsync(HttpMethod.Put, "/admin/settings", Form(digestAt: "10:15", contactEmail: "contact@ss-sport.co")))
        {
        }

        _schedule.Applied.Should().HaveCount(1);
    }

    /// <summary>The contact email on the viewer's page is the one from settings.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_player_page_shows_the_contact_email_from_settings()
    {
        var code = await _host.CreateLinkAsync(await _host.AddPlayerAsync());
        var session = await _host.StartViewerSessionAsync(code);
        using (await _host.AdminJsonAsync(HttpMethod.Put, "/admin/settings", Form(contactEmail: "contact@ss-sport.co")))
        {
        }

        using var response = await _host.SendAsync(HttpMethod.Get, $"/links/{code}/content", headers: new Dictionary<string, string> { [BackendTestHost.ViewerSessionHeader] = session });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await BackendTestHost.ReadJsonAsync(response);
        body.RootElement.GetProperty("contactEmail").GetString().Should().Be("contact@ss-sport.co");
    }

    /// <summary>Only admins read or change settings.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_change_settings()
    {
        using var read = await _host.SendAsync(HttpMethod.Get, "/admin/settings", telegramId: BackendTestHost.AnalystTelegramId);
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var write = await _host.SendAsync(HttpMethod.Put, "/admin/settings", Form(), telegramId: BackendTestHost.AnalystTelegramId);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = await _host.SendAsync(HttpMethod.Put, "/admin/settings", Form());
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A full settings form with the defaults, changed where asked.</summary>
    /// <param name="contactEmail">The contact email.</param>
    /// <param name="digestAt">The digest time.</param>
    /// <param name="quietHoursStart">The quiet hours start.</param>
    /// <param name="quietHoursEnd">The quiet hours end.</param>
    /// <param name="defaultLinkExpiryDays">The default link expiry.</param>
    /// <returns>The form.</returns>
    private static object Form(
        string contactEmail = "info@ss-sport.co",
        string digestAt = "09:00",
        string quietHoursStart = "22:30",
        string quietHoursEnd = "07:30",
        int defaultLinkExpiryDays = 30) =>
        new { contactEmail, digestAt, quietHoursStart, quietHoursEnd, defaultLinkExpiryDays };

    /// <summary>Records the digest times the API applies instead of touching Hangfire.</summary>
    private sealed class RecordingDigestSchedule : IDigestSchedule
    {
        /// <summary>Gets the applied times, in order.</summary>
        public ConcurrentQueue<TimeOnly> Applied { get; } = new();

        /// <summary>Records the time.</summary>
        /// <param name="digestAt">The Tehran wall-clock time of the digest.</param>
        public void Apply(TimeOnly digestAt) => Applied.Enqueue(digestAt);
    }
}
