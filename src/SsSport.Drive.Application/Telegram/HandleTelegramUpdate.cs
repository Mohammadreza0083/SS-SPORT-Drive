using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Telegram.Access;
using SsSport.Drive.Application.Telegram.AccessRequests;
using SsSport.Drive.Application.Telegram.Chat;
using SsSport.Drive.Application.Telegram.Links;
using SsSport.Drive.Application.Telegram.Photos;
using SsSport.Drive.Application.Telegram.Progress;
using AccessRequestDecisionOutcome = SsSport.Drive.Application.Public.AccessRequestDecisionOutcome;
using DecideAccessRequestFromBotCommand = SsSport.Drive.Application.Public.DecideAccessRequestFromBotCommand;

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

/// <summary>Who sent an update: what the bot needs to create and greet a team member. Never logged.</summary>
/// <param name="UserId">The Telegram user id.</param>
/// <param name="ChatId">The chat to answer in (a private chat's id is the user id).</param>
/// <param name="Username">The username without <c>@</c>, if any.</param>
/// <param name="DisplayName">First and last name.</param>
public sealed record TelegramSender(long UserId, long ChatId, string? Username, string DisplayName);

/// <summary>
/// An authenticated update from Telegram (webhook or local polling), reduced to what the application needs: ids, the
/// sender, a bot command name (<c>start</c>), the data of one of our buttons, or a typed message for the chat flow
/// (T-10a). The typed text is never logged; it is read only while a chat question waits for it.
/// </summary>
/// <param name="UpdateId">Telegram's <c>update_id</c>.</param>
/// <param name="Kind">The update type, e.g. <c>Message</c> or <c>CallbackQuery</c>.</param>
/// <param name="TelegramUserId">The sender's Telegram user id, when the update has one.</param>
public sealed record HandleTelegramUpdateCommand(long UpdateId, string Kind, long? TelegramUserId) : ICommand<TelegramUpdateOutcome>
{
    /// <summary>Gets who sent the update, if anyone.</summary>
    public TelegramSender? Sender { get; init; }

    /// <summary>Gets the bot command without slash or bot name (e.g. <c>start</c>), for messages that start with one.</summary>
    public string? BotCommand { get; init; }

    /// <summary>
    /// Gets a typed message that is not a bot command, for the chat flow (T-10a); <see langword="null"/> otherwise. Never logged.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>Gets a pressed button's data, for callback queries.</summary>
    public string? CallbackData { get; init; }

    /// <summary>Gets the callback query id to answer, for callback queries.</summary>
    public string? CallbackQueryId { get; init; }

    /// <summary>Gets the id of the message the pressed button is on.</summary>
    public int? CallbackMessageId { get; init; }

    /// <summary>
    /// Gets the photo in the message, or for a button press the photo the pressed message replies to (T-4d);
    /// <see langword="null"/> when there is none.
    /// </summary>
    public IncomingPhoto? Photo { get; init; }

    /// <summary>Gets the id of the message carrying <see cref="Photo"/>, or <see langword="null"/>.</summary>
    public int? MessageId { get; init; }

    /// <summary>Gets the id of the message the user replied to, or <see langword="null"/>.</summary>
    public int? ReplyToMessageId { get; init; }
}

/// <summary>Checks that an update has a valid id and a kind.</summary>
public sealed class HandleTelegramUpdateCommandValidator : AbstractValidator<HandleTelegramUpdateCommand>
{
    /// <summary>Initializes the rules.</summary>
    public HandleTelegramUpdateCommandValidator()
    {
        RuleFor(command => command.UpdateId).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Kind).NotEmpty();
        RuleFor(command => command.Text).MaximumLength(4096);
    }
}

