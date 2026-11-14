using System.Linq.Expressions;
using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Settings;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Links;

/// <summary>The one rule for what a viewer may see of a player's files: of this kind, visible, and finished processing.</summary>
public static class ViewerAssetConditions
{
    /// <summary>Builds the condition.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="kind">The kind of file.</param>
    /// <returns>The condition, for a specification.</returns>
    public static Expression<Func<Asset, bool>> Watchable(Guid playerId, AssetKind kind) =>
        asset => asset.PlayerId == playerId
            && asset.File.Kind == kind
            && asset.Visibility == AssetVisibility.Visible
            && asset.State == ProcessingState.Ready;
}

/// <summary>A player's videos a viewer can watch: ready and visible, the hero first, then the page order.</summary>
public sealed class ViewerVideosSpec : Specification<Asset>
{
    /// <summary>Initializes a new instance of the <see cref="ViewerVideosSpec"/> class.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="heroId">The player's hero video, if any.</param>
    public ViewerVideosSpec(Guid playerId, Guid? heroId) =>
        Query.Where(ViewerAssetConditions.Watchable(playerId, AssetKind.Video))
            .OrderByDescending(asset => heroId.HasValue && asset.Id == heroId.Value)
            .ThenBy(asset => asset.SortOrder);
}

/// <summary>A player's documents a viewer can read: ready and visible PDFs and images, in page order. DataVolley files are listed by <see cref="ViewerScoutsSpec"/> (V-8).</summary>
public sealed class ViewerDocumentsSpec : Specification<Asset>
{
    /// <summary>Initializes a new instance of the <see cref="ViewerDocumentsSpec"/> class.</summary>
    /// <param name="playerId">The player.</param>
    public ViewerDocumentsSpec(Guid playerId) =>
        Query.Where(ViewerAssetConditions.Watchable(playerId, AssetKind.Document))
            .OrderBy(asset => asset.SortOrder);
}

/// <summary>The checks every viewer content read starts with: the link is open, the session is live and belongs to it, and the player is on it.</summary>
public static class ViewerContentAccess
{
    /// <summary>Finds the viewer's live session for this link.</summary>
    /// <param name="sessions">The sessions.</param>
    /// <param name="secrets">Hashes the token.</param>
    /// <param name="link">The open link.</param>
    /// <param name="sessionToken">The session token from the header, if any.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The session.</returns>
    /// <exception cref="ViewerVerificationRequiredException">No session, an unknown or ended one, or one for another link.</exception>
    public static async Task<ViewerSession> RequireSessionAsync(
        this IRepository<ViewerSession, Guid> sessions,
        IViewerSecrets secrets,
        ShareLink link,
        string? sessionToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            throw new ViewerVerificationRequiredException();
        }

        var session = await sessions.FirstOrDefaultAsync(new ViewerSessionByTokenSpec(secrets.HashToken(sessionToken)), cancellationToken);
        return session is not null && session.ShareLinkId == link.Id && session.IsActiveAt(now)
            ? session
            : throw new ViewerVerificationRequiredException();
    }

    /// <summary>Makes sure the player is on the link, so other players' ids cannot be walked.</summary>
    /// <param name="link">The link.</param>
    /// <param name="playerId">The player id from the URL.</param>
    /// <exception cref="NotFoundException">The player is not on this link.</exception>
    public static void EnsureShows(this ShareLink link, Guid playerId)
    {
        if (!link.PlayerIds.Contains(playerId))
        {
            throw new NotFoundException("Player");
        }
    }
}

/// <summary>Reads the link overview for a signed-in viewer.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
public sealed record GetLinkContentQuery(string Code, string? SessionToken) : IQuery<LinkContentDto>;

/// <summary>Handles <see cref="GetLinkContentQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="settings">The console's settings (the contact email).</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetLinkContentQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    IAppSettingsStore settings,
    TimeProvider timeProvider) : IQueryHandler<GetLinkContentQuery, LinkContentDto>
{
    /// <inheritdoc />
    public async Task<LinkContentDto> Handle(GetLinkContentQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        var session = await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        var recipient = await recipients.GetByIdAsync(link.RecipientId, cancellationToken) ?? throw new ShareLinkUnavailableException();
        return link.ToContentDto(recipient, session) with { ContactEmail = (await settings.GetAsync(cancellationToken)).ContactEmail.Value };
    }
}

/// <summary>Lists one page of a link's players in the link's page order.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
/// <param name="Paging">The page.</param>
public sealed record GetLinkPlayersQuery(string Code, string? SessionToken, PageRequest Paging) : IPagedQuery<LinkPlayerCardDto>;

/// <summary>
/// Handles <see cref="GetLinkPlayersQuery"/>. The page is cut from the link's own player list (at most
/// <see cref="ShareLink.MaxPlayers"/> ids, already loaded with the link, in page order), and only that page's players
/// are read from the database.
/// </summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="reader">Reads the players' facts.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetLinkPlayersQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    ILinkPlayerReader reader,
    TimeProvider timeProvider) : IQueryHandler<GetLinkPlayersQuery, PagedResult<LinkPlayerCardDto>>
{
    /// <inheritdoc />
    public async Task<PagedResult<LinkPlayerCardDto>> Handle(GetLinkPlayersQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);

        var pageIds = link.PlayerIds.Skip(request.Paging.Skip).Take(request.Paging.PageSize).ToList();
        var facts = pageIds.Count == 0 ? [] : await reader.ReadAsync(pageIds, cancellationToken);
        var byId = facts.ToDictionary(item => item.Id);
        var cards = pageIds.Where(byId.ContainsKey).Select(id => byId[id].ToCardDto()).ToList();
        return PagedResult<LinkPlayerCardDto>.Create(cards, request.Paging, link.PlayerIds.Count);
    }
}

