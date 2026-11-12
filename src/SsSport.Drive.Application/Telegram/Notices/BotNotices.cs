using System.Text;
using Ardalis.Specification;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Domain.Notifications;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Notices;

/// <summary>The notification rules in force (T-8); Infrastructure reads them from the <c>Notifications</c> settings.</summary>
public interface INotificationSettings
{
    /// <summary>Gets the rules: the throttle and the quiet hours.</summary>
    NotificationRules Rules { get; }
}

/// <summary>
/// Turns held notices into bot messages (T-8): a single notice is sent as written; several get a header with their
/// count and are grouped by player, in the order the players first came up. Telegram's 4096-character limit splits a
/// long digest between lines, never inside one.
/// </summary>
public static class NoticeDigest
{
    /// <summary>Telegram's limit for a message text.</summary>
    private const int MaxMessageLength = 4096;

    /// <summary>Renders the notices as one or more message texts.</summary>
    /// <param name="notices">The notices, oldest first.</param>
    /// <returns>The texts to send, in order; empty when there are no notices.</returns>
    public static IReadOnlyList<string> Render(IReadOnlyList<PendingNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (notices.Count <= 1)
        {
            return notices.Select(notice => notice.Text).ToList();
        }

        var lines = notices
            .Select((notice, index) => (notice, index))
            .GroupBy(item => item.notice.PlayerId ?? Guid.Empty)
            .OrderBy(group => group.Min(item => item.index))
            .SelectMany(group => group.OrderBy(item => item.index).Select(item => item.notice.Text));

        var messages = new List<string>();
        var current = new StringBuilder(BotTexts.FormatNoticeDigestHeader(notices.Count));
        foreach (var line in lines)
        {
            if (current.Length + 1 + line.Length > MaxMessageLength)
            {
                messages.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line);
        }

        messages.Add(current.ToString());
        return messages;
    }
}

/// <summary>A recipient's notice box, tracked, with its held notices.</summary>
public sealed class NoticeBoxByUserSpec : Specification<NoticeBox>, ISingleResultSpecification<NoticeBox>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="userId">The recipient.</param>
    public NoticeBoxByUserSpec(Guid userId) => Query.Where(box => box.UserId == userId).Include(box => box.Notices);
}

/// <summary>One page of notice boxes that are due, tracked, the longest-waiting first (decisions §2b: paged in SQL).</summary>
public sealed class DueNoticeBoxesSpec : Specification<NoticeBox>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="take">The page size.</param>
    public DueNoticeBoxesSpec(DateTimeOffset now, int take) =>
        Query.Where(box => box.DueAt != null && box.DueAt <= now)
            .Include(box => box.Notices)
            .OrderBy(box => box.DueAt)
            .ThenBy(box => box.Id)
            .Take(take);
}

/// <summary>Holds bot notices under the notification rules and sends them when they are due (T-8).</summary>
public interface IBotNoticeOutbox
{
    /// <summary>
    /// Holds a notice for a team member and sends it at once when the rules allow; otherwise it waits for
    /// <see cref="SendDueNoticesCommand"/>. Saves the box.
    /// </summary>
    /// <param name="recipient">The team member.</param>
    /// <param name="kind">What it is about.</param>
    /// <param name="playerId">The player, or <see langword="null"/>.</param>
    /// <param name="text">The line to send.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the notice is held or sent.</returns>
    Task QueueAsync(User recipient, NoticeKind kind, Guid? playerId, string text, CancellationToken cancellationToken);

