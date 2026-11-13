using System.Text;
using NSubstitute;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.UnitTests.Ingest;

/// <summary>
/// I-3, I-4, I-9: processing a stored original. The original is downloaded into a work folder that is always cleaned up.
/// <list type="bullet">
/// <item>A video is probed, its renditions planned from the frame's short side, each encoded (watermark burned in by
/// the transcoder) and uploaded to <c>…/v{n}/hls/{q}p/</c>, the master playlist written last, and the poster, thumbnail
/// and scrub sprite stored next to the HLS folder. Then the asset is ready.</item>
/// <item>A document (PDF or screenshot) is rendered to page images under <c>…/v{n}/pages/{n}.webp</c> plus a thumbnail,
/// and completed with its page count.</item>
/// <item>A scout file needs no processing: it is read from the original by the V-8 viewer.</item>
/// </list>
/// A processing error fails the asset with its reason instead of retrying forever.
/// </summary>
public sealed class ProcessAssetTests : IDisposable
{
    /// <summary>The bytes of the stored original.</summary>
    private static readonly byte[] _original = Encoding.UTF8.GetBytes("original bytes");

    /// <summary>The player.</summary>
    private readonly Guid _playerId = Guid.CreateVersion7(IngestData.Now);

    /// <summary>The assets.</summary>
    private readonly IRepository<Asset, Guid> _assets = Substitute.For<IRepository<Asset, Guid>>();

    /// <summary>The media bucket.</summary>
    private readonly FakeObjectStorage _storage = new();

    /// <summary>Work folders.</summary>
    private readonly TestWorkspace _workspace = new();

    /// <summary>The stand-in ffmpeg.</summary>
    private readonly FakeTranscoder _transcoder = new();

    /// <summary>The stand-in document renderer.</summary>
    private readonly FakeRenderer _renderer = new();

    /// <summary>A 1080p landscape video is encoded in all three qualities and becomes ready.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_video_is_encoded_and_becomes_ready()
    {
        var video = Stored("Final.mp4");
        _transcoder.Probe = new VideoProbe(1920, 1080, 95.4);

        await Process(video);

        var hls = MediaFolders.VideoHls(_playerId, video.Id, 1);
        video.State.Should().Be(ProcessingState.Ready);
        video.DurationSeconds.Should().Be(95.4);
        video.Renditions.Select(r => (r.Quality, r.Status, r.PlaylistKey!.Value)).Should().Equal(
            (RenditionQuality.P360, RenditionStatus.Ready, $"{hls}/360p/index.m3u8"),
            (RenditionQuality.P720, RenditionStatus.Ready, $"{hls}/720p/index.m3u8"),
            (RenditionQuality.P1080, RenditionStatus.Ready, $"{hls}/1080p/index.m3u8"));
        _transcoder.Encoded.Should().Equal(RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080);
        _transcoder.InputBytes.Should().Equal(_original);
        _storage.ContentType($"{hls}/360p/index.m3u8").Should().Be("application/vnd.apple.mpegurl");
        _storage.ContentType($"{hls}/720p/seg_00000.ts").Should().Be("video/mp2t");
        _workspace.AllCleanedUp.Should().BeTrue();
    }

    /// <summary>The master playlist is written once every rendition is uploaded, so a stream never points at missing files.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_master_playlist_is_written_last()
    {
        var video = Stored("Final.mp4");
        _transcoder.Probe = new VideoProbe(1280, 720, 30);

        await Process(video);

        var master = $"{MediaFolders.VideoHls(_playerId, video.Id, 1)}/{MediaFolders.MasterPlaylist}";
        _storage.ContentType(master).Should().Be("application/vnd.apple.mpegurl");
        Encoding.UTF8.GetString(_storage.Bytes(master)).Should().Be(HlsMaster.Build(_transcoder.Probe, [RenditionQuality.P360, RenditionQuality.P720]));
        _storage.Writes.Where(key => key.Contains("/hls/", StringComparison.Ordinal)).Last().Should().Be(master);
    }

    /// <summary>The poster, thumbnail and sprite (with its WebVTT map) sit next to the HLS folder and are recorded on the asset.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_video_gets_its_previews()
    {
        var video = Stored("Final.mp4");
        _transcoder.Probe = new VideoProbe(640, 360, 12);

        await Process(video);

        var root = MediaFolders.VideoRoot(_playerId, video.Id, 1);
        video.PosterKey!.Value.Should().Be($"{root}/poster.webp");
        video.ThumbnailKey!.Value.Should().Be($"{root}/thumb.webp");
        video.SpriteKey!.Value.Should().Be($"{root}/sprite.webp");
        _storage.ContentType($"{root}/poster.webp").Should().Be("image/webp");
        _storage.ContentType($"{root}/sprite.vtt").Should().Be("text/vtt");
    }

