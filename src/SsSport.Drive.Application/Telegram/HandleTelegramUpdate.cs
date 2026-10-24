using FluentValidation;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Messaging;

namespace SsSport.Drive.Application.Telegram;

/// <summary>What happened to an incoming Telegram update.</summary>
public enum TelegramUpdateOutcome
{
    /// <summary>The update was new and has been taken in.</summary>
    Accepted,

    /// <summary>The update had been received before (Telegram re-delivered it) and was skipped.</summary>
    Duplicate,
}

/// <summary>
/// Remembers which Telegram updates were received, so a re-delivered update is processed only once.
/// Implemented in Infrastructure on the <c>telegram_updates</c> table.
/// </summary>
public interface ITelegramUpdateInbox
{
    /// <summary>Records an update id unless it is already recorded; safe against concurrent deliveries.</summary>
    /// <param name="updateId">Telegram's <c>update_id</c>.</param>
    /// <param name="receivedAt">When it arrived (UTC).</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the id is new; <see langword="false"/> when it was seen before.</returns>
    Task<bool> TryRecordAsync(long updateId, DateTimeOffset receivedAt, CancellationToken cancellationToken);
}

/// <summary>
/// An authenticated update from the Telegram webhook, reduced to what the application needs. Message text never
/// travels in this command, so it can never reach the logs.
/// </summary>
/// <param name="UpdateId">Telegram's <c>update_id</c>.</param>
/// <param name="Kind">The update type, e.g. <c>Message</c> or <c>CallbackQuery</c>.</param>
/// <param name="TelegramUserId">The sender's Telegram user id, when the update has one.</param>
public sealed record HandleTelegramUpdateCommand(long UpdateId, string Kind, long? TelegramUserId) : ICommand<TelegramUpdateOutcome>;

/// <summary>Checks that an update has a valid id and a kind.</summary>
public sealed class HandleTelegramUpdateCommandValidator : AbstractValidator<HandleTelegramUpdateCommand>
{
    /// <summary>Initializes the rules.</summary>
    public HandleTelegramUpdateCommandValidator()
    {
        RuleFor(command => command.UpdateId).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Kind).NotEmpty();
    }
}

/// <summary>
/// Takes in an update once: records its id and logs it (ids only). Routing to the bot features (access requests,
/// approvals…) is added by later backlog items (T-2 onwards).
/// </summary>
/// <param name="inbox">The update inbox.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class HandleTelegramUpdateCommandHandler(
    ITelegramUpdateInbox inbox,
    TimeProvider timeProvider,
    ILogger<HandleTelegramUpdateCommandHandler> logger)
    : ICommandHandler<HandleTelegramUpdateCommand, TelegramUpdateOutcome>
{
    /// <summary>Records and logs the update, or skips it when it was seen before.</summary>
    /// <param name="request">The update.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether the update was new.</returns>
    public async Task<TelegramUpdateOutcome> Handle(HandleTelegramUpdateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await inbox.TryRecordAsync(request.UpdateId, timeProvider.GetUtcNow(), cancellationToken))
        {
            LogDuplicate(logger, request.UpdateId, request.Kind);
            return TelegramUpdateOutcome.Duplicate;
        }

        LogAccepted(logger, request.UpdateId, request.Kind, request.TelegramUserId);
        return TelegramUpdateOutcome.Accepted;
    }

    /// <summary>Logs a new update.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="UpdateId">The update id.</param>
    /// <param name="UpdateKind">The update type.</param>
    /// <param name="TelegramUserId">The sender's Telegram id, if any.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram update {UpdateId} ({UpdateKind}) received from user {TelegramUserId}")]
    private static partial void LogAccepted(ILogger logger, long UpdateId, string UpdateKind, long? TelegramUserId);

    /// <summary>Logs a re-delivered update.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="UpdateId">The update id.</param>
    /// <param name="UpdateKind">The update type.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Telegram update {UpdateId} ({UpdateKind}) was already received; skipped")]
    private static partial void LogDuplicate(ILogger logger, long UpdateId, string UpdateKind);
}
