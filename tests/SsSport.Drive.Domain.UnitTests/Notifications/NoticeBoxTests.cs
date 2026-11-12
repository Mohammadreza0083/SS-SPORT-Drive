using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Notifications;

/// <summary>
/// A recipient's notice box (T-8) holds the bot notices that may not go out yet and knows when the next message is due.
/// </summary>
public sealed class NoticeBoxTests
{
    /// <summary>A fixed time, outside the quiet hours.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>10 minutes, quiet 23:00 to 08:00 UTC (UTC keeps the arithmetic readable).</summary>
    private static readonly NotificationRules _rules = NotificationRules.Create(TimeSpan.FromMinutes(10), new TimeOnly(23, 0), new TimeOnly(8, 0), TimeZoneInfo.Utc);

    /// <summary>The first notice is due at once.</summary>
    [Fact]
    public void First_notice_is_due_now()
    {
        var box = NoticeBox.Open(Guid.CreateVersion7(), 901, _now);

        box.Queue(NoticeKind.FolderAdded, Guid.CreateVersion7(), "📁 folder", _now, _rules);

        box.DueAt.Should().Be(_now);
        box.IsDue(_now).Should().BeTrue();
        box.Notices.Should().ContainSingle().Which.Text.Should().Be("📁 folder");
    }

    /// <summary>Taking the due notices empties the box and starts the 10 minutes.</summary>
    [Fact]
    public void Taking_starts_the_throttle()
    {
        var box = NoticeBox.Open(Guid.CreateVersion7(), 901, _now);
        box.Queue(NoticeKind.FolderAdded, null, "one", _now, _rules);

        var taken = box.TakeDue(_now);

        taken.Should().ContainSingle().Which.Text.Should().Be("one");
        box.Notices.Should().BeEmpty();
        box.DueAt.Should().BeNull();
        box.LastSentAt.Should().Be(_now);

        box.Queue(NoticeKind.FolderAdded, null, "two", _now.AddMinutes(2), _rules);
        box.DueAt.Should().Be(_now.AddMinutes(10));
        box.IsDue(_now.AddMinutes(9)).Should().BeFalse();
        box.TakeDue(_now.AddMinutes(9)).Should().BeEmpty("the throttle is still running");
        box.IsDue(_now.AddMinutes(10)).Should().BeTrue();
    }

    /// <summary>Notices that arrive while one is waiting join it and keep its time.</summary>
    [Fact]
    public void Later_notices_join_the_waiting_ones()
    {
        var box = NoticeBox.Open(Guid.CreateVersion7(), 901, _now);
        box.Queue(NoticeKind.FolderAdded, null, "one", _now, _rules);
        box.TakeDue(_now);

        box.Queue(NoticeKind.FolderAdded, null, "two", _now.AddMinutes(1), _rules);
        box.Queue(NoticeKind.PlayerPublished, null, "three", _now.AddMinutes(4), _rules);

        box.DueAt.Should().Be(_now.AddMinutes(10));
        box.TakeDue(_now.AddMinutes(10)).Select(notice => notice.Text).Should().Equal("two", "three");
    }

    /// <summary>A failed send moves the time on and keeps the notices.</summary>
    [Fact]
    public void Postpone_keeps_the_notices()
    {
        var box = NoticeBox.Open(Guid.CreateVersion7(), 901, _now);
        box.Queue(NoticeKind.FolderAdded, null, "one", _now, _rules);

        box.Postpone(_now.AddMinutes(10));

        box.IsDue(_now).Should().BeFalse();
        box.DueAt.Should().Be(_now.AddMinutes(10));
        box.Notices.Should().ContainSingle();
    }

    /// <summary>Discarding drops the notices of a recipient who left the team.</summary>
    [Fact]
    public void Discard_drops_everything()
    {
        var box = NoticeBox.Open(Guid.CreateVersion7(), 901, _now);
        box.Queue(NoticeKind.FolderAdded, null, "one", _now, _rules);

        box.Discard();

        box.Notices.Should().BeEmpty();
        box.DueAt.Should().BeNull();
        box.IsDue(_now).Should().BeFalse();
    }

    /// <summary>A notice line needs 1 to 1000 characters.</summary>
    /// <param name="length">The text length (0 for empty).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Text_must_fit(int length)
    {
        var box = NoticeBox.Open(Guid.CreateVersion7(), 901, _now);

        var queue = () => box.Queue(NoticeKind.FolderAdded, null, new string('a', length), _now, _rules);

        queue.Should().Throw<DomainException>().Which.Code.Should().Be("notice.text_invalid");
    }
}
