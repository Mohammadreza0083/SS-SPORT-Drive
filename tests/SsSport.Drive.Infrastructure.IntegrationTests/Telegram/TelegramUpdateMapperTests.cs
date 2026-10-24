using System.Text.Json;
using global::Telegram.Bot;
using global::Telegram.Bot.Types;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Telegram;

/// <summary>
/// The mapper (shared by the webhook and polling) turns a Telegram update into the application command: ids, the
/// sender's profile, a bot command name or our own button data — never free message text.
/// </summary>
public sealed class TelegramUpdateMapperTests
{
    /// <summary>A /start message carries the command name and the sender's profile, not the text.</summary>
    [Fact]
    public void Maps_a_command_message()
    {
        var command = TelegramUpdateMapper.ToCommand(Parse("""
            {"update_id":7,"message":{"message_id":3,"date":1,"chat":{"id":42,"type":"private"},
             "from":{"id":42,"is_bot":false,"first_name":"Reza","last_name":"M","username":"reza"},"text":"/start@sssportdrive_bot hello"}}
            """));

        command.UpdateId.Should().Be(7);
        command.Kind.Should().Be("Message");
        command.TelegramUserId.Should().Be(42);
        command.Sender.Should().Be(new TelegramSender(42, 42, "reza", "Reza M"));
        command.BotCommand.Should().Be("start");
        command.CallbackData.Should().BeNull();
    }

    /// <summary>Plain text is not carried at all.</summary>
    [Fact]
    public void Free_text_is_dropped()
    {
        var command = TelegramUpdateMapper.ToCommand(Parse("""
            {"update_id":8,"message":{"message_id":3,"date":1,"chat":{"id":42,"type":"private"},
             "from":{"id":42,"is_bot":false,"first_name":"Reza"},"text":"my phone is 0912…"}}
            """));

        command.BotCommand.Should().BeNull();
        command.Sender!.DisplayName.Should().Be("Reza");
    }

    /// <summary>A button press carries our callback data, the callback id and the message it is on.</summary>
    [Fact]
    public void Maps_a_button_press()
    {
        var command = TelegramUpdateMapper.ToCommand(Parse("""
            {"update_id":9,"callback_query":{"id":"cb1","chat_instance":"1","data":"access:request",
             "from":{"id":42,"is_bot":false,"first_name":"Reza"},
             "message":{"message_id":55,"date":1,"chat":{"id":42,"type":"private"},"text":"x"}}}
            """));

        command.Kind.Should().Be("CallbackQuery");
        command.CallbackData.Should().Be("access:request");
        command.CallbackQueryId.Should().Be("cb1");
        command.CallbackMessageId.Should().Be(55);
        command.Sender!.ChatId.Should().Be(42);
    }

    /// <summary>Parses an update the way the webhook does.</summary>
    /// <param name="json">The update JSON.</param>
    /// <returns>The update.</returns>
    private static Update Parse(string json) => JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)!;
}