/// <summary>Reads one player's page header on a link.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
/// <param name="PlayerId">The player.</param>
public sealed record GetLinkPlayerQuery(string Code, string? SessionToken, Guid PlayerId) : IQuery<LinkPlayerDto>;

/// <summary>Handles <see cref="GetLinkPlayerQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="reader">Reads the player's facts.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetLinkPlayerQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    ILinkPlayerReader reader,
    TimeProvider timeProvider) : IQueryHandler<GetLinkPlayerQuery, LinkPlayerDto>
{
    /// <inheritdoc />
    public async Task<LinkPlayerDto> Handle(GetLinkPlayerQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);

        var facts = await reader.ReadAsync([request.PlayerId], cancellationToken);
        return facts.Count == 1 ? facts[0].ToPlayerDto() : throw new NotFoundException("Player");
    }
}

/// <summary>Lists one page of a player's videos on a link.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="Paging">The page.</param>
public sealed record GetLinkPlayerVideosQuery(string Code, string? SessionToken, Guid PlayerId, PageRequest Paging) : IPagedQuery<LinkVideoDto>;

/// <summary>Handles <see cref="GetLinkPlayerVideosQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="players">The players, for the hero.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="pages">Pages in SQL.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetLinkPlayerVideosQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<Player, Guid> players,
    IViewerSecrets secrets,
    IPagedReader pages,
    TimeProvider timeProvider) : IQueryHandler<GetLinkPlayerVideosQuery, PagedResult<LinkVideoDto>>
{
    /// <inheritdoc />
    public async Task<PagedResult<LinkVideoDto>> Handle(GetLinkPlayerVideosQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);

        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken) ?? throw new NotFoundException("Player");
        return await pages.PageAsync(new ViewerVideosSpec(player.Id, player.HeroAssetId), request.Paging, ViewerContentMapper.ToVideoDto, cancellationToken);
    }
}

/// <summary>Lists one page of a player's documents on a link.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="Paging">The page.</param>
public sealed record GetLinkPlayerDocumentsQuery(string Code, string? SessionToken, Guid PlayerId, PageRequest Paging) : IPagedQuery<LinkDocumentDto>;

/// <summary>Handles <see cref="GetLinkPlayerDocumentsQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="pages">Pages in SQL.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetLinkPlayerDocumentsQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    IPagedReader pages,
    TimeProvider timeProvider) : IQueryHandler<GetLinkPlayerDocumentsQuery, PagedResult<LinkDocumentDto>>
{
    /// <inheritdoc />
    public async Task<PagedResult<LinkDocumentDto>> Handle(GetLinkPlayerDocumentsQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);

        return await pages.PageAsync(new ViewerDocumentsSpec(request.PlayerId), request.Paging, ViewerContentMapper.ToDocumentDto, cancellationToken);
    }
}

/// <summary>Reads a player's photo on a link.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
/// <param name="PlayerId">The player.</param>
public sealed record GetLinkPlayerPhotoQuery(string Code, string? SessionToken, Guid PlayerId) : IQuery<StoredObject>;

/// <summary>Handles <see cref="GetLinkPlayerPhotoQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="players">The players.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="storage">Where photos are kept.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetLinkPlayerPhotoQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<Player, Guid> players,
    IViewerSecrets secrets,
    IObjectStorage storage,
    TimeProvider timeProvider) : IQueryHandler<GetLinkPlayerPhotoQuery, StoredObject>
{
    /// <inheritdoc />
    public async Task<StoredObject> Handle(GetLinkPlayerPhotoQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);

        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken) ?? throw new NotFoundException("Player");
        if (player.Photo is null)
        {
            throw new NotFoundException("Photo");
        }

        return await storage.GetAsync(player.Photo.StorageKey, cancellationToken) ?? throw new NotFoundException("Photo");
    }
}

/// <summary>Validates <see cref="GetLinkContentQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetLinkContentQueryValidator : AbstractValidator<GetLinkContentQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetLinkContentQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
    }
}

/// <summary>Validates <see cref="GetLinkPlayerDocumentsQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetLinkPlayerDocumentsQueryValidator : AbstractValidator<GetLinkPlayerDocumentsQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetLinkPlayerDocumentsQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
        RuleFor(r => r.PlayerId).ValidId().OverridePropertyName("playerId");
        RuleFor(r => r.Paging).ValidPaging();
    }
}

/// <summary>Validates <see cref="GetLinkPlayerPhotoQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetLinkPlayerPhotoQueryValidator : AbstractValidator<GetLinkPlayerPhotoQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetLinkPlayerPhotoQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
        RuleFor(r => r.PlayerId).ValidId().OverridePropertyName("playerId");
    }
}

/// <summary>Validates <see cref="GetLinkPlayerQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetLinkPlayerQueryValidator : AbstractValidator<GetLinkPlayerQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetLinkPlayerQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
        RuleFor(r => r.PlayerId).ValidId().OverridePropertyName("playerId");
    }
}

/// <summary>Validates <see cref="GetLinkPlayerVideosQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetLinkPlayerVideosQueryValidator : AbstractValidator<GetLinkPlayerVideosQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetLinkPlayerVideosQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
        RuleFor(r => r.PlayerId).ValidId().OverridePropertyName("playerId");
        RuleFor(r => r.Paging).ValidPaging();
    }
}

/// <summary>Validates <see cref="GetLinkPlayersQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetLinkPlayersQueryValidator : AbstractValidator<GetLinkPlayersQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetLinkPlayersQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
        RuleFor(r => r.Paging).ValidPaging();
    }
}
