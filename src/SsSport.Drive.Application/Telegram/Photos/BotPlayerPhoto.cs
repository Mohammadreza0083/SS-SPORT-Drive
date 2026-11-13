using System.Globalization;
using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Images;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Photos;

/// <summary>What the bot answers to a photo: the text and, for the picker, its buttons.</summary>
/// <param name="Text">The answer.</param>
/// <param name="Keyboard">The picker buttons, or <see langword="null"/>.</param>
public sealed record BotPhotoReply(string Text, BotKeyboard? Keyboard = null);

/// <summary>The callback data of the photo picker (T-4d); a player id or a page, well under Telegram's 64 bytes.</summary>
public static class PhotoCallbacks
{
    /// <summary>The prefix of a "this player" button.</summary>
    public const string PlayerPrefix = "photo:";

    /// <summary>The prefix of a picker page button.</summary>
    public const string PagePrefix = "photo-page:";

    /// <summary>How many players one picker page shows.</summary>
    public const int PageSize = 5;

    /// <summary>Tells whether callback data belongs to the photo picker.</summary>
    /// <param name="data">The callback data.</param>
    /// <returns><see langword="true"/> for picker buttons.</returns>
    public static bool IsPhoto(string data) => data.StartsWith(PlayerPrefix, StringComparison.Ordinal) || data.StartsWith(PagePrefix, StringComparison.Ordinal);

    /// <summary>Reads a picker button.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="playerId">The chosen player, or <see langword="null"/> for a page button.</param>
    /// <param name="page">The page, or 1 for a player button.</param>
    /// <returns><see langword="true"/> when the data is well formed.</returns>
    public static bool TryParse(string data, out Guid? playerId, out int page)
    {
        playerId = null;
        page = 1;
        if (data.StartsWith(PlayerPrefix, StringComparison.Ordinal) && Guid.TryParseExact(data[PlayerPrefix.Length..], "N", out var id))
        {
            playerId = id;
            return true;
        }

        return data.StartsWith(PagePrefix, StringComparison.Ordinal)
            && int.TryParse(data[PagePrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out page)
            && page >= 1;
    }

    /// <summary>Builds one picker page: a button per player, then Previous/Next where there are more.</summary>
    /// <param name="players">The players on the page.</param>
    /// <param name="page">The page number.</param>
    /// <param name="hasNext">Whether another page follows.</param>
    /// <returns>The keyboard.</returns>
    public static BotKeyboard Picker(IReadOnlyList<Player> players, int page, bool hasNext)
    {
        var rows = players.Select(player => (IReadOnlyList<BotButton>)[new BotButton(player.Name?.Value ?? BotTexts.ProgressUnnamed, PlayerPrefix + player.Id.ToString("N"))]).ToList();
        var navigation = new List<BotButton>();
        if (page > 1)
        {
            navigation.Add(new BotButton(BotTexts.PreviousButton, PagePrefix + (page - 1).ToString(CultureInfo.InvariantCulture)));
        }

        if (hasNext)
        {
            navigation.Add(new BotButton(BotTexts.NextButton, PagePrefix + (page + 1).ToString(CultureInfo.InvariantCulture)));
        }

        if (navigation.Count > 0)
        {
            rows.Add(navigation);
        }

        return new BotKeyboard(rows);
    }
}

/// <summary>An analyst players that still have no photo, newest first, one page at a time (decisions §2b: paged in SQL).</summary>
public sealed class PlayersAwaitingPhotoSpec : Specification<Player>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="analystId">The analyst.</param>
    /// <param name="page">The 1-based page, or <see langword="null"/> to only count.</param>
    public PlayersAwaitingPhotoSpec(Guid analystId, int? page = null)
    {
        Query.Where(player => player.AnalystId == analystId && player.Photo == null)
            .OrderByDescending(player => player.CreatedAt).ThenByDescending(player => player.Id)
            .AsNoTracking();
        if (page is { } number)
        {
            Query.Skip((number - 1) * PhotoCallbacks.PageSize).Take(PhotoCallbacks.PageSize);
        }
    }
}

/// <summary>The progress message a chat message is, read untracked.</summary>
public sealed class ProgressMessageByTelegramMessageSpec : Specification<ProgressMessage>, ISingleResultSpecification<ProgressMessage>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="chatId">The chat.</param>
    /// <param name="messageId">The Telegram message id.</param>
    public ProgressMessageByTelegramMessageSpec(long chatId, int messageId) =>
        Query.Where(message => message.ChatId == chatId && message.MessageId == messageId).AsNoTracking();
}

/// <summary>
/// A photo sent in the bot chat (T-4d). The player is the one whose progress message the photo replies to, or the one
/// picked with a button; without either, the answer is the picker. The photo is saved exactly like a Mini App upload.
/// </summary>
/// <param name="From">The sender.</param>
/// <param name="Photo">The photo.</param>
/// <param name="ReplyToMessageId">The message the photo replies to, or <see langword="null"/>.</param>
/// <param name="PlayerId">The player picked with a button, or <see langword="null"/>.</param>
/// <param name="Page">The picker page to show.</param>
public sealed record ReceiveBotPhotoCommand(TelegramSender From, IncomingPhoto Photo, int? ReplyToMessageId, Guid? PlayerId, int Page = 1)
    : ICommand<BotPhotoReply>;

