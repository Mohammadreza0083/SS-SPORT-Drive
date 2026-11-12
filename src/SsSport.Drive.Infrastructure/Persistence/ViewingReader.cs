using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Tracking;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Reads how a link was watched (L-6): one row per viewer and video. The groups are counted and paged in SQL, newest viewing
/// first; only the page's watches are then loaded, to merge their ranges into the unique seconds and the completion.
/// </summary>
/// <param name="context">The database context.</param>
public sealed class ViewingReader(DbContext context) : IViewingReader
{
    /// <inheritdoc />
    public async Task<PagedResult<ViewingRowDto>> PageAsync(Guid shareLinkId, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var watches = context.Set<WatchSession>().AsNoTracking().Where(watch => watch.ShareLinkId == shareLinkId);
        var groups = watches
            .GroupBy(watch => new { watch.Email, watch.AssetId })
            .Select(group => new { group.Key.Email, group.Key.AssetId, LastSeenAt = group.Max(watch => watch.LastSeenAt) });

        var totalCount = await groups.CountAsync(cancellationToken);
        var page = await groups
            .OrderByDescending(group => group.LastSeenAt)
            .ThenBy(group => group.AssetId)
            .ThenBy(group => group.Email)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var emails = page.Select(group => group.Email).Distinct().ToList();
        var assets = page.Select(group => group.AssetId).Distinct().ToList();
        var rows = await watches
            .Where(watch => emails.Contains(watch.Email) && assets.Contains(watch.AssetId))
            .ToListAsync(cancellationToken);

        var items = page.Select(group =>
        {
            var own = rows.Where(watch => watch.Email == group.Email && watch.AssetId == group.AssetId).ToList();
            var ranges = own.SelectMany(watch => watch.Ranges).ToList();
            var duration = own.Max(watch => watch.DurationSeconds);
            return new ViewingRowDto
            {
                Email = group.Email.Value,
                PlayerId = own[0].PlayerId,
                AssetId = group.AssetId,
                Plays = own.Sum(watch => watch.Plays),
                WatchSeconds = WatchMetrics.WatchSeconds(ranges),
                UniqueSeconds = WatchMetrics.UniqueSeconds(ranges),
                Completion = WatchMetrics.Completion(ranges, duration),
                IsComplete = WatchMetrics.IsComplete(ranges, duration),
                LastSeenAt = group.LastSeenAt,
            };
        }).ToList();

        return PagedResult<ViewingRowDto>.Create(items, request, totalCount);
    }
}
