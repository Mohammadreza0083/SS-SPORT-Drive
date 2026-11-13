using System.Globalization;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Digest;

/// <summary>The day a digest covers, as UTC instants: midnight to midnight of yesterday in the notification time zone (Tehran).</summary>
/// <param name="Day">The local date.</param>
/// <param name="Start">When the day starts (inclusive).</param>
/// <param name="End">When the day ends (exclusive).</param>
public sealed record DigestWindow(DateOnly Day, DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>The window of the local day before <paramref name="now"/>.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="zone">The time zone whose calendar counts (Tehran).</param>
    /// <returns>The window.</returns>
    public static DigestWindow Yesterday(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var day = today.AddDays(-1);
        return new DigestWindow(day, StartOf(day, zone), StartOf(today, zone));
    }

    /// <summary>The UTC instant of local midnight at the start of a date.</summary>
    /// <param name="date">The local date.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The instant.</returns>
    private static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
}

/// <summary>One of the day's most watched players.</summary>
/// <param name="Name">The player's name.</param>
/// <param name="WatchMinutes">The minutes viewers watched them.</param>
public sealed record DigestTopPlayer(string Name, int WatchMinutes);

/// <summary>Everything the digest says about one day.</summary>
public sealed record DigestFacts
{
    /// <summary>Gets the day the digest covers.</summary>
    public DateOnly Day { get; init; }

    /// <summary>Gets how many links were opened.</summary>
    public int LinksOpened { get; init; }

    /// <summary>Gets how many different viewers opened links.</summary>
    public int Viewers { get; init; }

    /// <summary>Gets how many plays there were.</summary>
    public int Plays { get; init; }

    /// <summary>Gets the minutes watched.</summary>
    public int WatchMinutes { get; init; }

    /// <summary>Gets the most watched players, most minutes first (at most three are shown).</summary>
    public IReadOnlyList<DigestTopPlayer> TopPlayers { get; init; } = [];

    /// <summary>Gets the forwards still waiting for Keep or Block.</summary>
    public int PendingForwards { get; init; }

    /// <summary>Gets the players on the waiting list.</summary>
    public int WaitingPlayers { get; init; }

    /// <summary>Gets the open access requests, or <see langword="null"/> until requests exist (P-4).</summary>
    public int? OpenAccessRequests { get; init; }

    /// <summary>Gets the emails that failed after every retry.</summary>
    public int FailedEmails { get; init; }

    /// <summary>Gets a value indicating whether anyone watched anything: links opened, viewers, plays or minutes. Waiting items alone are not activity.</summary>
    public bool HasActivity => LinksOpened > 0 || Viewers > 0 || Plays > 0 || WatchMinutes > 0;
}

/// <summary>Reads the day's numbers for the digest in a few SQL counts, never by loading rows.</summary>
public interface IDigestFactsReader
{
    /// <summary>Reads the facts of a window.</summary>
    /// <param name="window">The day.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The facts.</returns>
    Task<DigestFacts> ReadAsync(DigestWindow window, CancellationToken cancellationToken);
}

/// <summary>Puts the digest's facts into the lines the admins read. All wording lives in <c>BotTexts.resx</c>.</summary>
public static class DailyDigestText
{
    /// <summary>How many top players are shown.</summary>
    private const int TopPlayersShown = 3;

    /// <summary>Renders the digest.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The message, one line per part.</returns>
    public static string Render(DigestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var lines = new List<string>
        {
            BotTexts.FormatDigestHeader(facts.Day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture)),
            facts.HasActivity
                ? BotTexts.FormatDigestActivity(
                    Count(facts.LinksOpened, BotTexts.FormatDigestLinksOpenedOne, BotTexts.FormatDigestLinksOpenedMany),
                    Count(facts.Viewers, BotTexts.FormatDigestViewersOne, BotTexts.FormatDigestViewersMany),
                    Count(facts.Plays, BotTexts.FormatDigestPlaysOne, BotTexts.FormatDigestPlaysMany),
                    BotTexts.FormatDigestMinutes(facts.WatchMinutes))
                : BotTexts.DigestNoActivity,
        };

