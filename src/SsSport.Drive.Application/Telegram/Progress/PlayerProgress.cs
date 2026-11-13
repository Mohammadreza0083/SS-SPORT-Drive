using Ardalis.Specification;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Abstractions.Web;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Domain.Assets;
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

    /// <summary>
    /// Renders the progress of a player with its build (T-6b): the T-6a lines, then the Copy / Transcode / Pages lines,
    /// the failed files, the progress bar with the time left while building, and the result card once the player is
    /// published and every file is done.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="folderCount">How many Drive folders are linked.</param>
    /// <param name="build">The build of its files (<see cref="BuildProgress.None"/> without files).</param>
    /// <param name="remaining">The estimated time left, or <see langword="null"/> when unknown.</param>
    /// <param name="playerPage">The player's public page, or <see langword="null"/> when it cannot be built.</param>
    /// <returns>The message text.</returns>
    public static string Render(Player player, int folderCount, BuildProgress build, TimeSpan? remaining, Uri? playerPage)
    {
        ArgumentNullException.ThrowIfNull(build);
        var text = Render(player, folderCount);
        if (build.Files == 0)
        {
            return text;
        }

        var lines = new List<string> { text, BotTexts.FormatProgressCopy(Tick(build.CopyDone), build.Copied, build.Files) };
        if (build.Videos > 0)
        {
            lines.Add(BotTexts.FormatProgressTranscode(Tick(build.VideosDone), build.VideosReady, build.Videos));
        }

        if (build.Documents > 0)
        {
            lines.Add(BotTexts.FormatProgressPages(Tick(build.DocumentsDone), build.DocumentsReady, build.Documents));
        }

        if (build.Failed > 0)
        {
            lines.Add(build.Failed == 1 ? BotTexts.ProgressFailedOne : BotTexts.FormatProgressFailedMany(build.Failed));
        }

        if (build.IsBuilding)
        {
            var filled = Math.Clamp(build.Percent / 10, 0, 10);
            var bar = new string('▓', filled) + new string('░', 10 - filled);
            lines.Add(BotTexts.FormatProgressBar(bar, build.Percent, remaining is { } left ? TimeLeft(left) : BotTexts.ProgressEstimating));
        }
        else if (player.IsPublished && build.VideosReady + build.DocumentsReady > 0)
        {
            lines.Add(BotTexts.FormatProgressLive(string.Join(" · ", LiveParts(build))));
            if (playerPage is not null)
            {
                lines.Add(BotTexts.FormatProgressLink(playerPage.AbsoluteUri));
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>Picks the tick for a step: done (✅) or still going (⏳).</summary>
    /// <param name="done">Whether the step is done.</param>
    /// <returns>The tick.</returns>
    private static string Tick(bool done) => done ? "✅" : "⏳";

    /// <summary>Words the time left, rounded up to whole minutes.</summary>
    /// <param name="remaining">The time left.</param>
    /// <returns>For example "about 13 min left" or "about 1 h 5 min left".</returns>
    private static string TimeLeft(TimeSpan remaining)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0)
        {
            return BotTexts.FormatProgressTimeLeftMinutes(rest);
        }

        return rest == 0 ? BotTexts.FormatProgressTimeLeftHours(hours) : BotTexts.FormatProgressTimeLeftHoursMinutes(hours, rest);
    }

    /// <summary>Lists what is live on the result card: ready videos and ready documents, leaving out a kind with none.</summary>
    /// <param name="build">The finished build.</param>
    /// <returns>The parts, in order.</returns>
    private static IEnumerable<string> LiveParts(BuildProgress build)
    {
        if (build.VideosReady > 0)
        {
            yield return build.VideosReady == 1 ? BotTexts.ProgressLiveVideoOne : BotTexts.FormatProgressLiveVideoMany(build.VideosReady);
        }

        if (build.DocumentsReady > 0)
        {
            yield return build.DocumentsReady == 1 ? BotTexts.ProgressLiveDocumentOne : BotTexts.FormatProgressLiveDocumentMany(build.DocumentsReady);
        }
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
/// <param name="builds">Where each file of a player is in the pipeline (T-6b).</param>
/// <param name="webLinks">Builds the public page of a player for the result card (T-6b).</param>
/// <param name="storage">Holds the QR code of a player (T-6b).</param>
/// <param name="unitOfWork">Saves the message ids.</param>
/// <param name="bot">The messenger.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class PlayerProgressUpdater(
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> folders,
    IRepository<User, Guid> users,
    IRepository<ProgressMessage, Guid> messages,
    IPlayerBuildReader builds,
    IWebLinks webLinks,
    IObjectStorage storage,
    IUnitOfWork unitOfWork,
    ITelegramMessenger bot,
    TimeProvider timeProvider,
    ILogger<PlayerProgressUpdater> logger)
    : IBackgroundEventHandler<PlayerCreated>, IBackgroundEventHandler<PlayerDetailsChanged>, IBackgroundEventHandler<PlayerPublished>, IBackgroundEventHandler<DriveFolderLinked>,
      IBackgroundEventHandler<AssetAdded>, IBackgroundEventHandler<AssetProgressed>, IBackgroundEventHandler<AssetReady>,
      IBackgroundEventHandler<AssetFailed>, IBackgroundEventHandler<AssetReplaced>, IBackgroundEventHandler<AssetHidden>
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

    /// <summary>Updates the build lines after a file was found (T-6b).</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(AssetAdded domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the build lines after a file moved a step in its pipeline (T-6b).</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(AssetProgressed domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the build lines after a file finished (T-6b).</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(AssetReady domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the build lines after a file failed (T-6b).</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(AssetFailed domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the build lines after a file was replaced by a new version (T-6b).</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(AssetReplaced domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

    /// <summary>Updates the build lines after a file was hidden or removed from Drive (T-6b).</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (unused).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the message is current.</returns>
    public Task HandleAsync(AssetHidden domainEvent, EventContext context, CancellationToken cancellationToken) => RefreshAsync(domainEvent.PlayerId, cancellationToken);

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

            var folderCount = await folders.CountAsync(new PlayerFoldersSpec(playerId, 1), cancellationToken);
            var build = BuildProgress.From(await builds.ReadAsync(playerId, cancellationToken));
            var now = timeProvider.GetUtcNow();
            var message = await messages.FirstOrDefaultAsync(new ProgressMessageByPlayerSpec(playerId), cancellationToken);
            var buildStart = message?.BuildStartedAt ?? (build.IsBuilding ? now : null);
            var text = PlayerProgressText.Render(player, folderCount, build, build.EstimateRemaining(buildStart, now), PublicPage(player));
            var changed = false;
            if (message is null)
            {
                var messageId = await bot.SendAsync(owner.TelegramId, text, null, cancellationToken);
                message = ProgressMessage.Start(playerId, owner.TelegramId, messageId, text, now);
                message.TrackBuild(build.IsBuilding, now);
                messages.Add(message);
                changed = true;
            }
            else
            {
                changed = message.TrackBuild(build.IsBuilding, now);
                if (!message.Shows(text))
                {
                    await EditOrResendAsync(message, owner.TelegramId, text, now, cancellationToken);
                    changed = true;
                }
            }

            if (changed)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (player.IsPublished && build.IsFinished && player.QrCode is { } qrCode && message.QrSentAt is null)
            {
                await SendQrCodeAsync(message, player, qrCode.Value, now, cancellationToken);
            }
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

    /// <summary>Gets the public page of a player for the result card.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The address, or <see langword="null"/> when the player is not published, has no slug or the public address is not set.</returns>
    private Uri? PublicPage(Player player)
    {
        if (!player.IsPublished || player.Slug is not { } slug)
        {
            return null;
        }

        try
        {
            return webLinks.PlayerPage(slug.Value);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends the QR code of a player as a reply to the progress message, once. A missing image or a Telegram failure is
    /// logged and tried again on the next refresh; it never fails the text update.
    /// </summary>
    /// <param name="message">The progress message the QR code replies to.</param>
    /// <param name="player">The player.</param>
    /// <param name="qrKey">The storage key of the QR code PNG.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns>A task that completes when the QR code is sent and recorded, or the failure is logged.</returns>
    private async Task SendQrCodeAsync(ProgressMessage message, Player player, string qrKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await storage.GetAsync(qrKey, cancellationToken);
            if (stored is null)
            {
                LogQrMissing(logger, player.Id);
                return;
            }

            using var bytes = new MemoryStream();
            await using (stored.Content)
            {
                await stored.Content.CopyToAsync(bytes, cancellationToken);
            }

            var name = player.Name?.Value ?? BotTexts.ProgressUnnamed;
            await bot.SendPhotoAsync(message.ChatId, message.MessageId, bytes.ToArray(), BotTexts.FormatProgressQrCaption(name), cancellationToken);
            message.QrSent(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogQrFailed(logger, exception, player.Id);
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

    /// <summary>Logs a QR code that is not in storage yet (tried again on the next refresh).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="PlayerId">The player.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "QR code of player {PlayerId} is not in storage")]
    private static partial void LogQrMissing(ILogger logger, Guid PlayerId);

    /// <summary>Logs a QR code that could not be sent (tried again on the next refresh).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="PlayerId">The player.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "QR code of player {PlayerId} could not be sent")]
    private static partial void LogQrFailed(ILogger logger, Exception exception, Guid PlayerId);
}
