using System.Text;
using Ardalis.Specification;
using Riok.Mapperly.Abstractions;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Scouting;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Links;

/// <summary>One DataVolley scout file in a player's list. The file itself is never sent.</summary>
public sealed record LinkScoutDto
{
    /// <summary>Gets the scout file's id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the display title.</summary>
    public string Title { get; init; } = string.Empty;
}

/// <summary>The score of one set.</summary>
public sealed record SetScoreDto
{
    /// <summary>Gets the home team's points.</summary>
    public int Home { get; init; }

    /// <summary>Gets the away team's points.</summary>
    public int Away { get; init; }
}

/// <summary>The match header of a scout report.</summary>
public sealed record ScoutMatchDto
{
    /// <summary>Gets the match date, or <see langword="null"/> when the file has none.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Gets the home team's name.</summary>
    public string HomeTeam { get; init; } = string.Empty;

    /// <summary>Gets the away team's name.</summary>
    public string AwayTeam { get; init; } = string.Empty;

    /// <summary>Gets the sets the home team won.</summary>
    public int HomeSets { get; init; }

    /// <summary>Gets the sets the away team won.</summary>
    public int AwaySets { get; init; }

    /// <summary>Gets the final score of each played set.</summary>
    public IReadOnlyList<SetScoreDto> Sets { get; init; } = [];
}

/// <summary>Where the page's player was found in the scout file.</summary>
public sealed record ScoutPlayerDto
{
    /// <summary>Gets the shirt number.</summary>
    public int Number { get; init; }

    /// <summary>Gets the name of the player's team.</summary>
    public string Team { get; init; } = string.Empty;
}

/// <summary>One skill's numbers for the page's player.</summary>
public sealed record SkillStatsDto
{
    /// <summary>Gets the skill: Serve, Reception, Attack, Block, Dig or Set.</summary>
    public string Skill { get; init; } = string.Empty;

    /// <summary>Gets all actions of the skill.</summary>
    public int Total { get; init; }

    /// <summary>Gets the perfect ones.</summary>
    public int Perfect { get; init; }

    /// <summary>Gets the positive ones.</summary>
    public int Positive { get; init; }

    /// <summary>Gets the ok ones.</summary>
    public int Ok { get; init; }

    /// <summary>Gets the negative ones.</summary>
    public int Negative { get; init; }

    /// <summary>Gets the poor ones.</summary>
    public int Poor { get; init; }

    /// <summary>Gets the errors.</summary>
    public int Error { get; init; }

    /// <summary>Gets the success rate in whole percent, or <see langword="null"/> without actions.</summary>
    public int? SuccessRate { get; init; }

    /// <summary>Gets the efficiency in whole percent, or <see langword="null"/> without actions.</summary>
    public int? Efficiency { get; init; }
}

/// <summary>A parsed scout file for one player's page: the match and that player's stats per skill.</summary>
public sealed record ScoutReportDto
{
    /// <summary>Gets the scout file's id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the display title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the match header.</summary>
    public ScoutMatchDto Match { get; init; } = new();

    /// <summary>Gets where the player was found, or <see langword="null"/> when the file does not hold them.</summary>
    public ScoutPlayerDto? Player { get; init; }

    /// <summary>Gets the stats per skill; empty when the player was not found.</summary>
    public IReadOnlyList<SkillStatsDto> Skills { get; init; } = [];
}

/// <summary>Maps scout results to their DTOs at compile time.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ScoutMapper
{
    /// <summary>Maps a scout asset to its list row.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The row.</returns>
    public static partial LinkScoutDto ToScoutDto(this Asset asset);

    /// <summary>Maps one skill's numbers.</summary>
    /// <param name="stats">The numbers.</param>
    /// <returns>The DTO.</returns>
    public static partial SkillStatsDto ToDto(this SkillStats stats);

    /// <summary>Maps a set score.</summary>
    /// <param name="score">The score.</param>
    /// <returns>The DTO.</returns>
    public static partial SetScoreDto ToDto(this ScoutSetScore score);

    /// <summary>Builds the report for a scout file.</summary>
    /// <param name="asset">The scout file's asset.</param>
    /// <param name="match">The parsed match.</param>
    /// <param name="player">The page's player in the file, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    public static ScoutReportDto ToReport(Asset asset, ScoutMatch match, ScoutPlayer? player) => new()
    {
        Id = asset.Id,
        Title = asset.Title,
        Match = new ScoutMatchDto
        {
            Date = match.Date,
            HomeTeam = match.Home.Name,
            AwayTeam = match.Away.Name,
            HomeSets = match.Home.SetsWon,
            AwaySets = match.Away.SetsWon,
            Sets = [.. match.Sets.Select(set => set.ToDto())],
        },
        Player = player is null ? null : new ScoutPlayerDto { Number = player.Number, Team = player.Side == ScoutSide.Home ? match.Home.Name : match.Away.Name },
        Skills = player is null ? [] : [.. ScoutStats.For(match, player).Select(stats => stats.ToDto())],
    };
}

