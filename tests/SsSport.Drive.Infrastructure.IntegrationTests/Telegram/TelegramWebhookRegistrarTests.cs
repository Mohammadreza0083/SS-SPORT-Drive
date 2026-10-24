using global::Telegram.Bot.Types.Enums;
using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Telegram;

/// <summary>
/// The <c>telegram-set-webhook</c> step registers the HTTPS webhook URL, the secret token and the three update types the
/// bot uses, and refuses to run with incomplete settings.
/// </summary>
public sealed class TelegramWebhookRegistrarTests
{
    /// <summary>Complete settings.</summary>
    private static readonly TelegramOptions _options = new()
    {
        BotToken = "123456:test-token",
        WebhookUrl = "https://api.drive.ss-sport.co/telegram/webhook",
        WebhookSecret = "s3cr3t_Token-1",
    };

    /// <summary>The request carries the URL, the secret and only the allowed update types, and keeps pending updates.</summary>
    [Fact]
    public void Builds_the_set_webhook_request()
    {
        var request = TelegramWebhookRegistrar.BuildRequest(_options);

        request.Url.Should().Be("https://api.drive.ss-sport.co/telegram/webhook");
        request.SecretToken.Should().Be("s3cr3t_Token-1");
        request.AllowedUpdates.Should().BeEquivalentTo([UpdateType.Message, UpdateType.CallbackQuery, UpdateType.MyChatMember]);
        request.DropPendingUpdates.Should().BeFalse();
    }

    /// <summary>Missing token, missing or non-HTTPS URL and a missing secret are rejected with a clear message.</summary>
    [Fact]
    public void Rejects_incomplete_settings()
    {
        var noToken = () => TelegramWebhookRegistrar.BuildRequest(_options with { BotToken = "" });
        var httpUrl = () => TelegramWebhookRegistrar.BuildRequest(_options with { WebhookUrl = "http://example.com/telegram/webhook" });
        var noSecret = () => TelegramWebhookRegistrar.BuildRequest(_options with { WebhookSecret = "" });

        noToken.Should().Throw<InvalidOperationException>().WithMessage("*Telegram__BotToken*");
        httpUrl.Should().Throw<InvalidOperationException>().WithMessage("*Telegram__WebhookUrl*");
        noSecret.Should().Throw<InvalidOperationException>().WithMessage("*Telegram__WebhookSecret*");
    }

    /// <summary>Secrets follow Telegram's rule: 1–256 characters from A–Z, a–z, 0–9, _ and -.</summary>
    /// <param name="secret">The secret.</param>
    /// <param name="valid">Whether it is allowed.</param>
    [Theory]
    [InlineData("abc_DEF-123", true)]
    [InlineData("", true)]
    [InlineData("has space", false)]
    [InlineData("bad!char", false)]
    public void Secret_format_is_validated(string secret, bool valid)
    {
        TelegramOptions.IsValidSecret(secret).Should().Be(valid);
    }

    /// <summary>A secret longer than 256 characters is rejected.</summary>
    [Fact]
    public void Overlong_secret_is_rejected()
    {
        TelegramOptions.IsValidSecret(new string('a', 257)).Should().BeFalse();
    }
}
