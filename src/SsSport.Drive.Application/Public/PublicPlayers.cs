using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Public;

/// <summary>A published player by slug, read untracked.</summary>
public sealed class PublishedPlayerBySlugSpec : Specification<Player>, ISingleResultSpecification<Player>
{
    /// <summary>Initializes a new instance of the <see cref="PublishedPlayerBySlugSpec"/> class.</summary>
    /// <param name="slug">The slug.</param>
    public PublishedPlayerBySlugSpec(Slug slug) =>
        Query.Where(player => player.Slug == slug && player.Status == PlayerStatus.Published).AsNoTracking();
}

/// <summary>What the public pages share: reading a slug from the address and the mappings to the public DTOs.</summary>
public static class PublicPlayerRules
{
    /// <summary>The position codes the home page can filter by.</summary>
    public static readonly IReadOnlyList<string> PositionCodes = ["S", "OH", "OPP", "MB", "L", "DS"];

    /// <summary>Reads a slug from the address.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The slug, or <see langword="null"/> when no player could have it.</returns>
    public static Slug? ParseSlug(string? text)
    {
        try
        {
            return Slug.Create(text);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    /// <summary>Finds a published player, or fails like an unknown one.</summary>
    /// <param name="players">The players.</param>
    /// <param name="slug">The slug from the address.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The player.</returns>
    /// <exception cref="NotFoundException">No such published player.</exception>
    public static async Task<Player> RequirePublishedAsync(this IRepository<Player, Guid> players, string? slug, CancellationToken cancellationToken)
    {
        var parsed = ParseSlug(slug) ?? throw new NotFoundException("Player");
        return await players.FirstOrDefaultAsync(new PublishedPlayerBySlugSpec(parsed), cancellationToken) ?? throw new NotFoundException("Player");
    }

    /// <summary>Maps the facts to a card.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The card.</returns>
    public static PublicPlayerCardDto ToCardDto(this PublicPlayerFacts facts)
    {
        var card = facts.ToLinkFacts().ToCardDto();
        return new PublicPlayerCardDto
        {
            Slug = facts.Slug,
            Name = card.Name,
            Position = card.Position,
            Nationality = card.Nationality,
            Videos = card.VideoCount,
            Documents = facts.DocumentCount,
            HasPhoto = card.HasPhoto,
        };
    }

    /// <summary>Maps the facts to the locked page's header.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The header.</returns>
    public static PublicPlayerDto ToPlayerDto(this PublicPlayerFacts facts)
    {
        var player = facts.ToLinkFacts().ToPlayerDto();
        return new PublicPlayerDto
        {
            Slug = facts.Slug,
            Name = player.Name,
            FirstName = player.FirstName,
            Position = player.Position,
            Nationality = player.Nationality,
            VolleyboxUrl = player.VolleyboxUrl,
            HasPhoto = player.HasPhoto,
            Stats = player.Stats,
        };
    }

    /// <summary>Maps a ready video to its locked row: no id, only title, tag and length.</summary>
    /// <param name="asset">The video.</param>
    /// <returns>The row.</returns>
    public static LockedVideoDto ToLockedDto(this Asset asset)
    {
        var video = asset.ToVideoDto();
        return new LockedVideoDto { Title = video.Title, Tag = video.Tag, DurationSeconds = video.DurationSeconds };
    }

    /// <summary>Maps a ready document to its locked row: no id, only title and pages.</summary>
    /// <param name="asset">The document.</param>
    /// <returns>The row.</returns>
    public static LockedDocumentDto ToLockedDocumentDto(this Asset asset)
    {
        var document = asset.ToDocumentDto();
        return new LockedDocumentDto { Title = document.Title, PageCount = document.PageCount };
    }

    /// <summary>Reuses the viewer pages' mapping rules for names, positions and countries.</summary>
    /// <param name="facts">The public facts.</param>
    /// <returns>The same facts as the link pages read them; the public pages show no scout files, so their count is 0.</returns>
    private static LinkPlayerFacts ToLinkFacts(this PublicPlayerFacts facts) => new(
        facts.Id,
        facts.Name,
        facts.NationalityCode,
        facts.Positions,
        facts.HasPhoto,
        facts.VolleyboxUrl,
        facts.VideoCount,
        facts.VideoSeconds,
        facts.DocumentCount,
        ScoutCount: 0);
}

/// <summary>Searches published players (<c>GET /public/players</c>). Public.</summary>
/// <param name="Search">Part of the name, or <see langword="null"/>.</param>
/// <param name="Position">A primary position code, or <see langword="null"/> for all.</param>
/// <param name="Paging">The page.</param>
public sealed record GetPublicPlayersQuery(string? Search, string? Position, PageRequest Paging) : IPagedQuery<PublicPlayerCardDto>;

/// <summary>The search text is short enough and the position is one of the six codes.</summary>
public sealed class GetPublicPlayersQueryValidator : AbstractValidator<GetPublicPlayersQuery>
{
    /// <summary>The longest search text accepted.</summary>
    public const int MaxSearchLength = 100;

    /// <summary>Builds the rules.</summary>
    public GetPublicPlayersQueryValidator()
    {
        RuleFor(query => query.Search).MaximumLength(MaxSearchLength).OverridePropertyName("search");
        RuleFor(query => query.Position)
            .Must(position => PositionParser.TryParse(position, out _))
            .When(query => !string.IsNullOrWhiteSpace(query.Position))
            .WithMessage("Position must be one of S, OH, OPP, MB, L, DS.")
            .OverridePropertyName("position");
    }
}

/// <summary>Handles <see cref="GetPublicPlayersQuery"/>.</summary>
/// <param name="reader">Reads the page in SQL.</param>
public sealed class GetPublicPlayersQueryHandler(IPublicPlayerReader reader) : IQueryHandler<GetPublicPlayersQuery, PagedResult<PublicPlayerCardDto>>
{
    /// <inheritdoc />
    public async Task<PagedResult<PublicPlayerCardDto>> Handle(GetPublicPlayersQuery request, CancellationToken cancellationToken)
    {
        var position = PositionParser.TryParse(request.Position, out var parsed) ? parsed.Code() : null;
        var page = await reader.PageAsync(request.Search, position, request.Paging, cancellationToken);
        return PagedResult<PublicPlayerCardDto>.Create([.. page.Items.Select(facts => facts.ToCardDto())], request.Paging, page.TotalCount);
    }
}

/// <summary>Reads one published player for the locked page (<c>GET /public/players/{slug}</c>). Public.</summary>
/// <param name="Slug">The slug.</param>
public sealed record GetPublicPlayerQuery(string Slug) : IQuery<PublicPlayerDto>;

/// <summary>Handles <see cref="GetPublicPlayerQuery"/>.</summary>
/// <param name="reader">Reads the facts.</param>
public sealed class GetPublicPlayerQueryHandler(IPublicPlayerReader reader) : IQueryHandler<GetPublicPlayerQuery, PublicPlayerDto>
{
    /// <inheritdoc />
    public async Task<PublicPlayerDto> Handle(GetPublicPlayerQuery request, CancellationToken cancellationToken)
    {
        var facts = await reader.FindAsync(request.Slug, cancellationToken) ?? throw new NotFoundException("Player");
        return facts.ToPlayerDto();
    }
}

/// <summary>Lists one page of a player's locked videos (<c>GET /public/players/{slug}/videos</c>). Public.</summary>
/// <param name="Slug">The slug.</param>
/// <param name="Paging">The page.</param>
public sealed record GetPublicPlayerVideosQuery(string Slug, PageRequest Paging) : IPagedQuery<LockedVideoDto>;

/// <summary>Handles <see cref="GetPublicPlayerVideosQuery"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="pages">Pages in SQL.</param>
public sealed class GetPublicPlayerVideosQueryHandler(IRepository<Player, Guid> players, IPagedReader pages) : IQueryHandler<GetPublicPlayerVideosQuery, PagedResult<LockedVideoDto>>
{
    /// <inheritdoc />
    public async Task<PagedResult<LockedVideoDto>> Handle(GetPublicPlayerVideosQuery request, CancellationToken cancellationToken)
    {
        var player = await players.RequirePublishedAsync(request.Slug, cancellationToken);
        return await pages.PageAsync(new ViewerVideosSpec(player.Id, player.HeroAssetId), request.Paging, PublicPlayerRules.ToLockedDto, cancellationToken);
    }
}

/// <summary>Lists one page of a player's locked documents (<c>GET /public/players/{slug}/documents</c>). Public.</summary>
/// <param name="Slug">The slug.</param>
/// <param name="Paging">The page.</param>
public sealed record GetPublicPlayerDocumentsQuery(string Slug, PageRequest Paging) : IPagedQuery<LockedDocumentDto>;

/// <summary>Handles <see cref="GetPublicPlayerDocumentsQuery"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="pages">Pages in SQL.</param>
public sealed class GetPublicPlayerDocumentsQueryHandler(IRepository<Player, Guid> players, IPagedReader pages) : IQueryHandler<GetPublicPlayerDocumentsQuery, PagedResult<LockedDocumentDto>>
{
    /// <inheritdoc />
    public async Task<PagedResult<LockedDocumentDto>> Handle(GetPublicPlayerDocumentsQuery request, CancellationToken cancellationToken)
    {
        var player = await players.RequirePublishedAsync(request.Slug, cancellationToken);
        return await pages.PageAsync(new ViewerDocumentsSpec(player.Id), request.Paging, PublicPlayerRules.ToLockedDocumentDto, cancellationToken);
    }
}

/// <summary>Reads a published player's photo (<c>GET /public/players/{slug}/photo</c>). Public.</summary>
/// <param name="Slug">The slug.</param>
public sealed record GetPublicPlayerPhotoQuery(string Slug) : IQuery<StoredObject>;

/// <summary>Handles <see cref="GetPublicPlayerPhotoQuery"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="storage">Where photos are kept.</param>
public sealed class GetPublicPlayerPhotoQueryHandler(IRepository<Player, Guid> players, IObjectStorage storage) : IQueryHandler<GetPublicPlayerPhotoQuery, StoredObject>
{
    /// <inheritdoc />
    public async Task<StoredObject> Handle(GetPublicPlayerPhotoQuery request, CancellationToken cancellationToken)
    {
        var player = await players.RequirePublishedAsync(request.Slug, cancellationToken);
        if (player.Photo is null)
        {
            throw new NotFoundException("Photo");
        }

        return await storage.GetAsync(player.Photo.StorageKey, cancellationToken) ?? throw new NotFoundException("Photo");
    }
}
