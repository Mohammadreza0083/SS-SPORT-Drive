using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Domain.Notifications;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>Held bot notices go out together as one grouped message (T-8).</summary>
public sealed class NoticeDigestTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>No quiet hours, so every notice is queued as written.</summary>
    private static readonly NotificationRules _rules = NotificationRules.Create(TimeSpan.FromMinutes(10), new TimeOnly(0, 0), new TimeOnly(0, 0), TimeZoneInfo.Utc);

    /// <summary>A single notice is sent as written, with no header.</summary>
    [Fact]
    public void Single_notice_is_sent_as_is()
    {
        NoticeDigest.Render(Notices((null, "📁 one"))).Should().Equal("📁 one");
    }

    /// <summary>Several notices get a header with the count and are grouped by player, in the order they came.</summary>
    [Fact]
    public void Several_notices_are_grouped_by_player()
    {
        var arash = Guid.CreateVersion7();
        var sara = Guid.CreateVersion7();

        var messages = NoticeDigest.Render(Notices((arash, "a1"), (sara, "s1"), (arash, "a2")));

        messages.Should().ContainSingle();
        var lines = messages[0].Split('\n');
        lines[0].Should().Contain("3");
        lines.Skip(1).Where(line => line.Length > 0).Should().Equal("a1", "a2", "s1");
    }

    /// <summary>A digest longer than Telegram's 4096 characters is split, without cutting a line.</summary>
    [Fact]
    public void Long_digest_is_split()
    {
        var many = Enumerable.Range(0, 20).Select(index => ((Guid?)Guid.CreateVersion7(), $"{index:D2}" + new string('x', 900))).ToArray();

        var messages = NoticeDigest.Render(Notices(many));

        messages.Should().HaveCountGreaterThan(1);
        messages.Should().AllSatisfy(message => message.Length.Should().BeLessThanOrEqualTo(4096));
        string.Join('\n', messages).Split('\n').Count(line => line.EndsWith('x')).Should().Be(20, "every notice is sent once");
    }

    /// <summary>Queues notices in a box and takes them, as the sender does.</summary>
    /// <param name="notices">The players and texts.</param>
    /// <returns>The taken notices.</returns>
    private static IReadOnlyList<PendingNotice> Notices(params (Guid? PlayerId, string Text)[] notices)
    {
        var box = NoticeBox.Open(Guid.CreateVersion7(), 901, _now);
        foreach (var (playerId, text) in notices)
        {
            box.Queue(NoticeKind.FolderAdded, playerId, text, _now, _rules);
        }

        return box.TakeDue(_now);
    }
}