        if (facts.TopPlayers.Count > 0)
        {
            lines.Add(BotTexts.FormatDigestTopPlayers(string.Join(
                " · ",
                facts.TopPlayers.Take(TopPlayersShown).Select(player => BotTexts.FormatDigestTopPlayer(player.Name, player.WatchMinutes)))));
        }

        var waiting = new List<string>();
        AddWaiting(waiting, facts.PendingForwards, BotTexts.FormatDigestForwardsOne, BotTexts.FormatDigestForwardsMany);
        AddWaiting(waiting, facts.WaitingPlayers, BotTexts.FormatDigestWaitingPlayersOne, BotTexts.FormatDigestWaitingPlayersMany);
        AddWaiting(waiting, facts.OpenAccessRequests ?? 0, BotTexts.FormatDigestAccessRequestsOne, BotTexts.FormatDigestAccessRequestsMany);
        lines.Add(waiting.Count == 0 ? BotTexts.DigestNothingWaiting : BotTexts.FormatDigestWaiting(string.Join(" · ", waiting)));

        if (facts.FailedEmails > 0)
        {
            lines.Add(Count(facts.FailedEmails, BotTexts.FormatDigestFailedEmailsOne, BotTexts.FormatDigestFailedEmailsMany));
        }

        return string.Join('\n', lines);
    }

    /// <summary>Words a count in the singular for one and the plural otherwise.</summary>
    /// <param name="count">The count.</param>
    /// <param name="one">The singular text.</param>
    /// <param name="many">The plural text.</param>
    /// <returns>The text.</returns>
    private static string Count(int count, Func<object[], string> one, Func<object[], string> many) => (count == 1 ? one : many)([count]);

    /// <summary>Adds a waiting part when there is something waiting.</summary>
    /// <param name="parts">The parts so far.</param>
    /// <param name="count">The count.</param>
    /// <param name="one">The singular text.</param>
    /// <param name="many">The plural text.</param>
    private static void AddWaiting(List<string> parts, int count, Func<object[], string> one, Func<object[], string> many)
    {
        if (count > 0)
        {
            parts.Add(Count(count, one, many));
        }
    }
}

/// <summary>Sends the daily digest to every active admin (<c>daily-digest</c> job, 09:00 Tehran).</summary>
/// <param name="Now">The current time.</param>
[InternalRequest("Sent only by the scheduled daily-digest job with the clock's time.")]
public sealed record SendDailyDigestCommand(DateTimeOffset Now) : ICommand<int>;

/// <summary>
/// Handles <see cref="SendDailyDigestCommand"/>: reads yesterday's facts and messages each active admin at once. The
/// digest is not held by quiet hours or the throttle, and has no buttons. One admin's Telegram failure is logged and
/// does not stop the others.
/// </summary>
/// <param name="facts">Reads the day's numbers.</param>
/// <param name="users">The users.</param>
/// <param name="bot">The messenger.</param>
/// <param name="settings">The notification rules, for the time zone.</param>
/// <param name="logger">The logger.</param>
public sealed partial class SendDailyDigestCommandHandler(
    IDigestFactsReader facts,
    IRepository<User, Guid> users,
    ITelegramMessenger bot,
    INotificationSettings settings,
    ILogger<SendDailyDigestCommandHandler> logger) : ICommandHandler<SendDailyDigestCommand, int>
{
    /// <summary>Reads the facts, renders the digest and sends it.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many admins got it.</returns>
    public async Task<int> Handle(SendDailyDigestCommand request, CancellationToken cancellationToken)
    {
        var window = DigestWindow.Yesterday(request.Now, settings.Rules.TimeZone);
        var text = DailyDigestText.Render(await facts.ReadAsync(window, cancellationToken));
        var sent = 0;
        foreach (var admin in await users.ListAsync(new ActiveAdminsSpec(), cancellationToken))
        {
            try
            {
                await bot.SendAsync(admin.TelegramId, text, null, cancellationToken);
                sent++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSendFailed(logger, admin.Id, exception);
            }
        }

        return sent;
    }

    /// <summary>Logs a digest that could not be sent to one admin.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="UserId">The admin.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "The daily digest could not be sent to admin {UserId}")]
    private static partial void LogSendFailed(ILogger logger, Guid UserId, Exception exception);
}
