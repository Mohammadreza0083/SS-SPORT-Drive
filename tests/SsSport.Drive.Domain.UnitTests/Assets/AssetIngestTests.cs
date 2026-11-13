using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Assets;

/// <summary>
/// What ingest (I-4, I-6) adds to the Asset aggregate: preview images (a video's poster, thumbnail and scrub sprite; a
/// document's thumbnail), dropped again when the content is replaced or retried, and a file that comes back to Drive
/// after it was removed.
/// </summary>
public sealed class AssetIngestTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The owning player.</summary>
    private static readonly Guid _playerId = Guid.CreateVersion7(_now);

    /// <summary>A valid md5.</summary>
    private const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>A different valid md5.</summary>
    private const string OtherMd5 = "e4d909c290d0fb1ca068ffaddf22cbd0";

    /// <summary>A video being processed takes its poster, thumbnail and sprite.</summary>
    [Fact]
    public void A_video_takes_its_previews_while_processing()
    {
        var video = ProcessingAsset("game.mp4");

        video.AttachVideoPreviews(Key("poster.webp"), Key("thumb.webp"), Key("sprite.webp"));

        video.PosterKey!.Value.Should().EndWith("poster.webp");
        video.ThumbnailKey!.Value.Should().EndWith("thumb.webp");
        video.SpriteKey!.Value.Should().EndWith("sprite.webp");
    }

    /// <summary>Only videos have a poster and a sprite.</summary>
    [Fact]
    public void A_document_cannot_take_video_previews()
    {
        var document = ProcessingAsset("stats.pdf");

        var act = () => document.AttachVideoPreviews(Key("poster.webp"), Key("thumb.webp"), Key("sprite.webp"));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("asset.not_a_video");
    }

    /// <summary>A document (PDF or screenshot) being processed takes its thumbnail; it has no poster or sprite.</summary>
    [Fact]
    public void A_document_takes_its_thumbnail_while_processing()
    {
        var document = ProcessingAsset("stats.pdf");

        document.AttachDocumentThumbnail(Key("thumb.webp"));

        document.ThumbnailKey!.Value.Should().EndWith("thumb.webp");
        document.PosterKey.Should().BeNull();
        document.SpriteKey.Should().BeNull();
    }

    /// <summary>Videos get their thumbnail through <see cref="Asset.AttachVideoPreviews"/>, scout files get none.</summary>
    /// <param name="fileName">The file.</param>
    [Theory]
    [InlineData("game.mp4")]
    [InlineData("&game.dvw")]
    public void Only_documents_take_a_document_thumbnail(string fileName)
    {
        var asset = ProcessingAsset(fileName);

        var act = () => asset.AttachDocumentThumbnail(Key("thumb.webp"));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("asset.not_a_document");
    }

    /// <summary>Previews are part of processing: not before the original is stored, not after the asset is ready.</summary>
    [Fact]
    public void Previews_are_attached_only_while_processing()
    {
        var queued = Asset.Add(_playerId, null, AssetSource.Drive, "f-1", AssetFile.Create("game.mp4", Md5, 10), "Game", AssetTags.FullMatch, 0, _now);
        var document = ProcessingAsset("stats.pdf");
        document.CompleteDocument(3);

        var early = () => queued.AttachVideoPreviews(Key("poster.webp"), Key("thumb.webp"), Key("sprite.webp"));
        var late = () => document.AttachDocumentThumbnail(Key("thumb.webp"));

        early.Should().Throw<DomainException>().Which.Code.Should().Be("asset.invalid_state");
        late.Should().Throw<DomainException>().Which.Code.Should().Be("asset.invalid_state");
    }

    /// <summary>New content drops the old previews, like the renditions, so a replaced video never shows an old poster.</summary>
    [Fact]
    public void Replacing_the_content_drops_the_previews()
    {
        var video = ProcessingAsset("game.mp4");
        video.AttachVideoPreviews(Key("poster.webp"), Key("thumb.webp"), Key("sprite.webp"));

        video.ReplaceContent(AssetFile.Create("game.mp4", OtherMd5, 20));

        video.PosterKey.Should().BeNull();
        video.ThumbnailKey.Should().BeNull();
        video.SpriteKey.Should().BeNull();
    }

    /// <summary>A retry starts from scratch, previews included.</summary>
    [Fact]
    public void Retrying_drops_the_previews()
    {
        var document = ProcessingAsset("stats.pdf");
        document.AttachDocumentThumbnail(Key("thumb.webp"));
        document.Fail("PDF is encrypted.");

        document.Retry();

        document.ThumbnailKey.Should().BeNull();
    }

    /// <summary>A file removed from Drive and put back is shown again and no longer marked removed.</summary>
    [Fact]
    public void A_file_that_comes_back_to_Drive_is_shown_again()
    {
        var asset = Asset.Add(_playerId, null, AssetSource.Drive, "f-1", AssetFile.Create("game.mp4", Md5, 10), "Game", AssetTags.FullMatch, 0, _now);
        asset.MarkRemovedFromSource(_now);

        asset.ReturnToSource();

        asset.RemovedFromSourceAt.Should().BeNull();
        asset.Visibility.Should().Be(AssetVisibility.Visible);
    }

    /// <summary>Returning only undoes a removal: a file an admin hid by hand stays hidden.</summary>
    [Fact]
    public void Returning_leaves_a_hand_hidden_file_hidden()
    {
        var asset = Asset.Add(_playerId, null, AssetSource.Drive, "f-1", AssetFile.Create("game.mp4", Md5, 10), "Game", AssetTags.FullMatch, 0, _now);
        asset.Hide();

        asset.ReturnToSource();

        asset.Visibility.Should().Be(AssetVisibility.Hidden);
        asset.RemovedFromSourceAt.Should().BeNull();
    }

    /// <summary>Builds an asset whose original is stored and which is now processing.</summary>
    /// <param name="fileName">The file name; its extension decides the kind.</param>
    /// <returns>The asset.</returns>
    private static Asset ProcessingAsset(string fileName)
    {
        var asset = Asset.Add(_playerId, null, AssetSource.Drive, "f-1", AssetFile.Create(fileName, Md5, 10), "Title", AssetTags.Untagged, 0, _now);
        asset.AttachOriginal(StorageKey.Create($"originals/{Md5}{Path.GetExtension(fileName)}"));
        return asset;
    }

    /// <summary>Builds a preview key under the asset folder.</summary>
    /// <param name="file">The file name.</param>
    /// <returns>The key.</returns>
    private static StorageKey Key(string file) => StorageKey.Create($"players/{_playerId}/videos/x/v1/{file}");
}
