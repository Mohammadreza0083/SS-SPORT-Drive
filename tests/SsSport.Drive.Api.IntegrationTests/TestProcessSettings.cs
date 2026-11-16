using System.Runtime.CompilerServices;

namespace SsSport.Drive.Api.IntegrationTests;

/// <summary>Settings for every API host this test process starts.</summary>
internal static class TestProcessSettings
{
    /// <summary>
    /// Turns the in-API Hangfire server off (T-6c) before any host starts. The suite runs dozens of hosts in parallel;
    /// each would otherwise poll a database, and locally the API reads the developer's <c>.env</c>, so a test host could
    /// take a real notice off the developer's queue. <see cref="Telegram.BotNoticeQueueTests"/> turns it back on with
    /// its own database, and other tests run notices inline.
    /// </summary>
    [ModuleInitializer]
    internal static void TurnOffBackgroundJobServer() => Environment.SetEnvironmentVariable("BackgroundJobs__RunServer", "false");

    /// <summary>
    /// Leaves every real email provider unconfigured before any host starts (found 2026-11-16). Locally the API reads the
    /// developer's <c>.env</c>, which holds real credentials, and a host that did not swap the transports sent real email.
    /// A blank value is set rather than none, because <c>.env</c> never overrides a variable that exists, and a blank one
    /// counts as unconfigured. Tests that check sending use recording transports (<c>BackendTestHost</c>).
    /// </summary>
    [ModuleInitializer]
    internal static void TurnOffRealEmail()
    {
        foreach (var name in (string[])["Email__Resend__ApiKey", "Email__Brevo__ApiKey", "Email__GoogleSmtp__AppPassword", "Email__Smtp__Password"])
        {
            Environment.SetEnvironmentVariable(name, " ");
        }

        Environment.SetEnvironmentVariable("Email__GoogleSmtp__Host", "smtp.invalid");
    }

    /// <summary>
    /// Turns the bot notice quiet hours off (T-8), so tests that expect a notice right away pass at any time of day.
    /// <see cref="Telegram.BotNoticeRulesTests"/> sets the owner's quiet hours back and drives its own clock.
    /// </summary>
    [ModuleInitializer]
    internal static void TurnOffQuietHours()
    {
        Environment.SetEnvironmentVariable("Notifications__QuietHoursStart", "00:00");
        Environment.SetEnvironmentVariable("Notifications__QuietHoursEnd", "00:00");
    }
}
