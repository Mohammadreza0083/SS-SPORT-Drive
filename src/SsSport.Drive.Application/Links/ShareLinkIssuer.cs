using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Links;

/// <summary>
/// Creates a personal link: draws a code nobody uses, builds the link, stores it and writes the
/// <c>share_link.created</c> audit entry. Shared by the New share link form (L-1) and by approving an access request (P-4),
/// so both make links the same way.
/// </summary>
/// <param name="links">The links.</param>
/// <param name="audit">The audit log.</param>
public sealed class ShareLinkIssuer(IRepository<ShareLink, Guid> links, IRepository<AuditEntry, Guid> audit)
{
    /// <summary>How many fresh codes to try before giving up; a clash is already one in billions.</summary>
    private const int CodeAttempts = 5;

    /// <summary>Creates, stores and audits a link.</summary>
    /// <param name="recipient">Who the link is for.</param>
    /// <param name="players">The players, in page order.</param>
    /// <param name="note">The personal note, or <see langword="null"/>.</param>
    /// <param name="expiresAt">When the link stops opening.</param>
    /// <param name="settings">The link's switches.</param>
    /// <param name="actorId">The admin who creates it, or <see langword="null"/>.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The new link.</returns>
    /// <exception cref="Domain.Primitives.DomainException">A broken link rule (for example a player who is not published).</exception>
    /// <exception cref="InvalidOperationException">Every code attempt clashed.</exception>
    public async Task<ShareLink> IssueAsync(
        Recipient recipient,
        IReadOnlyCollection<Player> players,
        string? note,
        DateTimeOffset? expiresAt,
        ShareLinkSettings settings,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var link = ShareLink.Create(await UnusedCodeAsync(cancellationToken), recipient, players, note, expiresAt, settings, actorId, now);
        links.Add(link);
        audit.Add(AuditEntry.Record(actorId, CreateShareLinkCommand.CreatedAction, ShareLinkRules.AuditTarget, link.Id.ToString(), null, now));
        return link;
    }

    /// <summary>Draws codes until one is not used by any link.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The unused code.</returns>
    /// <exception cref="InvalidOperationException">Every attempt clashed.</exception>
    private async Task<ShareLinkCode> UnusedCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var code = ShareLinkCode.Generate();
            if (!await links.AnyAsync(new ShareLinkByCodeSpec(code), cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException($"No unused link code after {CodeAttempts} attempts.");
    }
}
