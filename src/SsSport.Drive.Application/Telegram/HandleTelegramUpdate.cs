using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Telegram.Access;
using SsSport.Drive.Application.Telegram.Photos;

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
/// sender, a bot command name (<c>start</c>) or the data of one of our buttons. Free message text never travels in this
/// command, so it can never reach the logs.
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
    }
}

/// <summary>
/// Takes in an update once (records its id and logs ids only), then routes it: <c>/help</c> → help; any other message
/// → <see cref="StartBotCommand"/>; buttons → access request, review, or the menu. The New player and My players buttons (and <c>/new</c>,
/// <c>/players</c> for active members) answer with a button that opens that Mini App page. State changes run as their own
/// commands, so they are saved (and their events sent) before the bot replies.
/// </summary>
/// <param name="inbox">The update inbox.</param>
/// <param name="sender">Sends the access commands.</param>
/// <param name="bot">The messenger.</param>
/// <param name="miniApp">Links to Mini App pages, for the New player and My players buttons.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class HandleTelegramUpdateCommandHandler(
    ITelegramUpdateInbox inbox,
    ISender sender,
    ITelegramMessenger bot,
    IMiniAppLinks miniApp,
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
            else if (request.CallbackData is { } buttonData && request.CallbackQueryId is { } buttonId)
            {
                await OnButtonAsync(from, buttonData, buttonId, request.CallbackMessageId, cancellationToken);
            }
            else if (request.Kind == "Message" && request.Photo is { } photo)
            {
                var reply = await sender.Send(new ReceiveBotPhotoCommand(from, photo, request.ReplyToMessageId, null), cancellationToken);
                await bot.SendReplyAsync(from.ChatId, request.MessageId, reply.Text, reply.Keyboard, cancellationToken);
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

        if (status.State == BotUserState.Active && MiniAppPageOf(botCommand) is { } miniAppPage)
        {
            await SendMiniAppLinkAsync(from, miniAppPage, null, cancellationToken);
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
                await SendMiniAppLinkAsync(from, MiniAppPage.NewPlayer, callbackId, cancellationToken);
                return;

            case BotCallbacks.MyPlayers:
                await SendMiniAppLinkAsync(from, MiniAppPage.MyPlayers, callbackId, cancellationToken);
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

    /// <summary>Tells which Mini App page a bot command opens.</summary>
    /// <param name="botCommand">The bot command, if any.</param>
    /// <returns>The page, or <see langword="null"/> for other commands.</returns>
    private static MiniAppPage? MiniAppPageOf(string? botCommand) => botCommand switch
    {
        "new" => MiniAppPage.NewPlayer,
        "players" => MiniAppPage.MyPlayers,
        _ => null,
    };

    /// <summary>
    /// Sends a button that opens a Mini App page. Without a configured Mini App URL, a button press is answered with
    /// a note instead (the Mini App guards access itself).
    /// </summary>
    /// <param name="to">The chat.</param>
    /// <param name="page">The page.</param>
    /// <param name="callbackId">The button press to answer, or <see langword="null"/> for a command.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    private async Task SendMiniAppLinkAsync(TelegramSender to, MiniAppPage page, string? callbackId, CancellationToken cancellationToken)
    {
        var (path, text, label) = page == MiniAppPage.MyPlayers
            ? ("players", BotTexts.OpenMyPlayers, BotTexts.OpenMyPlayersButton)
            : ("new", BotTexts.OpenNewPlayer, BotTexts.OpenNewPlayerButton);
        var url = miniApp.PageUrl(path);
        if (callbackId is not null)
        {
            await bot.AnswerCallbackAsync(callbackId, url is null ? BotTexts.MiniAppNotSetUp : null, false, cancellationToken);
        }

        if (url is not null)
        {
            await bot.SendAsync(to.ChatId, text, BotKeyboard.Column(BotButton.OpenMiniApp(label, url)), cancellationToken);
        }
        else if (callbackId is null)
        {
            await bot.SendAsync(to.ChatId, BotTexts.MiniAppNotSetUp, null, cancellationToken);
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
}

/// <summary>A Mini App page the bot links to.</summary>
internal enum MiniAppPage
{
    /// <summary>The new-player form (<c>/miniapp/new</c>).</summary>
    NewPlayer,

    /// <summary>My players (<c>/miniapp/players</c>).</summary>
    MyPlayers,
}
