using Ardalis.Specification;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Progress;

/// <summary>
/// Writes the text of a player's progress message (T-6a): the player first, then one line per step, done (✅),
/// waiting (⏳) or still to do (⬜). The build steps (copy, transcode, publish) join here in T-6b.
/// </summary>
public static class PlayerProgressText
{
    /// <summary>Renders the progress of a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="folderCount">How many Drive folders are linked.</param>
    /// <returns>The message text.</returns>
    public static string Render(Player player, int folderCount)
    {
        ArgumentNullException.ThrowIfNull(player);
        var name = player.Name?.Value ?? BotTexts.ProgressUnnamed;
        var lines = new List<string>
        {
            player.IsPublished ? BotTexts.FormatProgressHeaderPublished(name) : BotTexts.FormatProgressHeaderWaiting(name),
        };

        if (player.Volleybox is not null && folderCount > 0)
        {
            lines.Add(BotTexts.ProgressLinksVerified);
        }

        lines.Add(folderCount switch
        {
            0 => BotTexts.ProgressNoFolder,
            1 => BotTexts.ProgressOneFolder,
            _ => BotTexts.FormatProgressFolders(folderCount),
        });
        lines.Add(player.Photo is null ? BotTexts.ProgressPhotoMissing : BotTexts.ProgressPhotoAdded);

        var otherMissing = player.MissingDetails().Where(detail => detail != PlayerDetail.Photo).ToList();
        if (otherMissing.Count > 0)
        {
            lines.Add(BotTexts.FormatProgressStillMissing(string.Join(", ", otherMissing)));
        }

        lines.Add(player.IsPublished ? BotTexts.ProgressPublished : player.IsComplete ? BotTexts.ProgressWaitingAdmin : BotTexts.ProgressWaitingDetails);
        return string.Join('\n', lines);
    }
}

/// <summary>A player progress message, tracked for editing.</summary>
public sealed class ProgressMessageByPlayerSpec : Specification<ProgressMessage>, ISingleResultSpecification<ProgressMessage>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="playerId">The player.</param>
    public ProgressMessageByPlayerSpec(Guid playerId) => Query.Where(message => message.PlayerId == playerId);
}

/// <summary>
/// Keeps each player's one progress message current (T-6a). After every change the player shows (created, details or
/// photo changed, folder added, published), it renders the text again; the first time it sends the message to the
/// owning analyst, afterwards it edits that same message, and only when the text changed. A message that cannot be
/// edited any more (deleted in Telegram) is sent again. Telegram trouble is logged and never fails the request: the
/// change itself is already saved.
/// </summary>
/// <param name="players">The players.</param>
/// <param name="folders">The linked folders.</param>
/// <param name="users">The team.</param>
/// <param name="messages">The progress messages.</param>
/// <param name="unitOfWork">Saves the message ids.</param>
/// <param name="bot">The messenger.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class PlayerProgressUpdater(
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> folders,
    IRepository<User, Guid> users,
    IRepository<ProgressMessage, Guid> messages,
    IUnitOfWork unitOfWork,
    ITelegramMessenger bot,
    TimeProvider timeProvider,
    ILogger<PlayerProgressUpdater> logger)
    : IBackgroundEventHandler<PlayerCreated>, IBackgroundEventHandler<PlayerDetailsChanged>, IBackgroundEventHandler<PlayerPublished>, IBackgroundEventHandler<DriveFolderLinked>
{
    /// <summary>Sends the first progress message.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(PlayerCreated domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the message after a detail or the photo changed.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(PlayerDetailsChanged domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the message after an admin published the player.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(PlayerPublished domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the message after a Drive folder was added.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(DriveFolderLinked domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Renders the progress and sends or edits the message when the text changed.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current, or the failure is logged.</returns>
    private async Task RefreshAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            if (await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(playerId), cancellationToken) is not { AnalystId: { } ownerId } player
                || await users.FirstOrDefaultAsync(new UserByIdReadOnlySpec(ownerId), cancellationToken) is not { IsActive: true } owner)
            {
                return;
            }

            var text = PlayerProgressText.Render(player, await folders.CountAsync(new PlayerFoldersSpec(playerId, 1), cancellationToken));
            var now = timeProvider.GetUtcNow();
            var message = await messages.FirstOrDefaultAsync(new ProgressMessageByPlayerSpec(playerId), cancellationToken);
            if (message is null)
            {
                var messageId = await bot.SendAsync(owner.TelegramId, text, null, cancellationToken);
                messages.Add(ProgressMessage.Start(playerId, owner.TelegramId, messageId, text, now));
            }
            else if (message.Shows(text))
            {
                return;
            }
            else
            {
                await EditOrResendAsync(message, owner.TelegramId, text, now, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, playerId);
        }
    }

    /// <summary>Edits the message in place, or sends it again when Telegram cannot edit it any more.</summary>
    /// <param name="message">The progress message.</param>
    /// <param name="chatId">The owner chat.</param>
    /// <param name="text">The new text.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns>A task that completes when Telegram shows the text.</returns>
    private async Task EditOrResendAsync(ProgressMessage message, long chatId, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await bot.EditAsync(message.ChatId, message.MessageId, text, null, cancellationToken);
            message.Edited(text, now);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogResending(logger, exception, message.PlayerId);
            message.Resent(await bot.SendAsync(chatId, text, null, cancellationToken), text, now);
        }
    }

    /// <summary>Logs an edit that failed, before sending the message again.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="PlayerId">The player.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Progress message of player {PlayerId} could not be edited; sending it again")]
    private static partial void LogResending(ILogger logger, Exception exception, Guid PlayerId);

    /// <summary>Logs a progress update that could not reach Telegram (the change itself is saved).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="PlayerId">The player.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress message of player {PlayerId} could not be sent or updated")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid PlayerId);
}
