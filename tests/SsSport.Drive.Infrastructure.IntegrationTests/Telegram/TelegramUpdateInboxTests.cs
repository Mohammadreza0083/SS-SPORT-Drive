using SsSport.Drive.Infrastructure.IntegrationTests.Persistence;
using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Telegram;

/// <summary>The <c>telegram_updates</c> inbox records each update id exactly once, even when Telegram delivers it again.</summary>
/// <param name="fixture">The migrated database.</param>
public sealed class TelegramUpdateInboxTests(AppDbContextFixture fixture) : IClassFixture<AppDbContextFixture>
{
    /// <summary>The first delivery is recorded; the second is reported as already seen.</summary>
    [Fact]
    public async Task Records_each_update_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = fixture.CreateContext();
        var inbox = new TelegramUpdateInbox(context);
        var now = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

        (await inbox.TryRecordAsync(900001, now, cancellationToken)).Should().BeTrue();
        (await inbox.TryRecordAsync(900001, now.AddSeconds(5), cancellationToken)).Should().BeFalse();
        (await inbox.TryRecordAsync(900002, now, cancellationToken)).Should().BeTrue();
    }
}
