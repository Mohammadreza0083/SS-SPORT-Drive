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
}