    /// <summary>Renditions are never above the source: a 360p clip gets only 360p.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_small_video_gets_only_360p()
    {
        var video = Stored("phone.mov");
        _transcoder.Probe = new VideoProbe(640, 360, 12);

        await Process(video);

        _transcoder.Encoded.Should().Equal(RenditionQuality.P360);
        video.State.Should().Be(ProcessingState.Ready);
    }

    /// <summary>A portrait phone clip is planned from its short side, so a 1080×1920 clip still gets 1080p.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_portrait_video_is_planned_from_its_short_side()
    {
        var video = Stored("vertical.mp4");
        _transcoder.Probe = new VideoProbe(1080, 1920, 20);

        await Process(video);

        _transcoder.Encoded.Should().Equal(RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080);
    }

    /// <summary>An ffmpeg error fails the asset with its reason; no master playlist is written and the job does not throw.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_encoding_error_fails_the_asset()
    {
        var video = Stored("broken.mp4");
        _transcoder.Probe = new VideoProbe(1920, 1080, 60);
        _transcoder.FailAt = RenditionQuality.P720;

        await Process(video);

        video.State.Should().Be(ProcessingState.Failed);
        video.FailureReason.Should().Contain("moov atom not found");
        _storage.Keys.Should().NotContain(key => key.EndsWith(MediaFolders.MasterPlaylist, StringComparison.Ordinal));
        _workspace.AllCleanedUp.Should().BeTrue();
    }

    /// <summary>A PDF is rendered to one image per page plus a thumbnail and completed with its page count.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_pdf_is_rendered_to_page_images()
    {
        var document = Stored("Stats 2025.pdf");
        _renderer.Pages = 3;

        await Process(document);

        var pages = MediaFolders.DocumentPages(_playerId, document.Id, 1);
        document.State.Should().Be(ProcessingState.Ready);
        document.PageCount.Should().Be(3);
        document.ThumbnailKey!.Value.Should().Be($"{MediaFolders.DocumentRoot(_playerId, document.Id, 1)}/thumb.webp");
        _storage.Keys.Should().Contain([$"{pages}/1.webp", $"{pages}/2.webp", $"{pages}/3.webp"]);
        _storage.ContentType($"{pages}/2.webp").Should().Be("image/webp");
        _renderer.Names.Should().Equal("Stats 2025.pdf");
        _renderer.InputBytes.Should().Equal(_original);
        _workspace.AllCleanedUp.Should().BeTrue();
    }

    /// <summary>A stats screenshot is a one-page document.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_screenshot_is_a_one_page_document()
    {
        var document = Stored("stats.PNG");
        _renderer.Pages = 1;

        await Process(document);

        document.PageCount.Should().Be(1);
        _storage.Keys.Should().Contain($"{MediaFolders.DocumentPages(_playerId, document.Id, 1)}/1.webp");
    }

    /// <summary>A PDF that cannot be rendered (encrypted, corrupt) fails the asset with the reason.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_document_that_cannot_be_rendered_fails()
    {
        var document = Stored("locked.pdf");
        _renderer.Error = "The PDF is password-protected.";

        await Process(document);

        document.State.Should().Be(ProcessingState.Failed);
        document.FailureReason.Should().Contain("password");
    }

    /// <summary>A scout file needs no processing and is ready at once, without a page count.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_scout_file_is_ready_at_once()
    {
        var scout = Stored("&Final.dvw");

        await Process(scout);

        scout.State.Should().Be(ProcessingState.Ready);
        scout.PageCount.Should().BeNull();
        _renderer.Names.Should().BeEmpty();
        _storage.Writes.Should().BeEmpty();
    }

    /// <summary>Hangfire may run a job twice: an asset that is not processing is left alone.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_asset_not_processing_is_left_alone()
    {
        var queued = IngestData.QueuedAsset(_playerId, null, "Final.mp4");
        _assets.GetByIdAsync(queued.Id, Arg.Any<CancellationToken>()).Returns(queued);

        await Process(queued);

        _transcoder.Encoded.Should().BeEmpty();
        queued.State.Should().Be(ProcessingState.Queued);
    }

    /// <summary>Deletes the work folders.</summary>
    public void Dispose() => _workspace.Dispose();

