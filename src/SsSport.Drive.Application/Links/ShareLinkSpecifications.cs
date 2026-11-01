using Ardalis.Specification;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Links;

/// <summary>The link with a given code (codes are unique).</summary>
public sealed class ShareLinkByCodeSpec : Specification<ShareLink>, ISingleResultSpecification<ShareLink>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="code">The code.</param>
    public ShareLinkByCodeSpec(ShareLinkCode code) => Query.Where(link => link.Code == code);
}

/// <summary>The players with the given ids (at most <see cref="ShareLink.MaxPlayers"/> of them).</summary>
public sealed class PlayersByIdsSpec : Specification<Player>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="ids">The player ids.</param>
    public PlayersByIdsSpec(IReadOnlyCollection<Guid> ids) => Query.Where(player => ids.Contains(player.Id));
}