    /// <summary>Sends a box's notices as one grouped message when it is due; on a Telegram failure tries again later.</summary>
    /// <param name="box">The box (tracked; the caller saves it).</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the sends.</param>
    /// <returns><see langword="true"/> when a message went out.</returns>
    Task<bool> SendIfDueAsync(NoticeBox box, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>The notice outbox on the generic repository and the bot messenger (T-8).</summary>
/// <param name="boxes">The notice boxes.</param>
/// <param name="unitOfWork">Saves the boxes.</param>
/// <param name="bot">The messenger.</param>
/// <param name="settings">The notification rules.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class BotNoticeOutbox(
    IRepository<NoticeBox, Guid> boxes,
    IUnitOfWork unitOfWork,
    ITelegramMessenger bot,
    INotificationSettings settings,
    TimeProvider timeProvider,
    ILogger<BotNoticeOutbox> logger) : IBotNoticeOutbox
{
    /// <inheritdoc />
    public async Task QueueAsync(User recipient, NoticeKind kind, Guid? playerId, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var now = timeProvider.GetUtcNow();
        var box = await boxes.FirstOrDefaultAsync(new NoticeBoxByUserSpec(recipient.Id), cancellationToken);
        if (box is null)
        {
            box = NoticeBox.Open(recipient.Id, recipient.TelegramId, now);
            boxes.Add(box);
        }

        box.Queue(kind, playerId, text, now, settings.Rules);
        if (!box.IsDue(now))
        {
            LogHeld(logger, recipient.Id, kind, box.DueAt);
        }

        await SendIfDueAsync(box, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendIfDueAsync(NoticeBox box, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (!box.IsDue(now))
        {
            return false;
        }

        try
        {
            foreach (var message in NoticeDigest.Render(box.Notices))
            {
                await bot.SendAsync(box.ChatId, message, null, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var retryAt = now + settings.Rules.Throttle;
            box.Postpone(retryAt);
            LogSendFailed(logger, exception, box.UserId, box.Notices.Count, retryAt);
            return false;
        }

        var count = box.TakeDue(now).Count;
        LogSent(logger, box.UserId, count);
        return true;
    }

    /// <summary>Logs a notice held by the rules.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="UserId">The recipient.</param>
    /// <param name="Kind">What it is about.</param>
    /// <param name="DueAt">When it may go out.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Bot notice {Kind} for user {UserId} held until {DueAt}")]
    private static partial void LogHeld(ILogger logger, Guid UserId, NoticeKind Kind, DateTimeOffset? DueAt);

    /// <summary>Logs a notice message sent.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="UserId">The recipient.</param>
    /// <param name="NoticeCount">How many notices it carried.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Sent {NoticeCount} bot notice(s) to user {UserId}")]
    private static partial void LogSent(ILogger logger, Guid UserId, int NoticeCount);

    /// <summary>Logs a notice message Telegram did not take; the notices stay held.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="UserId">The recipient.</param>
    /// <param name="NoticeCount">How many notices are held.</param>
    /// <param name="RetryAt">When it is tried again.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Bot notices for user {UserId} could not be sent ({NoticeCount} held); retrying at {RetryAt}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid UserId, int NoticeCount, DateTimeOffset RetryAt);
}

/// <summary>
/// Sends every held bot notice that is due now (T-8). A recurring background job sends this every minute; it pages
/// through the due boxes in SQL and saves after each one, so a crash never sends a box twice.
/// </summary>
public sealed record SendDueNoticesCommand : ICommand<int>;

/// <summary>Handles <see cref="SendDueNoticesCommand"/>.</summary>
/// <param name="boxes">The notice boxes.</param>
/// <param name="users">The team.</param>
/// <param name="outbox">Sends a due box.</param>
/// <param name="unitOfWork">Saves each box.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class SendDueNoticesCommandHandler(
    IRepository<NoticeBox, Guid> boxes,
    IRepository<User, Guid> users,
    IBotNoticeOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<SendDueNoticesCommandHandler> logger) : ICommandHandler<SendDueNoticesCommand, int>
{
    /// <summary>How many boxes one page holds.</summary>
    private const int PageSize = 50;

    /// <summary>The most pages one round handles; the rest wait for the next round.</summary>
    private const int MaxPages = 20;

    /// <summary>Sends the due boxes; drops the notices of members who left the team.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many messages went out.</returns>
    public async Task<int> Handle(SendDueNoticesCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var sent = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var due = await boxes.ListAsync(new DueNoticeBoxesSpec(now, PageSize), cancellationToken);
            foreach (var box in due)
            {
                if (await users.FirstOrDefaultAsync(new UserByIdReadOnlySpec(box.UserId), cancellationToken) is not { IsActive: true })
                {
                    LogDiscarded(logger, box.UserId, box.Notices.Count);
                    box.Discard();
                }
                else if (await outbox.SendIfDueAsync(box, now, cancellationToken))
                {
                    sent++;
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (due.Count < PageSize)
            {
                break;
            }
        }

        return sent;
    }

    /// <summary>Logs notices dropped because the recipient is no longer an active member.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="UserId">The former recipient.</param>
    /// <param name="NoticeCount">How many notices were dropped.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped {NoticeCount} held bot notice(s) of inactive user {UserId}")]
    private static partial void LogDiscarded(ILogger logger, Guid UserId, int NoticeCount);
}