    /// <summary>Stores a processing asset and its original.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The asset.</returns>
    private Asset Stored(string fileName)
    {
        var asset = IngestData.ProcessingAsset(_playerId, fileName);
        _assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(asset);
        _storage.Seed(asset.OriginalKey!.Value, _original);
        return asset;
    }

    /// <summary>Runs the processing step.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>A task that completes when the handler is done.</returns>
    private Task Process(Asset asset) =>
        new ProcessAssetCommandHandler(_assets, _storage, _workspace, _transcoder, _renderer)
            .Handle(new ProcessAssetCommand(asset.Id), TestContext.Current.CancellationToken);

    /// <summary>A stand-in ffmpeg that writes tiny playlists, segments and previews.</summary>
    private sealed class FakeTranscoder : IVideoTranscoder
    {
        /// <summary>Gets or sets what probing answers.</summary>
        public VideoProbe Probe { get; set; } = new(1920, 1080, 60);

        /// <summary>Gets or sets the quality whose encoding fails, if any.</summary>
        public RenditionQuality? FailAt { get; set; }

        /// <summary>Gets the qualities encoded, in order.</summary>
        public List<RenditionQuality> Encoded { get; } = [];

        /// <summary>Gets the bytes of the input file the transcoder was given.</summary>
        public byte[] InputBytes { get; private set; } = [];

        /// <inheritdoc/>
        public async Task<VideoProbe> ProbeAsync(string inputPath, CancellationToken cancellationToken)
        {
            InputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
            return Probe;
        }

        /// <inheritdoc/>
        public async Task TranscodeAsync(string inputPath, VideoProbe probe, RenditionQuality quality, string outputFolder, CancellationToken cancellationToken)
        {
            if (quality == FailAt)
            {
                throw new MediaProcessingException("ffmpeg: moov atom not found");
            }

            Directory.CreateDirectory(outputFolder);
            await File.WriteAllTextAsync(Path.Combine(outputFolder, "index.m3u8"), "#EXTM3U\n#EXTINF:6.0,\nseg_00000.ts\n#EXT-X-ENDLIST\n", cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(outputFolder, "seg_00000.ts"), [0x47, 0x40, 0x00], cancellationToken);
            Encoded.Add(quality);
        }

        /// <inheritdoc/>
        public async Task<VideoPreviewFiles> ExtractPreviewsAsync(string inputPath, VideoProbe probe, string outputFolder, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputFolder);
            var files = new VideoPreviewFiles(
                Path.Combine(outputFolder, "poster.webp"),
                Path.Combine(outputFolder, "thumb.webp"),
                Path.Combine(outputFolder, "sprite.webp"),
                Path.Combine(outputFolder, "sprite.vtt"));
            foreach (var file in new[] { files.Poster, files.Thumbnail, files.Sprite })
            {
                await File.WriteAllBytesAsync(file, [0x52, 0x49, 0x46, 0x46], cancellationToken);
            }

            await File.WriteAllTextAsync(files.SpriteVtt, "WEBVTT\n", cancellationToken);
            return files;
        }
    }

    /// <summary>A stand-in document renderer that writes tiny page images.</summary>
    private sealed class FakeRenderer : IDocumentRenderer
    {
        /// <summary>Gets or sets how many pages the document has.</summary>
        public int Pages { get; set; } = 1;

        /// <summary>Gets or sets an error to fail with, if any.</summary>
        public string? Error { get; set; }

        /// <summary>Gets the original names rendered.</summary>
        public List<string> Names { get; } = [];

        /// <summary>Gets the bytes of the input file the renderer was given.</summary>
        public byte[] InputBytes { get; private set; } = [];

        /// <inheritdoc/>
        public async Task<RenderedDocument> RenderAsync(string inputPath, string originalName, string outputFolder, CancellationToken cancellationToken)
        {
            Names.Add(originalName);
            InputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken);
            if (Error is not null)
            {
                throw new MediaProcessingException(Error);
            }

            Directory.CreateDirectory(outputFolder);
            var pages = new List<string>();
            for (var page = 1; page <= Pages; page++)
            {
                var path = Path.Combine(outputFolder, $"{page}.webp");
                await File.WriteAllBytesAsync(path, [0x52, 0x49, 0x46, 0x46], cancellationToken);
                pages.Add(path);
            }

            var thumbnail = Path.Combine(outputFolder, "thumb.webp");
            await File.WriteAllBytesAsync(thumbnail, [0x52, 0x49, 0x46, 0x46], cancellationToken);
            return new RenderedDocument(pages, thumbnail);
        }
    }
}
