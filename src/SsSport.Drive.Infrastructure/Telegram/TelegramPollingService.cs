using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>
/// Local development only (<c>Telegram__Mode=Polling</c>): fetches updates from Telegram with long polling (through
/// <c>Telegram__Proxy</c> where Telegram needs a VPN) and feeds them into the same command as the webhook. It removes a
/// registered webhook first, because Telegram allows one or the other. In webhook mode it does nothing.
/// </summary>
/// <param name="scopes">Creates a scope per update, like a request.</param>
/// <param name="options">The Telegram settings.</param>
/// <param name="logger">The logger.</param>
public sealed partial class TelegramPollingService(IServiceScopeFactory scopes, IOptions<TelegramOptions> options, ILogger<TelegramPollingService> logger)
    : BackgroundService
{
    /// <summary>How long one long poll waits for updates.</summary>
    private const int PollSeconds = 25;

    /// <summary>The pause after a failed poll.</summary>
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Polls until the host stops.</summary>
    /// <param name="stoppingToken">Signals shutdown.</param>
    /// <returns>A task that completes when polling stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsPolling)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Value.BotToken))
        {
            LogNoToken(logger);
            return;
        }

        ITelegramBotClient client;
        await using (var scope = scopes.CreateAsyncScope())
        {
            client = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        }

        LogStarted(logger);
        var offset = 0;
        var webhookRemoved = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!webhookRemoved)
                {
                    await client.DeleteWebhook(dropPendingUpdates: false, cancellationToken: stoppingToken);
                    webhookRemoved = true;
                }

                var updates = await client.GetUpdates(offset, timeout: PollSeconds, allowedUpdates: TelegramWebhookRegistrar.AllowedUpdates, cancellationToken: stoppingToken);
                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    await HandleAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPollFailed(logger, exception);
                await Task.Delay(_retryDelay, stoppingToken);
            }
        }
    }

    /// <summary>Handles one update in its own scope; a failure is logged and does not stop polling.</summary>
    /// <param name="update">The update.</param>
    /// <param name="cancellationToken">Signals shutdown.</param>
    /// <returns>A task that completes when the update is handled.</returns>
    private async Task HandleAsync(global::Telegram.Bot.Types.Update update, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(TelegramUpdateMapper.ToCommand(update), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUpdateFailed(logger, exception, update.Id);
        }
    }

    /// <summary>Logs the start of polling.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram polling mode: fetching updates from Telegram (local development)")]
    private static partial void LogStarted(ILogger logger);

    /// <summary>Logs a missing token.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram polling mode is on, but Telegram__BotToken is not set; polling is off")]
    private static partial void LogNoToken(ILogger logger);

    /// <summary>Logs a failed poll.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling Telegram failed; retrying in 5 s (check Telegram__Proxy if Telegram needs a VPN)")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    /// <summary>Logs a failed update.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="UpdateId">The update id.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Handling polled Telegram update {UpdateId} failed")]
    private static partial void LogUpdateFailed(ILogger logger, Exception exception, long UpdateId);
}
