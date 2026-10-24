using Riok.Mapperly.Abstractions;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Players;

/// <summary>
/// Maps <see cref="Player"/> to its DTOs at compile time (Mapperly), flattening value objects and enums to plain values.
/// Every DTO member must be mapped, or the build fails.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class PlayerMapper
{
    /// <summary>Maps a player to its list DTO.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The DTO.</returns>
    [MapProperty([nameof(Player.Nationality), nameof(Nationality.Code)], nameof(PlayerSummaryDto.NationalityCode))]
    [MapProperty([nameof(Player.Nationality), nameof(Nationality.CountryName)], nameof(PlayerSummaryDto.NationalityName))]
    [MapProperty(nameof(Player.Photo), nameof(PlayerSummaryDto.PhotoKey))]
    public static partial PlayerSummaryDto ToSummaryDto(this Player player);

    /// <summary>Maps a player to its details DTO.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The DTO.</returns>
    [MapProperty([nameof(Player.Nationality), nameof(Nationality.Code)], nameof(PlayerDetailsDto.NationalityCode))]
    [MapProperty([nameof(Player.Nationality), nameof(Nationality.CountryName)], nameof(PlayerDetailsDto.NationalityName))]
    [MapProperty(nameof(Player.Photo), nameof(PlayerDetailsDto.PhotoKey))]
    [MapProperty([nameof(Player.Volleybox), nameof(VolleyboxProfile.PlayerId)], nameof(PlayerDetailsDto.VolleyboxId))]
    [MapProperty([nameof(Player.Volleybox), nameof(VolleyboxProfile.Url)], nameof(PlayerDetailsDto.VolleyboxUrl))]
    [MapProperty(nameof(Player.QrCode), nameof(PlayerDetailsDto.QrCodeKey))]
    [MapPropertyFromSource(nameof(PlayerDetailsDto.MissingDetails), Use = nameof(MissingDetailNames))]
    public static partial PlayerDetailsDto ToDetailsDto(this Player player);

    /// <summary>Turns a name into its text.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The text.</returns>
    private static string FromFullName(FullName name) => name.Value;

    /// <summary>Turns a slug into its text.</summary>
    /// <param name="slug">The slug.</param>
    /// <returns>The text.</returns>
    private static string FromSlug(Slug slug) => slug.Value;

    /// <summary>Turns a photo into its storage key.</summary>
    /// <param name="photo">The photo.</param>
    /// <returns>The key.</returns>
    private static string FromPhoto(PlayerPhoto photo) => photo.StorageKey;

    /// <summary>Turns a storage key into its text.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The text.</returns>
    private static string FromStorageKey(Domain.Primitives.StorageKey key) => key.Value;

    /// <summary>Turns positions into their codes, primary first; empty while unknown.</summary>
    /// <param name="positions">The positions, or <see langword="null"/>.</param>
    /// <returns>The codes.</returns>
    private static IReadOnlyList<string> FromPositions(PlayerPositions? positions) =>
        positions is null ? [] : positions.Items.Select(position => position.Code()).ToList();

    /// <summary>Lists the names of the details still needed before publishing.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The detail names.</returns>
    private static IReadOnlyList<string> MissingDetailNames(Player player) =>
        player.MissingDetails().Select(detail => detail.ToString()).ToList();
}
