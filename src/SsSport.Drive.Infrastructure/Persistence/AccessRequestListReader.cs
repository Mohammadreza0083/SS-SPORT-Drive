using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Public;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IAccessRequestListReader"/>. The requests are joined to their players, filtered,
/// counted, ordered and paged in PostgreSQL; the deciding admins and the links of that one page are then read in two
/// small queries. Nothing is tracked. A recorded exception to "conditions in specifications", like
/// <see cref="ShareLinkListReader"/>: the page needs a join.
/// </summary>
/// <param name="context">The request's EF Core context.</param>
public sealed class AccessRequestListReader(DbContext context) : IAccessRequestListReader
{
    /// <summary>Reads one page of requests with a status, newest first (the id breaks ties), and the total.</summary>
    /// <param name="status">The status to list.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<PagedResult<AccessRequestDto>> PageAsync(AccessRequestStatus status, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rows = from accessRequest in context.Set<AccessRequest>().AsNoTracking().Where(item => item.Status == status)
                   join player in context.Set<Player>().AsNoTracking() on accessRequest.PlayerId equals player.Id
                   select new { Request = accessRequest, PlayerId = player.Id, player.Name, player.Slug };

        var totalCount = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(row => row.Request.CreatedAt)
            .ThenByDescending(row => row.Request.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var adminIds = page.Select(row => row.Request.DecidedByUserId).OfType<Guid>().Distinct().ToList();
        var admins = await context.Set<User>().AsNoTracking()
            .Where(user => adminIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName })
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);

        var linkIds = page.Select(row => row.Request.ShareLinkId).OfType<Guid>().Distinct().ToList();
        var links = (await context.Set<ShareLink>().AsNoTracking()
            .Where(link => linkIds.Contains(link.Id))
            .Select(link => new { link.Id, link.Code, link.ExpiresAt })
            .ToListAsync(cancellationToken))
            .ToDictionary(link => link.Id);

        var items = page.Select(row => new AccessRequestDto
        {
            Id = row.Request.Id,
            Player = new ShareLinkPlayerDto { Id = row.PlayerId, Name = row.Name?.Value, Slug = row.Slug?.Value },
            Email = row.Request.Email.Value,
            About = row.Request.About,
            Source = row.Request.Source.ToString(),
            Status = row.Request.Status.ToString(),
            CreatedAt = row.Request.CreatedAt,
            DecidedAt = row.Request.DecidedAt,
            DecidedBy = row.Request.DecidedByUserId is { } adminId && admins.TryGetValue(adminId, out var name) ? name : null,
            ShareLink = row.Request.ShareLinkId is { } linkId && links.TryGetValue(linkId, out var link)
                ? new AccessRequestLinkDto { Id = link.Id, Code = link.Code.Value, Path = $"/r/{link.Code.Value}", ExpiresAt = link.ExpiresAt }
                : null,
        }).ToList();

        return PagedResult<AccessRequestDto>.Create(items, request, totalCount);
    }
}