/// <summary>
/// Takes in an update once (records its id and logs ids only), then routes it: <c>/help</c> → help; the New player
/// button, <c>/new</c>, <c>/cancel</c>, the chat's buttons and answers to an open chat question →
/// <see cref="ChatNewPlayerCommand"/> (T-10a); any other message → <see cref="StartBotCommand"/>; buttons → access
/// request, review, or the menu. The My players button, <c>/players</c>, its buttons and a typed search →
/// <see cref="ChatMyPlayersCommand"/> (T-10b). State changes run as their own commands, so they are saved (and their events sent)
/// before the bot replies.
/// </summary>
/// <param name="inbox">The update inbox.</param>
/// <param name="sender">Sends the access commands.</param>
/// <param name="bot">The messenger.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class HandleTelegramUpdateCommandHandler(
    ITelegramUpdateInbox inbox,
    ISender sender,
    ITelegramMessenger bot,
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
        if (request.Sender is { } from)
        {
            if (request.CallbackData is { } data && request.CallbackQueryId is { } callbackId && PhotoCallbacks.IsPhoto(data))
            {
                await OnPhotoButtonAsync(from, data, callbackId, request, cancellationToken);
            }
            else if (request.CallbackData is { } playersData && request.CallbackQueryId is { } playersButtonId && MyPlayersCallbacks.IsMyPlayers(playersData))
            {
                await OnMyPlayersButtonAsync(from, playersData, playersButtonId, request.CallbackMessageId, cancellationToken);
            }
            else if (request.CallbackData is { } chatData && request.CallbackQueryId is { } chatButtonId && ChatCallbacks.IsChat(chatData))
            {
                await OnChatButtonAsync(from, chatData, chatButtonId, request.CallbackMessageId, cancellationToken);
            }
            else if (request.CallbackData is { } buttonData && request.CallbackQueryId is { } buttonId)
            {
                await OnButtonAsync(from, buttonData, buttonId, request.CallbackMessageId, cancellationToken);
            }
            else if (request.Kind == "Message" && request.Photo is { } photo)
            {
                if (!await SendChatAsync(from, new ChatInput(ChatInputKind.Photo, Photo: photo), cancellationToken))
                {
                    var reply = await sender.Send(new ReceiveBotPhotoCommand(from, photo, request.ReplyToMessageId, null), cancellationToken);
                    await bot.SendReplyAsync(from.ChatId, request.MessageId, reply.Text, reply.Keyboard, cancellationToken);
                }
            }
            else if (request.Kind == "Message" && ChatInputOf(request) is { } chatInput)
            {
                if (!(chatInput.Kind == ChatInputKind.Text && await SendMyPlayersAsync(from, chatInput, cancellationToken)) && !await SendChatAsync(from, chatInput, cancellationToken))
                {
                    await OnMessageAsync(from, request.BotCommand, cancellationToken);
                }
            }
            else if (request.Kind == "Message")
            {
                await OnMessageAsync(from, request.BotCommand, cancellationToken);
            }
        }

        return TelegramUpdateOutcome.Accepted;
    }

    /// <summary>Answers a message: help, or the start screen for the sender's state.</summary>
    /// <param name="from">The sender.</param>
    /// <param name="botCommand">The bot command, if any.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    private async Task OnMessageAsync(TelegramSender from, string? botCommand, CancellationToken cancellationToken)
    {
        if (botCommand == "help")
        {
            await bot.SendAsync(from.ChatId, BotTexts.Help, null, cancellationToken);
            return;
        }

        var status = await sender.Send(new StartBotCommand(from), cancellationToken);
        var (text, keyboard) = status.State switch
        {
            BotUserState.Unknown => (BotTexts.FormatWelcome(from.UserId), BotCallbacks.RequestAccessKeyboard),
            BotUserState.FirstAdminNeedsEmail => (BotTexts.FirstAdminNeedsEmail, null),
            BotUserState.Pending => (BotTexts.RequestPending, null),
            BotUserState.Active => (BotTexts.FormatWelcomeBack(status.DisplayName), BotCallbacks.MenuKeyboard),
            BotUserState.Declined when status.CanRequestAgainAt is { } at && at > timeProvider.GetUtcNow() => (BotTexts.FormatRequestTooSoon(HoursUntil(at)), null),
            BotUserState.Declined => (BotTexts.FormatWelcome(from.UserId), BotCallbacks.RequestAccessKeyboard),
            BotUserState.Removed => (BotTexts.AccessEnded, BotCallbacks.RequestAccessKeyboard),
            _ => ((string?)null, (BotKeyboard?)null),
        };

        if (status.State == BotUserState.Active && botCommand == "players")
        {
            await SendMyPlayersAsync(from, new ChatInput(ChatInputKind.Start), cancellationToken);
            return;
        }

        // The first admin is welcomed by the UserApproved event, so nothing is sent here.
        if (text is not null)
        {
            await bot.SendAsync(from.ChatId, text, keyboard, cancellationToken);
        }
    }

    /// <summary>Answers a button press.</summary>
    /// <param name="from">The presser.</param>
    /// <param name="data">The button data.</param>
    /// <param name="callbackId">The callback query to answer.</param>
    /// <param name="messageId">The message the button is on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the press is answered.</returns>
    private async Task OnButtonAsync(TelegramSender from, string data, string callbackId, int? messageId, CancellationToken cancellationToken)
    {
        switch (data)
        {
            case BotCallbacks.RequestAccess:
                await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
                var requested = await sender.Send(new RequestBotAccessCommand(from), cancellationToken);
                var (text, keyboard) = requested.Outcome switch
                {
                    AccessRequestOutcome.Requested => (BotTexts.RequestSent, null),
                    AccessRequestOutcome.AlreadyPending => (BotTexts.RequestPending, null),
                    AccessRequestOutcome.AlreadyActive => (BotTexts.FormatWelcomeBack(requested.DisplayName), BotCallbacks.MenuKeyboard),
                    _ => (BotTexts.FormatRequestTooSoon(HoursUntil(requested.CanRequestAgainAt!.Value)), (BotKeyboard?)null),
                };
                await ReplyAsync(from, messageId, text, keyboard, cancellationToken);
                return;

            case BotCallbacks.Help:
                await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
                await bot.SendAsync(from.ChatId, BotTexts.Help, null, cancellationToken);
                return;

            case BotCallbacks.NewPlayer:
                await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
                await SendChatAsync(from, new ChatInput(ChatInputKind.Start), cancellationToken);
                return;

            case BotCallbacks.MyPlayers:
                await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
                await SendMyPlayersAsync(from, new ChatInput(ChatInputKind.Start), cancellationToken);
                return;
        }

        if (ForwardCallbacks.TryParse(data, out var keep, out var forwardId))
        {
            await OnForwardButtonAsync(from, keep, forwardId, callbackId, messageId, cancellationToken);
            return;
        }

        if (PlayerPublishCallbacks.TryParse(data, out var publishPlayerId))
        {
            await OnPublishButtonAsync(from, publishPlayerId, callbackId, cancellationToken);
            return;
        }

        if (AccessRequestCallbacks.TryParse(data, out var approveRequest, out var accessRequestId))
        {
            await OnAccessRequestButtonAsync(from, approveRequest, accessRequestId, callbackId, messageId, cancellationToken);
            return;
        }

        if (!BotCallbacks.TryParseReview(data, out var decision, out var userId))
        {
            await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
            return;
        }

        var review = await sender.Send(new ReviewAccessRequestCommand(from.UserId, userId, decision), cancellationToken);
        switch (review.Outcome)
        {
            case ReviewOutcome.NotAllowed:
                await bot.AnswerCallbackAsync(callbackId, BotTexts.OnlyAdmins, true, cancellationToken);
                return;
            case ReviewOutcome.AdminNeedsEmail:
                await bot.AnswerCallbackAsync(callbackId, BotTexts.FormatAdminNeedsEmail(review.RequesterName), true, cancellationToken);
                return;
        }

        await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
        var outcomeText = review.Outcome switch
        {
            ReviewOutcome.Approved => BotTexts.FormatApproved(review.RequesterName, review.Detail, review.ReviewerName),
            ReviewOutcome.Declined => BotTexts.FormatDeclined(review.RequesterName, review.ReviewerName),
            ReviewOutcome.AlreadyHandled => BotTexts.FormatAlreadyHandled(review.RequesterName, review.Detail, review.ReviewerName),
            _ => BotTexts.RequestGone,
        };
        await ReplyAsync(from, messageId, outcomeText, null, cancellationToken);
    }

    /// <summary>
    /// Answers a Keep or Block press on a forward alert (L-3): only admins decide; the alert is replaced by the outcome.
    /// </summary>
    /// <param name="from">The presser.</param>
    /// <param name="keep"><see langword="true"/> for Keep.</param>
    /// <param name="forwardId">The forward.</param>
    /// <param name="callbackId">The callback query to answer.</param>
    /// <param name="messageId">The alert message.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the press is answered.</returns>
    private async Task OnForwardButtonAsync(TelegramSender from, bool keep, Guid forwardId, string callbackId, int? messageId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DecideForwardCommand(from.UserId, forwardId, keep), cancellationToken);
        if (result.Outcome == ForwardDecisionOutcome.NotAllowed)
        {
            LogForwardDenied(logger, from.UserId, forwardId);
            await bot.AnswerCallbackAsync(callbackId, BotTexts.OnlyAdminsForwards, true, cancellationToken);
            return;
        }

        await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
        await ReplyAsync(from, messageId, ForwardCallbacks.Describe(result), null, cancellationToken);
    }

    /// <summary>
    /// Answers an Approve or Decline press on an access-request alert (P-4): only admins decide; the alert is replaced by
    /// the outcome.
    /// </summary>
    /// <param name="from">The presser.</param>
    /// <param name="approve"><see langword="true"/> for Approve.</param>
    /// <param name="requestId">The access request.</param>
    /// <param name="callbackId">The callback query to answer.</param>
    /// <param name="messageId">The alert message.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the press is answered.</returns>
    private async Task OnAccessRequestButtonAsync(TelegramSender from, bool approve, Guid requestId, string callbackId, int? messageId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DecideAccessRequestFromBotCommand(from.UserId, requestId, approve), cancellationToken);
        if (result.Outcome == AccessRequestDecisionOutcome.NotAllowed)
        {
            LogAccessRequestDenied(logger, from.UserId, requestId);
            await bot.AnswerCallbackAsync(callbackId, BotTexts.OnlyAdminsAccessRequests, true, cancellationToken);
            return;
        }

        await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
        await ReplyAsync(from, messageId, AccessRequestCallbacks.Describe(result), null, cancellationToken);
    }

    /// <summary>
    /// Handles the Publish button on an admin's progress message (T-11): publishes and answers the press. The progress
    /// message itself turns published through <c>PlayerPublished</c>, like a publish from the console.
    /// </summary>
    /// <param name="from">The presser.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="callbackId">The callback query to answer.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the press is answered.</returns>
    private async Task OnPublishButtonAsync(TelegramSender from, Guid playerId, string callbackId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PublishPlayerFromBotCommand(from.UserId, playerId), cancellationToken);
        if (result.Outcome == BotPublishOutcome.NotAllowed)
        {
            LogPublishDenied(logger, from.UserId, playerId);
        }

        var (text, alert) = PlayerPublishCallbacks.Describe(result);
        await bot.AnswerCallbackAsync(callbackId, text, alert, cancellationToken);
    }

    /// <summary>Tells what a message is for the new-player chat: <c>/new</c>, <c>/cancel</c> or typed text.</summary>
    /// <param name="request">The update.</param>
    /// <returns>The chat input, or <see langword="null"/> for other messages.</returns>
    private static ChatInput? ChatInputOf(HandleTelegramUpdateCommand request) => request.BotCommand switch
    {
        "new" => new ChatInput(ChatInputKind.Start),
        "cancel" => new ChatInput(ChatInputKind.Cancel),
        null when request.Text is { } text => new ChatInput(ChatInputKind.Text, Text: text),
        _ => null,
    };

    /// <summary>Runs one step of the new-player chat and sends its answer as a new message.</summary>
    /// <param name="from">The sender.</param>
    /// <param name="input">What the sender did.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="false"/> when the input was not for the chat, so the caller handles it as before.</returns>
    private async Task<bool> SendChatAsync(TelegramSender from, ChatInput input, CancellationToken cancellationToken)
    {
        var reply = await sender.Send(new ChatNewPlayerCommand(from, input), cancellationToken);
        if (reply?.Text is { } text)
        {
            await bot.SendAsync(from.ChatId, text, reply.Keyboard, cancellationToken);
        }

        return reply is not null;
    }

    /// <summary>
    /// Handles a button of the new-player chat (T-10a): a short note on the press, and the message the button is on
    /// edited into the next question.
    /// </summary>
    /// <param name="from">The presser.</param>
    /// <param name="data">The button data.</param>
    /// <param name="callbackId">The press, to answer.</param>
    /// <param name="messageId">The message the button is on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the press is answered.</returns>
    private async Task OnChatButtonAsync(TelegramSender from, string data, string callbackId, int? messageId, CancellationToken cancellationToken)
    {
        var reply = await sender.Send(new ChatNewPlayerCommand(from, new ChatInput(ChatInputKind.Button, Data: data)), cancellationToken);
        await bot.AnswerCallbackAsync(callbackId, reply?.Toast, false, cancellationToken);
        if (reply?.Text is { } text)
        {
            await ReplyAsync(from, messageId, text, reply.Keyboard, cancellationToken);
        }
    }

    /// <summary>Runs one My players input (T-10b) and sends its answer as a new message.</summary>
    /// <param name="from">The sender.</param>
    /// <param name="input">What the sender did.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="false"/> when the input was not for My players, so the caller handles it as before.</returns>
    private async Task<bool> SendMyPlayersAsync(TelegramSender from, ChatInput input, CancellationToken cancellationToken)
    {
        var reply = await sender.Send(new ChatMyPlayersCommand(from, input), cancellationToken);
        if (reply?.Text is { } text)
        {
            await bot.SendAsync(from.ChatId, text, reply.Keyboard, cancellationToken);
        }

        return reply is not null;
    }

    /// <summary>
    /// Handles a button of My players (T-10b): a short note on the press, and the message the button is on edited into
    /// the page, card or files the button leads to.
    /// </summary>
    /// <param name="from">The presser.</param>
    /// <param name="data">The button data.</param>
    /// <param name="callbackId">The press, to answer.</param>
    /// <param name="messageId">The message the button is on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the press is answered.</returns>
    private async Task OnMyPlayersButtonAsync(TelegramSender from, string data, string callbackId, int? messageId, CancellationToken cancellationToken)
    {
        var reply = await sender.Send(new ChatMyPlayersCommand(from, new ChatInput(ChatInputKind.Button, Data: data)), cancellationToken);
        await bot.AnswerCallbackAsync(callbackId, reply?.Toast, false, cancellationToken);
        if (reply?.Text is { } text)
        {
            await ReplyAsync(from, messageId, text, reply.Keyboard, cancellationToken);
        }
    }

    /// <summary>
    /// Handles a button on the photo picker (T-4d): saves the photo for the chosen player, or shows another page. The
    /// photo comes from the message the picker replies to, and the picker itself becomes the answer.
    /// </summary>
    /// <param name="from">Who pressed.</param>
    /// <param name="data">The button data.</param>
    /// <param name="callbackId">The press, to answer.</param>
    /// <param name="request">The update, with the photo and the picker message.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the picker is updated.</returns>
    private async Task OnPhotoButtonAsync(TelegramSender from, string data, string callbackId, HandleTelegramUpdateCommand request, CancellationToken cancellationToken)
    {
        await bot.AnswerCallbackAsync(callbackId, null, false, cancellationToken);
        var reply = request.Photo is { } photo && PhotoCallbacks.TryParse(data, out var playerId, out var page)
            ? await sender.Send(new ReceiveBotPhotoCommand(from, photo, null, playerId, page), cancellationToken)
            : new BotPhotoReply(BotTexts.PhotoFailed);
        await ReplyAsync(from, request.CallbackMessageId, reply.Text, reply.Keyboard, cancellationToken);
    }

    /// <summary>Edits the message the button was on, or sends a new one when it is unknown.</summary>
    /// <param name="to">The chat.</param>
    /// <param name="messageId">The message to edit, if known.</param>
    /// <param name="text">The text.</param>
    /// <param name="keyboard">The buttons.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when Telegram has the reply.</returns>
    private Task ReplyAsync(TelegramSender to, int? messageId, string text, BotKeyboard? keyboard, CancellationToken cancellationToken) =>
        messageId is { } id
            ? bot.EditAsync(to.ChatId, id, text, keyboard, cancellationToken)
            : bot.SendAsync(to.ChatId, text, keyboard, cancellationToken);

    /// <summary>Whole hours from now until a time, at least 1.</summary>
    /// <param name="at">The time.</param>
    /// <returns>The hours.</returns>
    private int HoursUntil(DateTimeOffset at) => Math.Max(1, (int)Math.Ceiling((at - timeProvider.GetUtcNow()).TotalHours));

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

    /// <summary>Logs a Keep or Block press by someone who is not an active admin (suspicious activity, decisions §1b).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="TelegramUserId">Who pressed.</param>
    /// <param name="ForwardId">The forward.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Forward decision denied: Telegram user {TelegramUserId} is not an admin (forward {ForwardId})")]
    private static partial void LogForwardDenied(ILogger logger, long TelegramUserId, Guid ForwardId);

    /// <summary>Logs a Publish press by someone who is not an active admin (suspicious activity, decisions §1b).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="TelegramUserId">Who pressed.</param>
    /// <param name="PlayerId">The player.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Publish denied: Telegram user {TelegramUserId} is not an admin (player {PlayerId})")]
    private static partial void LogPublishDenied(ILogger logger, long TelegramUserId, Guid PlayerId);

    /// <summary>Logs an Approve or Decline press by someone who is not an active admin (suspicious activity, decisions §1b).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="TelegramUserId">Who pressed.</param>
    /// <param name="AccessRequestId">The access request.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Access request decision denied: Telegram user {TelegramUserId} is not an admin (request {AccessRequestId})")]
    private static partial void LogAccessRequestDenied(ILogger logger, long TelegramUserId, Guid AccessRequestId);
}
