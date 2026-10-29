using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Notifications;

/// <summary>The progress message remembers which Telegram message to edit and what it shows (T-6a).</summary>
public sealed class ProgressMessageTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The first message keeps its chat, id and text.</summary>
    [Fact]
    public void Starts_with_the_sent_message()
    {
        var playerId = Guid.CreateVersion7(_now);

        var message = ProgressMessage.Start(playerId, 901, 1001, "⏳ Arash", _now);

        message.PlayerId.Should().Be(playerId);
        message.ChatId.Should().Be(901);
        message.MessageId.Should().Be(1001);
        message.Shows("⏳ Arash").Should().BeTrue();
        message.UpdatedAt.Should().Be(_now);
    }

    /// <summary>An edit changes the text; a resend also changes the message to edit.</summary>
    [Fact]
    public void Tracks_edits_and_resends()
    {
        var message = ProgressMessage.Start(Guid.CreateVersion7(_now), 901, 1001, "one", _now);

        message.Edited("two", _now.AddMinutes(1));
        message.Shows("two").Should().BeTrue();
        message.MessageId.Should().Be(1001);

        message.Resent(1002, "three", _now.AddMinutes(2));
        message.MessageId.Should().Be(1002);
        message.Shows("three").Should().BeTrue();
        message.UpdatedAt.Should().Be(_now.AddMinutes(2));
    }

    /// <summary>Telegram refuses empty texts and texts over 4096 characters.</summary>
    /// <param name="length">The text length (0 for empty).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Text_must_fit_telegram(int length)
    {
        var start = () => ProgressMessage.Start(Guid.CreateVersion7(_now), 901, 1001, new string('a', length), _now);

        start.Should().Throw<DomainException>().Which.Code.Should().Be("progress.text_invalid");
    }
}
