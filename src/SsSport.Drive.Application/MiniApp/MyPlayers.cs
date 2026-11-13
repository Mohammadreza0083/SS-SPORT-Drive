using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Assets;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.MiniApp;

/// <summary>The filter chips of My players (T-7).</summary>
public enum PlayerListFilter
{
    /// <summary>Every player.</summary>
    All,

    /// <summary>Published players.</summary>
    Live,

    /// <summary>Players with at least one file queued, copying or processing.</summary>
    Processing,

    /// <summary>Players with missing details, a failed file or a Drive folder that lost access.</summary>
    NeedsAttention,
}

/// <summary>Which players a list shows.</summary>
/// <param name="AnalystId">Only this analyst's players, or <see langword="null"/> for every player (admins).</param>
/// <param name="Search">Part of the name or the exact Volleybox id, or <see langword="null"/>.</param>
/// <param name="Filter">The filter chip.</param>
/// <param name="PlayerId">Only this player (the bot's player card reads its counts this way), or <see langword="null"/>.</param>
public sealed record PlayerListCriteria(Guid? AnalystId, string? Search, PlayerListFilter Filter, Guid? PlayerId = null);

/// <summary>One row of My players: who the player is and the state of their files and folders.</summary>
public sealed record PlayerListItemDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the full name, or <see langword="null"/> while unknown.</summary>
    public string? Name { get; init; }

    /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Gets the two-letter country code, or <see langword="null"/>.</summary>
    public string? NationalityCode { get; init; }

    /// <summary>Gets the country's English name, or <see langword="null"/>.</summary>
    public string? NationalityName { get; init; }

    /// <summary>Gets the position codes, primary first; empty while unknown.</summary>
    public IReadOnlyList<string> Positions { get; init; } = [];

    /// <summary>Gets a value indicating whether the player has a photo (read it from <c>/miniapp/players/{id}/photo</c>).</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Gets <c>Waiting</c> or <c>Published</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether every detail needed for publishing is set.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Gets how many files the player has, hidden ones included.</summary>
    public int FileCount { get; init; }

    /// <summary>Gets how many files are queued, copying or processing.</summary>
    public int ProcessingCount { get; init; }

    /// <summary>Gets how many files failed to process.</summary>
    public int FailedCount { get; init; }

    /// <summary>Gets a value indicating whether a linked Drive folder lost access.</summary>
    public bool HasLostFolder { get; init; }

    /// <summary>Gets a value indicating whether the player needs attention (missing details, a failed file or a lost folder).</summary>
    public bool NeedsAttention => !IsComplete || FailedCount > 0 || HasLostFolder;

    /// <summary>Gets when the player was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One file in the player detail, in page order.</summary>
public sealed record PlayerFileDto
{
    /// <summary>Gets the asset id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets <c>Video</c>, <c>Document</c> or <c>Scout</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Gets the title shown to viewers.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the tag names (e.g. <c>Highlight</c>, <c>FullMatch</c>); empty when untagged.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets the position on the player page.</summary>
    public int SortOrder { get; init; }

    /// <summary>Gets <c>Visible</c> or <c>Hidden</c>.</summary>
    public string Visibility { get; init; } = string.Empty;

    /// <summary>Gets the processing state (<c>Queued</c>, <c>Copying</c>, <c>Processing</c>, <c>Ready</c>, <c>Failed</c>).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Gets a video's duration in seconds, or <see langword="null"/>.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Gets a document's page count, or <see langword="null"/>.</summary>
    public int? PageCount { get; init; }

    /// <summary>Gets a value indicating whether this is the player's hero video.</summary>
    public bool IsHero { get; init; }
}

/// <summary>
/// Lists My players (<c>GET /miniapp/players</c>, T-7): an analyst's own players, or every player for an admin, newest
/// first, searched and filtered, one page at a time.
/// </summary>
/// <param name="Paging">The page to read.</param>
/// <param name="Search">Part of the name or the exact Volleybox id, or <see langword="null"/>.</param>
/// <param name="Filter"><c>all</c>, <c>live</c>, <c>processing</c> or <c>needsAttention</c> (any case), or <see langword="null"/> for all.</param>
public sealed record GetMyPlayersQuery(PageRequest Paging, string? Search, string? Filter) : IPagedQuery<PlayerListItemDto>, IAuthorizedRequest
{
    /// <summary>The longest search text.</summary>
    public const int MaxSearchLength = 100;

    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;

