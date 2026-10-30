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

    /// <summary>A photo keeps its largest size, the message id and what it replies to (T-4d).</summary>
    [Fact]
    public void Maps_a_photo_reply()
    {
        var command = TelegramUpdateMapper.ToCommand(Parse("""
            {"update_id":10,"message":{"message_id":61,"date":1,"chat":{"id":42,"type":"private"},
             "from":{"id":42,"is_bot":false,"first_name":"Reza"},
             "photo":[{"file_id":"small","file_unique_id":"s","width":90,"height":120,"file_size":1500},
                      {"file_id":"big","file_unique_id":"b","width":960,"height":1280,"file_size":180000},
                      {"file_id":"mid","file_unique_id":"m","width":480,"height":640,"file_size":40000}],
             "reply_to_message":{"message_id":1001,"date":1,"chat":{"id":42,"type":"private"},"text":"⏳ Arash"}}}
            """));

        command.Photo.Should().Be(new IncomingPhoto("big", 180000, IsFile: false));
        command.MessageId.Should().Be(61);
        command.ReplyToMessageId.Should().Be(1001);
        command.BotCommand.Should().BeNull();
    }

    /// <summary>An image sent as a File is a photo too (full quality); other files are not.</summary>
    /// <param name="mimeType">The document type.</param>
    /// <param name="isPhoto">Whether it counts as a photo.</param>
    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/heic", true)]
    [InlineData("application/pdf", false)]
    public void Maps_an_image_file(string mimeType, bool isPhoto)
    {
        var command = TelegramUpdateMapper.ToCommand(Parse("""
            {"update_id":11,"message":{"message_id":62,"date":1,"chat":{"id":42,"type":"private"},
             "from":{"id":42,"is_bot":false,"first_name":"Reza"},
             "document":{"file_id":"doc1","file_unique_id":"d","file_name":"player.jpg","mime_type":"MIME","file_size":4200000}}}
            """.Replace("MIME", mimeType, StringComparison.Ordinal)));

        command.Photo.Should().Be(isPhoto ? new IncomingPhoto("doc1", 4200000, IsFile: true) : null);
    }

    /// <summary>A button on the bot's reply to a photo carries that photo, so nothing has to be stored in between.</summary>
    [Fact]
    public void Button_on_a_photo_reply_carries_the_photo()
    {
        var command = TelegramUpdateMapper.ToCommand(Parse("""
            {"update_id":12,"callback_query":{"id":"cb2","chat_instance":"1","data":"photo:p",
             "from":{"id":42,"is_bot":false,"first_name":"Reza"},
             "message":{"message_id":70,"date":1,"chat":{"id":42,"type":"private"},"text":"Which player?",
               "reply_to_message":{"message_id":62,"date":1,"chat":{"id":42,"type":"private"},
                 "photo":[{"file_id":"only","file_unique_id":"o","width":960,"height":1280,"file_size":150000}]}}}}
            """));

        command.CallbackData.Should().Be("photo:p");
        command.Photo.Should().Be(new IncomingPhoto("only", 150000, IsFile: false));
        command.MessageId.Should().Be(62, "the photo's own message, so the picker can be redrawn as a reply to it");
    }

    /// <summary>Parses an update the way the webhook does.</summary>
    /// <param name="json">The update JSON.</param>
    /// <returns>The update.</returns>
    private static Update Parse(string json) => JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)!;
}
