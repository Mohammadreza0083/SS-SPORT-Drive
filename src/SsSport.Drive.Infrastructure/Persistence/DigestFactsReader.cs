using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Telegram.Digest;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads the admins' daily digest numbers (L-7) in a few parameterized, read-only SQL counts: nothing is loaded but the
/// counts. Links opened and viewers come from the viewer sessions that started in the window; plays, minutes and top
/// players from the watches that started in it; failed emails from the emails whose last attempt fell in it. What is
/// waiting on the admins is counted as it stands now.
/// </summary>
/// <param name="context">The database context.</param>
public sealed class DigestFactsReader(DbContext context) : IDigestFactsReader
{
    /// <inheritdoc />
    public async Task<DigestFacts> ReadAsync(DigestWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        var start = window.Start;
        var end = window.End;

        // distinct links and distinct emails of the sessions that started yesterday (Tehran)
        var sessions = await context.Database.SqlQuery<SessionCounts>(
            $"""
            SELECT count(DISTINCT share_link_id)::int AS links_opened, count(DISTINCT email)::int AS viewers
            FROM viewer_sessions
            WHERE created_at >= {start} AND created_at < {end}
            """).SingleAsync(cancellationToken);
        var pendingForwards = await context.Database.SqlQuery<int>(
            $"""SELECT count(*)::int AS "Value" FROM link_forwards WHERE status = 'Pending'""").SingleAsync(cancellationToken);
        var waitingPlayers = await context.Database.SqlQuery<int>(
            $"""SELECT count(*)::int AS "Value" FROM players WHERE status = 'Waiting'""").SingleAsync(cancellationToken);

        // A watch counts on the Tehran day it started. Its seconds are summed in a subquery first, so a watch with many
        // ranges still counts its plays once. Watch time is every second played (L-6), overlaps counted twice.
        var watching = await context.Database.SqlQuery<WatchingCounts>(
            $"""
            WITH day AS (
                SELECT w.plays,
                       COALESCE((SELECT sum((r->>'to')::float8 - (r->>'from')::float8) FROM jsonb_array_elements(w.ranges) AS r), 0) AS seconds
                FROM watch_sessions w
                WHERE w.started_at >= {start} AND w.started_at < {end})
            SELECT COALESCE(sum(plays), 0)::int AS plays,
                   round(COALESCE(sum(seconds), 0)::numeric / 60)::int AS watch_minutes
            FROM day
            """).SingleAsync(cancellationToken);
        var topPlayers = await context.Database.SqlQuery<TopPlayerRow>(
            $"""
            WITH day AS (
                SELECT w.player_id,
                       COALESCE((SELECT sum((r->>'to')::float8 - (r->>'from')::float8) FROM jsonb_array_elements(w.ranges) AS r), 0) AS seconds
                FROM watch_sessions w
                WHERE w.started_at >= {start} AND w.started_at < {end})
            SELECT p.full_name AS name, round(sum(day.seconds)::numeric / 60)::int AS watch_minutes
            FROM day
            JOIN players p ON p.id = day.player_id
            GROUP BY p.id, p.full_name
            HAVING sum(day.seconds) > 0
            ORDER BY sum(day.seconds) DESC, p.full_name
            LIMIT 3
            """).ToListAsync(cancellationToken);

        // Emails that failed for good: their last attempt, the one that failed them, fell on the day.
        var failedEmails = await context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value" FROM outgoing_emails
            WHERE status = 'Failed' AND last_attempt_at >= {start} AND last_attempt_at < {end}
            """).SingleAsync(cancellationToken);

        return new DigestFacts
        {
            Day = window.Day,
            LinksOpened = sessions.LinksOpened,
            Viewers = sessions.Viewers,
            Plays = watching.Plays,
            WatchMinutes = watching.WatchMinutes,
            TopPlayers = topPlayers.Select(row => new DigestTopPlayer(row.Name, row.WatchMinutes)).ToList(),
            PendingForwards = pendingForwards,
            WaitingPlayers = waitingPlayers,
            FailedEmails = failedEmails,
            OpenAccessRequests = null,
        };
    }

    /// <summary>The two counts of the sessions query.</summary>
    /// <param name="LinksOpened">Distinct links opened.</param>
    /// <param name="Viewers">Distinct viewer emails.</param>
    private sealed record SessionCounts(int LinksOpened, int Viewers);

    /// <summary>The two numbers of the watching query.</summary>
    /// <param name="Plays">Plays of the watches that started in the window.</param>
    /// <param name="WatchMinutes">Minutes played, rounded to the nearest minute.</param>
    private sealed record WatchingCounts(int Plays, int WatchMinutes);

    /// <summary>One row of the top players query.</summary>
    /// <param name="Name">The player's name.</param>
    /// <param name="WatchMinutes">The minutes watched, rounded.</param>
    private sealed record TopPlayerRow(string Name, int WatchMinutes);
}
