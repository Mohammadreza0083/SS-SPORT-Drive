using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.Telegram;

/// <summary>A received Telegram update id; a row in <c>telegram_updates</c>, used only to skip re-deliveries.</summary>
/// <param name="UpdateId">Telegram's <c>update_id</c>.</param>
/// <param name="ReceivedAt">When it arrived (UTC).</param>
public sealed record TelegramUpdateRecord(long UpdateId, DateTimeOffset ReceivedAt);

/// <summary>
/// Records update ids with <c>INSERT … ON CONFLICT DO NOTHING</c>, so two concurrent deliveries of the same update
/// cannot both be accepted. The write is immediate and does not wait for the unit of work.
/// </summary>
/// <param name="context">The application context.</param>
public sealed class TelegramUpdateInbox(AppDbContext context) : ITelegramUpdateInbox
{
    /// <summary>Records the id unless it is already there.</summary>
    /// <param name="updateId">Telegram's <c>update_id</c>.</param>
    /// <param name="receivedAt">When it arrived (UTC).</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the id is new.</returns>
    public async Task<bool> TryRecordAsync(long updateId, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        var inserted = await context.Database.ExecuteSqlAsync(
            $"INSERT INTO telegram_updates (update_id, received_at) VALUES ({updateId}, {receivedAt}) ON CONFLICT (update_id) DO NOTHING",
            cancellationToken);
        return inserted == 1;
    }
}
