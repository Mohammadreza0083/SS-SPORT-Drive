using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Telegram.Digest;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads the admins' daily digest numbers (L-7) in a few parameterized, read-only SQL counts: nothing is loaded but the
/// counts. Viewing numbers come from the viewer sessions that started in the window; what is waiting on the admins is
/// counted as it stands now.
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

        // L-7 7b: plays, watch minutes, top players and failed emails come from L-5/L-6 and L-4 once they are merged.
        return new DigestFacts
        {
            Day = window.Day,
            LinksOpened = sessions.LinksOpened,
            Viewers = sessions.Viewers,
            PendingForwards = pendingForwards,
            WaitingPlayers = waitingPlayers,
            OpenAccessRequests = null,
        };
    }

    /// <summary>The two counts of the sessions query.</summary>
    /// <param name="LinksOpened">Distinct links opened.</param>
    /// <param name="Viewers">Distinct viewer emails.</param>
    private sealed record SessionCounts(int LinksOpened, int Viewers);
}
