using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.UnitTests.Ingest;

/// <summary>
/// The pure rules of I-3 / I-4 / I-9: the rendition ladder (bitrates and frame sizes, portrait clips included), the
/// HLS master playlist, the watermark layout of S-11 (decisions §6), and the R2 folders the viewer's edge tokens point at.
/// </summary>
public sealed class IngestRulesTests
{
    /// <summary>The player.</summary>
    private static readonly Guid _player = Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10");

    /// <summary>The asset.</summary>
    private static readonly Guid _asset = Guid.Parse("0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f61");

    /// <summary>Video and audio bitrates per quality (proposed, kbit/s).</summary>
    /// <param name="quality">The quality.</param>
    /// <param name="videoKbps">The video bitrate.</param>
    /// <param name="audioKbps">The audio bitrate.</param>
    [Theory]
    [InlineData(RenditionQuality.P360, 800, 96)]
    [InlineData(RenditionQuality.P720, 2800, 128)]
    [InlineData(RenditionQuality.P1080, 5000, 128)]
    public void Each_quality_has_its_bitrates(RenditionQuality quality, int videoKbps, int audioKbps)
    {
        var rung = RenditionLadder.For(quality);

        rung.VideoKbps.Should().Be(videoKbps);
        rung.AudioKbps.Should().Be(audioKbps);
        RenditionLadder.Bandwidth(quality).Should().Be((videoKbps + audioKbps) * 1000);
    }

    /// <summary>
    /// The quality is the frame's short side, so a portrait phone clip gets the same sharpness as a landscape one. The
    /// long side keeps the aspect ratio, rounded to an even number (H.264 needs even sizes).
    /// </summary>
    /// <param name="quality">The quality.</param>
    /// <param name="sourceWidth">The source width.</param>
    /// <param name="sourceHeight">The source height.</param>
    /// <param name="width">The expected width.</param>
    /// <param name="height">The expected height.</param>
    [Theory]
    [InlineData(RenditionQuality.P720, 1920, 1080, 1280, 720)]
    [InlineData(RenditionQuality.P1080, 1920, 1080, 1920, 1080)]
    [InlineData(RenditionQuality.P720, 1080, 1920, 720, 1280)]
    [InlineData(RenditionQuality.P360, 1440, 1080, 480, 360)]
    [InlineData(RenditionQuality.P360, 854, 480, 640, 360)]
    [InlineData(RenditionQuality.P720, 1918, 1080, 1278, 720)]
    public void Frame_sizes_keep_the_aspect_ratio(RenditionQuality quality, int sourceWidth, int sourceHeight, int width, int height) =>
        RenditionLadder.FrameSize(quality, sourceWidth, sourceHeight).Should().Be((width, height));

    /// <summary>The master playlist lists every rendition, lowest first, with its bandwidth, frame size and relative playlist.</summary>
    [Fact]
    public void The_master_playlist_lists_every_rendition()
    {
        var master = HlsMaster.Build(new VideoProbe(1920, 1080, 95.4), [RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080]);

        var lines = master.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[0].Should().Be("#EXTM3U");
        var streams = lines.Select((line, index) => (line, index)).Where(item => item.line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)).ToList();
        streams.Should().HaveCount(3);
        streams[0].line.Should().Contain($"BANDWIDTH={RenditionLadder.Bandwidth(RenditionQuality.P360)}").And.Contain("RESOLUTION=640x360");
        streams[1].line.Should().Contain($"BANDWIDTH={RenditionLadder.Bandwidth(RenditionQuality.P720)}").And.Contain("RESOLUTION=1280x720");
        streams[2].line.Should().Contain($"BANDWIDTH={RenditionLadder.Bandwidth(RenditionQuality.P1080)}").And.Contain("RESOLUTION=1920x1080");
        lines[streams[0].index + 1].Should().Be("360p/index.m3u8");
        lines[streams[1].index + 1].Should().Be("720p/index.m3u8");
        lines[streams[2].index + 1].Should().Be("1080p/index.m3u8");
    }

    /// <summary>A portrait clip's master playlist gives portrait frame sizes.</summary>
    [Fact]
    public void A_portrait_master_playlist_has_portrait_sizes() =>
        HlsMaster.Build(new VideoProbe(1080, 1920, 30), [RenditionQuality.P360]).Should().Contain("RESOLUTION=360x640");

    /// <summary>The watermark scales with the frame (decisions §6, S-11): logo 10 % of the width, margins 2.5 %, slogan 3.2 % of the height.</summary>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <param name="logoWidth">The logo width.</param>
    /// <param name="margin">The margin from each edge.</param>
    /// <param name="sloganHeight">The slogan's text height.</param>
    [Theory]
    [InlineData(1920, 1080, 192, 48, 35)]
    [InlineData(1280, 720, 128, 32, 23)]
    [InlineData(640, 360, 64, 16, 12)]
    public void The_watermark_scales_with_the_frame(int width, int height, int logoWidth, int margin, int sloganHeight)
    {
        var layout = WatermarkLayout.For(width, height);

        layout.LogoWidth.Should().Be(logoWidth);
        layout.Margin.Should().Be(margin);
        layout.SloganHeight.Should().Be(sloganHeight);
    }

    /// <summary>The approved opacities: logo 45 %, slogan 55 %, a soft 35 % shadow; the slogan text is fixed.</summary>
    [Fact]
    public void The_watermark_uses_the_approved_opacities()
    {
        var layout = WatermarkLayout.For(1920, 1080);

        layout.LogoOpacity.Should().Be(0.45);
        layout.SloganOpacity.Should().Be(0.55);
        layout.ShadowOpacity.Should().Be(0.35);
        WatermarkLayout.Slogan.Should().Be("SS-SPORT · Fetch Your Dreams");
    }

    /// <summary>I-9: a video version's folder holds its HLS folder (V-5b) and its previews.</summary>
    [Fact]
    public void A_video_version_has_one_folder()
    {
        MediaFolders.VideoRoot(_player, _asset, 2).Should().Be($"players/{_player}/videos/{_asset}/v2");
        MediaFolders.VideoHls(_player, _asset, 2).Should().Be($"{MediaFolders.VideoRoot(_player, _asset, 2)}/hls");
    }

    /// <summary>I-9: a document version's folder holds its page images (V-5b) and its thumbnail.</summary>
    [Fact]
    public void A_document_version_has_one_folder()
    {
        MediaFolders.DocumentRoot(_player, _asset, 3).Should().Be($"players/{_player}/documents/{_asset}/v3");
        MediaFolders.DocumentPages(_player, _asset, 3).Should().Be($"{MediaFolders.DocumentRoot(_player, _asset, 3)}/pages");
    }
}