/// <summary>Handles <see cref="ReceiveBotPhotoCommand"/>.</summary>
/// <param name="users">The team.</param>
/// <param name="players">The players.</param>
/// <param name="messages">The progress messages.</param>
/// <param name="bot">Downloads the photo.</param>
/// <param name="processor">Checks and converts the photo.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ReceiveBotPhotoCommandHandler(
    IRepository<User, Guid> users,
    IRepository<Player, Guid> players,
    IRepository<ProgressMessage, Guid> messages,
    ITelegramMessenger bot,
    IPhotoProcessor processor,
    IObjectStorage storage,
    TimeProvider timeProvider) : ICommandHandler<ReceiveBotPhotoCommand, BotPhotoReply>
{
    /// <summary>Finds the player, checks who may change its photo, and saves the photo; or shows the picker.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the bot answers.</returns>
    public async Task<BotPhotoReply> Handle(ReceiveBotPhotoCommand request, CancellationToken cancellationToken)
    {
        var user = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(request.From.UserId), cancellationToken);
        if (user is not { IsActive: true })
        {
            return new BotPhotoReply(BotTexts.PhotoNeedsAccess);
        }

        var playerId = request.PlayerId ?? await RepliedPlayerAsync(request, cancellationToken);
        if (playerId is null)
        {
            return await PickerAsync(user, request.Page, cancellationToken);
        }

        var player = await players.GetByIdAsync(playerId.Value, cancellationToken);
        if (player is null)
        {
            return new BotPhotoReply(BotTexts.PhotoPlayerGone);
        }

        if (!PlayerAccess.CanChangePhoto(player, user))
        {
            var owner = player.AnalystId is { } ownerId ? await users.GetByIdAsync(ownerId, cancellationToken) : null;
            return new BotPhotoReply(BotTexts.FormatPhotoNotYours(owner?.DisplayName ?? BotTexts.ProgressUnnamed));
        }

        if (request.Photo.FileSize > UploadPlayerPhotoCommand.MaxBytes)
        {
            return new BotPhotoReply(BotTexts.PhotoTooLarge);
        }

        var content = await bot.DownloadFileAsync(request.Photo.FileId, cancellationToken);
        if (content.Length > UploadPlayerPhotoCommand.MaxBytes)
        {
            return new BotPhotoReply(BotTexts.PhotoTooLarge);
        }

        try
        {
            await PlayerPhotos.SaveAsync(player, content, processor, storage, timeProvider, cancellationToken);
        }
        catch (DomainException exception) when (exception.Code is "player.photo_too_small" or "player.photo_unreadable")
        {
            return new BotPhotoReply(exception.Code == "player.photo_too_small" ? BotTexts.PhotoTooSmall : BotTexts.PhotoUnreadable);
        }

        return new BotPhotoReply(BotTexts.FormatPhotoSaved(player.Name?.Value ?? BotTexts.ProgressUnnamed));
    }

    /// <summary>Finds the player whose progress message the photo replies to.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The player id, or <see langword="null"/> when the photo replies to nothing of ours.</returns>
    private async Task<Guid?> RepliedPlayerAsync(ReceiveBotPhotoCommand request, CancellationToken cancellationToken) =>
        request.ReplyToMessageId is { } messageId
            ? (await messages.FirstOrDefaultAsync(new ProgressMessageByTelegramMessageSpec(request.From.ChatId, messageId), cancellationToken))?.PlayerId
            : null;

    /// <summary>Builds the picker page with the analyst players that still need a photo.</summary>
    /// <param name="user">The analyst.</param>
    /// <param name="page">The page.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The picker, or the explanation when nobody is waiting for a photo.</returns>
    private async Task<BotPhotoReply> PickerAsync(User user, int page, CancellationToken cancellationToken)
    {
        var total = await players.CountAsync(new PlayersAwaitingPhotoSpec(user.Id), cancellationToken);
        if (total == 0)
        {
            return new BotPhotoReply(BotTexts.PhotoNoPlayers);
        }

        var lastPage = (total + PhotoCallbacks.PageSize - 1) / PhotoCallbacks.PageSize;
        var current = Math.Min(page, lastPage);
        var onPage = await players.ListAsync(new PlayersAwaitingPhotoSpec(user.Id, current), cancellationToken);
        return new BotPhotoReply(BotTexts.PhotoPickPlayer, PhotoCallbacks.Picker(onPage, current, current < lastPage));
    }
}

/// <summary>Validates <see cref="ReceiveBotPhotoCommand"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class ReceiveBotPhotoCommandValidator : AbstractValidator<ReceiveBotPhotoCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReceiveBotPhotoCommandValidator()
    {
        RuleFor(r => r.From).Must(sender => sender is { UserId: > 0 } && sender.ChatId != 0).WithMessage("The Telegram sender is required.").OverridePropertyName("from");
        RuleFor(r => r.Photo).Must(photo => photo is not null && !string.IsNullOrWhiteSpace(photo.FileId) && photo.FileId.Length <= 256).WithMessage("The photo is required.").OverridePropertyName("photo");
        RuleFor(r => r.ReplyToMessageId).GreaterThan(0).When(r => r.ReplyToMessageId is not null).OverridePropertyName("replyToMessageId");
        RuleFor(r => r.PlayerId).NotEqual(Guid.Empty).When(r => r.PlayerId is not null).OverridePropertyName("playerId");
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
    }
}
