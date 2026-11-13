using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Assets;

/// <summary>
/// T-6b: every step an asset takes in its pipeline raises <see cref="AssetProgressed"/>, so the analyst's progress
/// message can follow the copy, the transcode and the pages as they happen. The last step raises
/// <see cref="AssetReady"/> only.
/// </summary>
public sealed class AssetProgressEventsTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The owning player.</summary>
    private static readonly Guid _playerId = Guid.CreateVersion7(_now);

    /// <summary>A valid md5.</summary>
    private const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>Starting the copy is a step forward.</summary>
    [Fact]
    public void Starting_the_copy_raises_progressed()
    {
        var asset = Video();
        asset.ClearDomainEvents();

        asset.StartCopying();

        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetProgressed(asset.Id, _playerId));
    }

    /// <summary>Storing the original is a step forward.</summary>
    [Fact]
    public void Storing_the_original_raises_progressed()
    {
        var asset = Video();
        asset.StartCopying();
        asset.ClearDomainEvents();

        asset.AttachOriginal(StorageKey.Create("originals/p/a/game.mp4"));

        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetProgressed(asset.Id, _playerId));
    }

    /// <summary>Planning the renditions is a step forward: the message now knows how many there are.</summary>
    [Fact]
    public void Planning_the_renditions_raises_progressed()
    {
        var asset = Processing(Video());
        asset.ClearDomainEvents();

        asset.RecordVideoInfo(sourceHeight: 720, durationSeconds: 600);

        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetProgressed(asset.Id, _playerId));
    }

    /// <summary>A rendition that is not the last raises progressed; the last one raises ready only.</summary>
    [Fact]
    public void Each_rendition_but_the_last_raises_progressed()
    {
        var asset = Processing(Video());
        asset.RecordVideoInfo(sourceHeight: 720, durationSeconds: 600);
        asset.ClearDomainEvents();

        asset.MarkRenditionReady(RenditionQuality.P360, StorageKey.Create("hls/a/360/index.m3u8"));
        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetProgressed(asset.Id, _playerId));
        asset.ClearDomainEvents();

        asset.MarkRenditionReady(RenditionQuality.P720, StorageKey.Create("hls/a/720/index.m3u8"));
        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetReady(asset.Id, _playerId));
    }

    /// <summary>The whole life of a 1080p video: five steps forward, then ready.</summary>
    [Fact]
    public void A_video_raises_every_step_in_order()
    {
        var asset = Video();
        asset.ClearDomainEvents();

        asset.StartCopying();
        asset.AttachOriginal(StorageKey.Create("originals/p/a/game.mp4"));
        asset.RecordVideoInfo(sourceHeight: 1080, durationSeconds: 5400);
        asset.MarkRenditionReady(RenditionQuality.P360, StorageKey.Create("hls/a/360/index.m3u8"));
        asset.MarkRenditionReady(RenditionQuality.P720, StorageKey.Create("hls/a/720/index.m3u8"));
        asset.MarkRenditionReady(RenditionQuality.P1080, StorageKey.Create("hls/a/1080/index.m3u8"));

        var progressed = new AssetProgressed(asset.Id, _playerId);
        asset.DomainEvents.Should().Equal(progressed, progressed, progressed, progressed, progressed, new AssetReady(asset.Id, _playerId));
    }

    /// <summary>A document goes straight from processing to ready: no progressed event for the last step.</summary>
    [Fact]
    public void Completing_a_document_raises_ready_only()
    {
        var asset = Processing(Document());
        asset.ClearDomainEvents();

        asset.CompleteDocument(pageCount: 4);

        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetReady(asset.Id, _playerId));
    }

    /// <summary>Queueing a failed asset again moves it back into the build, so the message shows it again.</summary>
    [Fact]
    public void Retrying_a_failed_asset_raises_progressed()
    {
        var asset = Processing(Video());
        asset.Fail("ffmpeg exited with code 1");
        asset.ClearDomainEvents();

        asset.Retry();

        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetProgressed(asset.Id, _playerId));
    }

    /// <summary>Builds a queued Drive video.</summary>
    /// <returns>The asset.</returns>
    private static Asset Video() =>
        Asset.Add(_playerId, null, AssetSource.Drive, "drive-file", AssetFile.Create("game.mp4", Md5, 1000), "Game", AssetTags.FullMatch, 0, _now);

    /// <summary>Builds a queued Drive PDF.</summary>
    /// <returns>The asset.</returns>
    private static Asset Document() =>
        Asset.Add(_playerId, null, AssetSource.Drive, "drive-pdf", AssetFile.Create("report.pdf", Md5, 1000), "Report", AssetTags.Untagged, 1, _now);

    /// <summary>Moves an asset into processing.</summary>
    /// <param name="asset">The queued asset.</param>
    /// <returns>The same asset.</returns>
    private static Asset Processing(Asset asset)
    {
        asset.StartCopying();
        asset.AttachOriginal(StorageKey.Create("originals/p/a/file"));
        return asset;
    }
}
