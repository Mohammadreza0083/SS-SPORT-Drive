using Riok.Mapperly.Abstractions;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Links;

/// <summary>Compile-time mappings for what a signed-in viewer reads behind <c>/r/{code}</c> (V-2b).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ViewerContentMapper
{
    /// <summary>The tag shown for a video without tags.</summary>
    public const string UntaggedLabel = "Video";

    /// <summary>The tags in the order the first one is picked for the chips.</summary>
    private static readonly AssetTags[] _tagOrder = Enum.GetValues<AssetTags>().Where(tag => tag != AssetTags.Untagged).ToArray();

    /// <summary>Builds the link overview.</summary>
    /// <param name="link">The open link.</param>
    /// <param name="recipient">Who it was prepared for.</param>
    /// <param name="session">The viewer's session.</param>
    /// <returns>The overview.</returns>
    public static LinkContentDto ToContentDto(this ShareLink link, Recipient recipient, ViewerSession session) => new()
    {
        Type = link.Type.ToString(),
        PlayerCount = link.PlayerIds.Count,
        Recipient = recipient.ToLinkRecipientDto(),
        Note = link.Note,
        ViewerEmail = session.Email.Value,
    };

    /// <summary>Maps the recipient's public facts; their emails stay behind.</summary>
    /// <param name="recipient">The recipient.</param>
    /// <returns>The name and organisation.</returns>
    public static partial LinkRecipientDto ToLinkRecipientDto(this Recipient recipient);

    /// <summary>Maps a player's facts to a roster card.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The card.</returns>
    [MapProperty(nameof(LinkPlayerFacts.Name), nameof(LinkPlayerCardDto.FirstName), Use = nameof(FirstName))]
    [MapProperty(nameof(LinkPlayerFacts.Positions), nameof(LinkPlayerCardDto.Position), Use = nameof(PrimaryPosition))]
    [MapProperty(nameof(LinkPlayerFacts.NationalityCode), nameof(LinkPlayerCardDto.Nationality), Use = nameof(Country))]
    public static partial LinkPlayerCardDto ToCardDto(this LinkPlayerFacts facts);

    /// <summary>Maps a player's facts to the player page header.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The header.</returns>
    [MapProperty(nameof(LinkPlayerFacts.Name), nameof(LinkPlayerDto.FirstName), Use = nameof(FirstName))]
    [MapProperty(nameof(LinkPlayerFacts.Positions), nameof(LinkPlayerDto.Position), Use = nameof(PrimaryPosition))]
    [MapProperty(nameof(LinkPlayerFacts.NationalityCode), nameof(LinkPlayerDto.Nationality), Use = nameof(Country))]
    [MapPropertyFromSource(nameof(LinkPlayerDto.Stats), Use = nameof(Stats))]
    public static partial LinkPlayerDto ToPlayerDto(this LinkPlayerFacts facts);

    /// <summary>Maps a ready video.</summary>
    /// <param name="asset">The video.</param>
    /// <returns>The video row.</returns>
    [MapProperty(nameof(Asset.Tags), nameof(LinkVideoDto.Tag), Use = nameof(TagLabel))]
    [MapProperty(nameof(Asset.DurationSeconds), nameof(LinkVideoDto.DurationSeconds), Use = nameof(WholeSeconds))]
    public static partial LinkVideoDto ToVideoDto(this Asset asset);

    /// <summary>Maps a ready document.</summary>
    /// <param name="asset">The document.</param>
    /// <returns>The document row.</returns>
    [MapProperty(nameof(Asset.PageCount), nameof(LinkDocumentDto.PageCount), Use = nameof(PagesOrOne))]
    public static partial LinkDocumentDto ToDocumentDto(this Asset asset);

    /// <summary>The label of a video's first tag, as the chips show it.</summary>
    /// <param name="tags">The tags.</param>
    /// <returns>e.g. "Full match", or <see cref="UntaggedLabel"/>.</returns>
    [UserMapping(Default = false)]
    public static string TagLabel(AssetTags tags)
    {
        foreach (var tag in _tagOrder)
        {
            if (tags.HasFlag(tag))
            {
                return tag == AssetTags.FullMatch ? "Full match" : tag.ToString();
            }
        }

        return UntaggedLabel;
    }

    /// <summary>The first name: the name up to its first space.</summary>
    /// <param name="name">The full name.</param>
    /// <returns>The first name.</returns>
    [UserMapping(Default = false)]
    private static string FirstName(string name)
    {
        var space = name.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? name[..space] : name;
    }

    /// <summary>The primary position with its label.</summary>
    /// <param name="codes">The position codes, primary first.</param>
    /// <returns>The primary position.</returns>
    [UserMapping(Default = false)]
    private static PositionDto PrimaryPosition(IReadOnlyList<string> codes) =>
        codes.Count > 0 && PositionParser.TryParse(codes[0], out var position)
            ? new PositionDto { Code = position.Code(), Label = position.Label() }
            : new PositionDto();

    /// <summary>The country with its English name.</summary>
    /// <param name="code">The nationality code.</param>
    /// <returns>The country.</returns>
    [UserMapping(Default = false)]
    private static CountryDto Country(string code)
    {
        var nationality = Nationality.Create(code);
        return new CountryDto { Code = nationality.Code, Name = nationality.CountryName };
    }

    /// <summary>The stats row from the counts.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The stats.</returns>
    [UserMapping(Default = false)]
    private static PlayerStatsDto Stats(LinkPlayerFacts facts) => new()
    {
        Videos = facts.VideoCount,
        Minutes = (int)Math.Round(facts.VideoSeconds / 60, MidpointRounding.AwayFromZero),
        Documents = facts.DocumentCount,
        Matches = facts.ScoutCount,
    };

    /// <summary>A length in whole seconds.</summary>
    /// <param name="seconds">The length, if known.</param>
    /// <returns>The rounded length, 0 when unknown.</returns>
    [UserMapping(Default = false)]
    private static int WholeSeconds(double? seconds) => seconds is { } value ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : 0;

    /// <summary>A document's page count; images and unknown counts are one page.</summary>
    /// <param name="pages">The page count, if known.</param>
    /// <returns>The page count.</returns>
    [UserMapping(Default = false)]
    private static int PagesOrOne(int? pages) => pages ?? 1;
}
