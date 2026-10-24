using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>
/// Registers the bot's webhook with Telegram (<c>setWebhook</c>). Run as its own step
/// (<c>SsSport.Drive.Api telegram-set-webhook</c>) after a deploy, never on normal start-up.
/// </summary>
public static partial class TelegramWebhookRegistrar
{
    /// <summary>The update types the bot handles: messages, button presses, and being blocked or unblocked.</summary>
    public static readonly IReadOnlyList<UpdateType> AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery, UpdateType.MyChatMember];

    /// <summary>Builds the <c>setMyCommands</c> request: the commands everyone sees in Telegram's menu.</summary>
    /// <returns>The request.</returns>
    public static SetMyCommandsRequest BuildCommandsRequest() => new()
    {
        Commands =
        [
            new BotCommand { Command = "start", Description = "Open the menu or ask for access" },
            new BotCommand { Command = "help", Description = "How the bot works" },
        ],
    };

    /// <summary>Builds the <c>setWebhook</c> request from the settings.</summary>
    /// <param name="options">The Telegram settings.</param>
    /// <returns>The request.</returns>
    /// <exception cref="InvalidOperationException">A required setting is missing or invalid.</exception>
    public static SetWebhookRequest BuildRequest(TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            throw new InvalidOperationException("Telegram__BotToken is not set.");
        }

        if (!Uri.TryCreate(options.WebhookUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Telegram__WebhookUrl must be the public HTTPS URL of /telegram/webhook.");
        }

        if (string.IsNullOrEmpty(options.WebhookSecret) || !TelegramOptions.IsValidSecret(options.WebhookSecret))
        {
            throw new InvalidOperationException("Telegram__WebhookSecret must be 1-256 characters of A-Z, a-z, 0-9, _ or -.");
        }

        return new SetWebhookRequest
        {
            Url = url.ToString(),
            SecretToken = options.WebhookSecret,
            AllowedUpdates = AllowedUpdates,
            DropPendingUpdates = false,
        };
    }

    /// <summary>Calls <c>setWebhook</c> with the configured URL, secret and update types, then <c>setMyCommands</c>.</summary>
    /// <param name="services">The root service provider of a host that called <c>AddInfrastructure</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when Telegram has accepted the webhook.</returns>
    public static async Task RegisterAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.GetRequiredService<IOptions<TelegramOptions>>().Value;
        var request = BuildRequest(options);
        var client = services.GetRequiredService<ITelegramBotClient>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TelegramWebhookRegistrar));

        await client.SendRequest(request, cancellationToken);
        await client.SendRequest(BuildCommandsRequest(), cancellationToken);
        LogRegistered(logger, new Uri(request.Url).Host, string.Join(",", AllowedUpdates));
    }

    /// <summary>Logs a successful registration (host only; the secret is never logged).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="WebhookHost">The webhook URL's host.</param>
    /// <param name="AllowedUpdates">The registered update types.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram webhook registered for {WebhookHost} with updates {AllowedUpdates}")]
    private static partial void LogRegistered(ILogger logger, string WebhookHost, string AllowedUpdates);
}
