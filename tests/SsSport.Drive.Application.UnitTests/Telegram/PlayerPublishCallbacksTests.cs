using System.Text;
using SsSport.Drive.Application.Telegram.Progress;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>
/// T-11 (owner, 2026-11-15): when an admin owns a complete waiting player, the progress message carries a
/// <b>Publish</b> button, so the admin does not have to open the console.
/// </summary>
public sealed class PlayerPublishCallbacksTests
{
    /// <summary>A player id.</summary>
    private static readonly Guid _playerId = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c");

    /// <summary>The keyboard is one Publish button; its data fits Telegram's 64-byte callback data.</summary>
    [Fact]
    public void The_keyboard_is_one_publish_button()
    {
        var keyboard = PlayerPublishCallbacks.Keyboard(_playerId);

        var button = keyboard.Rows.Should().ContainSingle().Which.Should().ContainSingle().Subject;
        button.Text.Should().Be("🚀 Publish");
        button.CallbackData.Should().Be("pub:019a3e1c5b2a7f10a4c38d9e0f1a2b3c");
        Encoding.UTF8.GetByteCount(button.CallbackData).Should().BeLessThanOrEqualTo(64);
    }

    /// <summary>The button reads back as its player.</summary>
    [Fact]
    public void The_button_reads_back_as_its_player()
    {
        var data = PlayerPublishCallbacks.Keyboard(_playerId).Rows.Single().Single().CallbackData;

        PlayerPublishCallbacks.TryParse(data, out var id).Should().BeTrue();
        id.Should().Be(_playerId);
    }

    /// <summary>Other buttons and broken ids are not Publish buttons.</summary>
    /// <param name="data">The callback data.</param>
    [Theory]
    [InlineData("areq:ok:019a3e1c5b2a7f10a4c38d9e0f1a2b3c")]
    [InlineData("pub:not-a-guid")]
    [InlineData("pub:")]
    [InlineData("publish:019a3e1c5b2a7f10a4c38d9e0f1a2b3c")]
    public void Other_data_is_not_a_publish_button(string data)
    {
        PlayerPublishCallbacks.TryParse(data, out _).Should().BeFalse();
    }
}