/// <summary>Keeps parsed scout files for a few minutes, so opening a report again does not read and parse the file again.</summary>
public interface IScoutMatchCache
{
    /// <summary>Returns the cached match, or loads and caches it.</summary>
    /// <param name="key">The cache key: the asset and its content version.</param>
    /// <param name="load">Reads and parses the file when it is not cached.</param>
    /// <returns>The match.</returns>
    Task<ScoutMatch> GetOrAddAsync(string key, Func<Task<ScoutMatch>> load);
}

/// <summary>A player's DataVolley files a viewer can open: ready and visible, in page order.</summary>
public sealed class ViewerScoutsSpec : Specification<Asset>
{
    /// <summary>Initializes a new instance of the <see cref="ViewerScoutsSpec"/> class.</summary>
    /// <param name="playerId">The player.</param>
    public ViewerScoutsSpec(Guid playerId) =>
        Query.Where(ViewerAssetConditions.Watchable(playerId, AssetKind.Scout)).OrderBy(asset => asset.SortOrder);
}

/// <summary>One ready, visible scout file of a player, read untracked.</summary>
public sealed class ViewerScoutSpec : Specification<Asset>, ISingleResultSpecification<Asset>
{
    /// <summary>Initializes a new instance of the <see cref="ViewerScoutSpec"/> class.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="scoutId">The scout file.</param>
    public ViewerScoutSpec(Guid playerId, Guid scoutId) =>
        Query.Where(ViewerAssetConditions.Watchable(playerId, AssetKind.Scout)).Where(asset => asset.Id == scoutId).AsNoTracking();
}

/// <summary>Lists one page of a player's scout files on a link.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="Paging">The page.</param>
public sealed record GetLinkPlayerScoutsQuery(string Code, string? SessionToken, Guid PlayerId, PageRequest Paging) : IPagedQuery<LinkScoutDto>;

/// <summary>Handles <see cref="GetLinkPlayerScoutsQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="pages">Pages in SQL.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetLinkPlayerScoutsQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    IPagedReader pages,
    TimeProvider timeProvider) : IQueryHandler<GetLinkPlayerScoutsQuery, PagedResult<LinkScoutDto>>
{
    /// <inheritdoc />
    public async Task<PagedResult<LinkScoutDto>> Handle(GetLinkPlayerScoutsQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);

        return await pages.PageAsync(new ViewerScoutsSpec(request.PlayerId), request.Paging, ScoutMapper.ToScoutDto, cancellationToken);
    }
}

/// <summary>Reads one scout file as a report for the page's player (<c>GET /links/{code}/players/{id}/scouts/{scoutId}</c>).</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="ScoutId">The scout file.</param>
public sealed record GetScoutReportQuery(string Code, string? SessionToken, Guid PlayerId, Guid ScoutId) : IQuery<ScoutReportDto>;

/// <summary>
/// Handles <see cref="GetScoutReportQuery"/>: the original is read from storage and parsed on the server, so the file
/// never reaches the browser. A file that cannot be read is 422 <c>scout.unreadable</c>, and its text is never echoed.
/// </summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="assets">The assets.</param>
/// <param name="players">The players.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="storage">Where originals are kept.</param>
/// <param name="cache">Keeps parsed matches for a few minutes.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetScoutReportQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<Asset, Guid> assets,
    IRepository<Player, Guid> players,
    IViewerSecrets secrets,
    IObjectStorage storage,
    IScoutMatchCache cache,
    TimeProvider timeProvider) : IQueryHandler<GetScoutReportQuery, ScoutReportDto>
{
    /// <summary>The code clients see for a file that is not a readable scout file.</summary>
    public const string UnreadableCode = "scout.unreadable";

    /// <inheritdoc />
    public async Task<ScoutReportDto> Handle(GetScoutReportQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);
        var scout = await assets.FirstOrDefaultAsync(new ViewerScoutSpec(request.PlayerId, request.ScoutId), cancellationToken)
            ?? throw new NotFoundException("Scout file");
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken)
            ?? throw new NotFoundException("Player");

        ScoutMatch match;
        try
        {
            match = await cache.GetOrAddAsync($"scout:{scout.Id}:v{scout.Version}", () => LoadAsync(scout, cancellationToken));
        }
        catch (FormatException)
        {
            throw new DomainException(UnreadableCode, "This scout file could not be read.");
        }

        return ScoutMapper.ToReport(scout, match, match.FindPlayer(player.Name?.Value ?? string.Empty));
    }

    /// <summary>Reads the original from storage and parses it.</summary>
    /// <param name="scout">The scout asset.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The match.</returns>
    /// <exception cref="NotFoundException">The original is not in storage.</exception>
    /// <exception cref="FormatException">The text is not a scout file.</exception>
    private async Task<ScoutMatch> LoadAsync(Asset scout, CancellationToken cancellationToken)
    {
        if (scout.OriginalKey is null)
        {
            throw new FormatException("Not a DataVolley scout file.");
        }

        var stored = await storage.GetAsync(scout.OriginalKey.Value, cancellationToken) ?? throw new NotFoundException("Scout file");
        await using (stored.Content)
        {
            using var buffer = new MemoryStream();
            await stored.Content.CopyToAsync(buffer, cancellationToken);
            return DataVolleyFile.Parse(Decode(buffer.ToArray()));
        }
    }

    /// <summary>Decodes the file: UTF-8 when it is valid, otherwise Latin-1 (older DataVolley files are Windows-1252).</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The text.</returns>
    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
