using Riok.Mapperly.Abstractions;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Assets;

/// <summary>Maps <see cref="Asset"/> and its <see cref="Rendition"/>s to DTOs at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class AssetMapper
{
    /// <summary>The individual tags, in declaration order (without <see cref="AssetTags.Untagged"/>).</summary>
    private static readonly AssetTags[] _tags = Enum.GetValues<AssetTags>().Where(tag => tag != AssetTags.Untagged).ToArray();

    /// <summary>Maps an asset to its DTO.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The DTO.</returns>
    [MapProperty([nameof(Asset.File), nameof(AssetFile.OriginalName)], nameof(AssetDto.OriginalName))]
    [MapProperty([nameof(Asset.File), nameof(AssetFile.SizeBytes)], nameof(AssetDto.SizeBytes))]
    public static partial AssetDto ToDto(this Asset asset);

    /// <summary>Maps an asset to its row in the Mini App player detail; the caller sets <see cref="MiniApp.PlayerFileDto.IsHero"/>.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The DTO.</returns>
    [MapperIgnoreTarget(nameof(MiniApp.PlayerFileDto.IsHero))]
    public static partial MiniApp.PlayerFileDto ToFileDto(this Asset asset);

    /// <summary>Maps a rendition to its DTO; the quality becomes its frame height.</summary>
    /// <param name="rendition">The rendition.</param>
    /// <returns>The DTO.</returns>
    private static partial RenditionDto ToDto(Rendition rendition);

    /// <summary>Turns a quality into its frame height.</summary>
    /// <param name="quality">The quality.</param>
    /// <returns>360, 720 or 1080.</returns>
    private static int FromQuality(RenditionQuality quality) => (int)quality;

    /// <summary>Turns a storage key into its text.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The text.</returns>
    private static string FromStorageKey(StorageKey key) => key.Value;

    /// <summary>Lists the names of the set tags; empty when untagged.</summary>
    /// <param name="tags">The tags.</param>
    /// <returns>The tag names.</returns>
    private static IReadOnlyList<string> FromTags(AssetTags tags) =>
        _tags.Where(tag => tags.HasFlag(tag)).Select(tag => tag.ToString()).ToList();
}
