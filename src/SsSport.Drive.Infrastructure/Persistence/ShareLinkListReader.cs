using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IShareLinkListReader"/>. The links are joined to their recipients, filtered,
/// counted, ordered and paged in PostgreSQL; the names of the players on that one page are then read in a second query.
/// Nothing is tracked.
/// </summary>
/// <param name="context">The request's EF Core context.</param>
public sealed class ShareLinkListReader(DbContext context) : IShareLinkListReader
{
    /// <summary>The column behind <see cref="ShareLink.PlayerIds"/>.</summary>
    private const string PlayerIdsField = "_playerIds";

    /// <summary>Reads one page of links, newest first (the id breaks ties), and the total.</summary>
    /// <param name="criteria">The status, player and recipient filters.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="now">The moment each link's access is judged at.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<PagedResult<ShareLinkListItemDto>> PageAsync(ShareLinkListCriteria criteria, PageRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(request);

        var links = Filter(context.Set<ShareLink>().AsNoTracking(), criteria);
        var rows = from link in links
                   join recipient in context.Set<Recipient>().AsNoTracking() on link.RecipientId equals recipient.Id
                   select new { Link = link, recipient.Name, recipient.Organisation };

        var totalCount = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(row => row.Link.CreatedAt)
            .ThenByDescending(row => row.Link.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var playerIds = page.SelectMany(row => row.Link.PlayerIds).Distinct().ToList();
        var names = await context.Set<Player>().AsNoTracking()
            .Where(player => playerIds.Contains(player.Id))
            .Select(player => new { player.Id, player.Name })
            .ToDictionaryAsync(player => player.Id, player => player.Name!.Value, cancellationToken);

        var items = page.Select(row => new ShareLinkListItemDto
        {
            Id = row.Link.Id,
            Code = row.Link.Code.Value,
            Type = row.Link.Type.ToString(),
            Status = row.Link.Status.ToString(),
            Access = row.Link.AccessAt(now).ToString(),
            RecipientId = row.Link.RecipientId,
            RecipientName = row.Name,
            RecipientOrganisation = row.Organisation,
            PlayerNames = [.. row.Link.PlayerIds.Where(names.ContainsKey).Select(id => names[id])],
            ExpiresAt = row.Link.ExpiresAt,
            CreatedAt = row.Link.CreatedAt,
        }).ToList();

        return PagedResult<ShareLinkListItemDto>.Create(items, request, totalCount);
    }

    /// <summary>Applies the status, player and recipient filters.</summary>
    /// <param name="links">Every link.</param>
    /// <param name="criteria">The criteria.</param>
    /// <returns>The matching links, not yet ordered or paged.</returns>
    private static IQueryable<ShareLink> Filter(IQueryable<ShareLink> links, ShareLinkListCriteria criteria)
    {
        if (criteria.Status is { } status)
        {
            links = links.Where(link => link.Status == status);
        }

        if (criteria.PlayerId is { } playerId)
        {
            links = links.Where(link => EF.Property<List<Guid>>(link, PlayerIdsField).Contains(playerId));
        }

        if (criteria.RecipientId is { } recipientId)
        {
            links = links.Where(link => link.RecipientId == recipientId);
        }

        return links;
    }
}