    /// <summary>Reads a filter chip by name, case-insensitively; numbers are not names.</summary>
    /// <param name="value">The value sent.</param>
    /// <param name="filter">The chip.</param>
    /// <returns><see langword="true"/> when the value names a chip, or is left out.</returns>
    public static bool TryParseFilter(string? value, out PlayerListFilter filter)
    {
        filter = PlayerListFilter.All;
        return string.IsNullOrEmpty(value)
            || (!value.Any(char.IsDigit) && Enum.TryParse(value, ignoreCase: true, out filter) && Enum.IsDefined(filter));
    }
}

/// <summary>The search fits in <see cref="GetMyPlayersQuery.MaxSearchLength"/> characters and the filter names a chip.</summary>
public sealed class GetMyPlayersQueryValidator : AbstractValidator<GetMyPlayersQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetMyPlayersQueryValidator()
    {
        RuleFor(query => query.Search)
            .MaximumLength(GetMyPlayersQuery.MaxSearchLength)
            .WithMessage($"Search with at most {GetMyPlayersQuery.MaxSearchLength} characters.")
            .OverridePropertyName("search");
        RuleFor(query => query.Filter)
            .Must(filter => GetMyPlayersQuery.TryParseFilter(filter, out _))
            .WithMessage("Use all, live, processing or needsAttention.")
            .OverridePropertyName("filter");
    }
}

/// <summary>Handles <see cref="GetMyPlayersQuery"/>.</summary>
/// <param name="reader">The player list read model.</param>
/// <param name="currentUser">The caller.</param>
public sealed class GetMyPlayersQueryHandler(IPlayerListReader reader, ICurrentUser currentUser) : IQueryHandler<GetMyPlayersQuery, PagedResult<PlayerListItemDto>>
{
    /// <summary>Limits an analyst to their own players and reads the page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="UnauthenticatedException">The caller has no user id.</exception>
    public Task<PagedResult<PlayerListItemDto>> Handle(GetMyPlayersQuery request, CancellationToken cancellationToken)
    {
        Guid? analystId = null;
        if (!currentUser.IsInRole(Role.Admin.Name))
        {
            analystId = Guid.TryParse(currentUser.UserId, out var userId) ? userId : throw new UnauthenticatedException();
        }

        GetMyPlayersQuery.TryParseFilter(request.Filter, out var filter);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        return reader.PageAsync(new PlayerListCriteria(analystId, search, filter), request.Paging, cancellationToken);
    }
}

/// <summary>A player's files in page order (the paged reader adds the id as the tie-breaker).</summary>
public sealed class PlayerFilesInPageOrderSpec : Specification<Asset>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="playerId">The player.</param>
    public PlayerFilesInPageOrderSpec(Guid playerId) =>
        Query.Where(asset => asset.PlayerId == playerId).OrderBy(asset => asset.SortOrder);
}

/// <summary>
/// Lists a player's files for the player detail (<c>GET /miniapp/players/{id}/files</c>, T-7): every file, hidden ones
/// included, in page order with the hero marked. Any active team member may read them, like the player card.
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Paging">The page to read.</param>
public sealed record GetPlayerFilesQuery(Guid PlayerId, PageRequest Paging) : IPagedQuery<PlayerFileDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Handles <see cref="GetPlayerFilesQuery"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="pages">Reads pages in the database.</param>
public sealed class GetPlayerFilesQueryHandler(IRepository<Player, Guid> players, IPagedReader pages)
    : IQueryHandler<GetPlayerFilesQuery, PagedResult<PlayerFileDto>>
{
    /// <summary>Reads the player's hero, then one page of its files.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="NotFoundException">The player does not exist.</exception>
    public async Task<PagedResult<PlayerFileDto>> Handle(GetPlayerFilesQuery request, CancellationToken cancellationToken)
    {
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken) ?? throw new NotFoundException("Player");
        return await pages.PageAsync(
            new PlayerFilesInPageOrderSpec(player.Id),
            request.Paging,
            asset => asset.ToFileDto() with { IsHero = asset.Id == player.HeroAssetId },
            cancellationToken);
    }
}

/// <summary>Validates <see cref="GetPlayerFilesQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetPlayerFilesQueryValidator : AbstractValidator<GetPlayerFilesQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetPlayerFilesQueryValidator()
    {
        RuleFor(r => r.PlayerId).ValidId().OverridePropertyName("playerId");
        RuleFor(r => r.Paging).ValidPaging();
    }
}
