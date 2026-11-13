using ImageMagick;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Infrastructure.Media;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Ingest;

/// <summary>
/// I-3: the ffmpeg arguments (built as a list, never a shell string, so file names cannot inject anything) and the
/// burned-in watermark image. HLS uses 6-second segments with a key frame every 6 seconds, H.264 + AAC at the ladder's
/// bitrates, metadata stripped, and one full-frame transparent PNG overlay holding the logo (top-left) and the slogan
/// (bottom-right) at the S-11 sizes.
/// </summary>
public sealed class FfmpegCommandsTests
{
    /// <summary>A file name with spaces and shell characters.</summary>
    private const string Input = "/work/in put; rm -rf $HOME.mp4";

    /// <summary>The output folder.</summary>
    private const string Output = "/work/720p";

    /// <summary>The watermark overlay.</summary>
    private const string Watermark = "/work/watermark-1280x720.png";

    /// <summary>The 720p arguments of a 1080p landscape source.</summary>
    private static readonly IReadOnlyList<string> _args =
        FfmpegCommands.Transcode(Input, new VideoProbe(1920, 1080, 60), RenditionQuality.P720, Watermark, Output);

    /// <summary>The input and the watermark are separate inputs, passed as whole arguments.</summary>
    [Fact]
    public void Inputs_are_whole_arguments()
    {
        Pair("-i", Input);
        Pair("-i", Watermark);
    }

    /// <summary>HLS VOD with 6-second segments named <c>seg_00000.ts</c>, playlist <c>index.m3u8</c> last.</summary>
    [Fact]
    public void Output_is_six_second_hls()
    {
        Pair("-f", "hls");
        Pair("-hls_time", "6");
        Pair("-hls_playlist_type", "vod");
        Pair("-hls_segment_filename", Path.Combine(Output, "seg_%05d.ts"));
        Pair("-force_key_frames", "expr:gte(t,n_forced*6)");
        _args[^1].Should().Be(Path.Combine(Output, "index.m3u8"));
    }

    /// <summary>H.264 and AAC at the ladder's bitrates, a pixel format every browser plays, no metadata (no GPS).</summary>
    [Fact]
    public void Codecs_follow_the_ladder()
    {
        var rung = RenditionLadder.For(RenditionQuality.P720);
        Pair("-c:v", "libx264");
        Pair("-b:v", $"{rung.VideoKbps}k");
        Pair("-c:a", "aac");
        Pair("-b:a", $"{rung.AudioKbps}k");
        Pair("-pix_fmt", "yuv420p");
        Pair("-map_metadata", "-1");
    }

    /// <summary>The frame is scaled to the rung's size and the watermark laid over it; a missing audio track is fine.</summary>
    [Fact]
    public void The_watermark_is_overlaid_on_the_scaled_frame()
    {
        var filter = Value("-filter_complex");

        filter.Should().Contain("scale=1280:720").And.Contain("overlay");
        _args.Should().Contain("0:a?");
    }

    /// <summary>Probing asks ffprobe for JSON; the input is a whole argument.</summary>
    [Fact]
    public void Probing_asks_for_json()
    {
        var probe = FfmpegCommands.Probe(Input);

        probe.Should().ContainInConsecutiveOrder("-of", "json");
        probe[^1].Should().Be(Input);
    }

    /// <summary>The watermark image is the frame's size, transparent in the middle, with the logo top-left and the slogan bottom-right.</summary>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(640, 360)]
    public void The_watermark_image_has_the_logo_and_the_slogan_in_their_corners(int width, int height)
    {
        var layout = WatermarkLayout.For(width, height);

        using var image = new MagickImage(WatermarkImage.RenderPng(width, height));
        using var pixels = image.GetPixels();

        image.Width.Should().Be((uint)width);
        image.Height.Should().Be((uint)height);
        image.HasAlpha.Should().BeTrue();
        Alpha(pixels, width / 2, height / 2).Should().Be(0);
        HasInk(pixels, layout.Margin, layout.Margin, layout.LogoWidth, layout.LogoWidth / 3).Should().BeTrue("the logo sits top-left");
        HasInk(pixels, width / 2, height - layout.Margin - layout.SloganHeight, (width / 2) - layout.Margin, layout.SloganHeight).Should().BeTrue("the slogan sits bottom-right");
        HasInk(pixels, layout.Margin, height - layout.Margin - layout.SloganHeight, width / 3, layout.SloganHeight).Should().BeFalse("bottom-left stays clear");
    }

    /// <summary>Asserts that a flag is directly followed by a value.</summary>
    /// <param name="flag">The flag.</param>
    /// <param name="value">The value.</param>
    private static void Pair(string flag, string value) =>
        _args.Should().ContainInConsecutiveOrder(flag, value);

    /// <summary>Reads the value after a flag.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>The value.</returns>
    private static string Value(string flag)
    {
        var index = _args.ToList().IndexOf(flag);
        index.Should().BeGreaterThanOrEqualTo(0, $"{flag} is passed");
        return _args[index + 1];
    }

    /// <summary>Reads a pixel's alpha (0 = transparent).</summary>
    /// <param name="pixels">The pixels.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The alpha.</returns>
    private static float Alpha(IPixelCollection<byte> pixels, int x, int y) => pixels.GetPixel(x, y).ToColor()!.A;

    /// <summary>Tells whether any pixel in a box is not fully transparent.</summary>
    /// <param name="pixels">The pixels.</param>
    /// <param name="left">The box's left edge.</param>
    /// <param name="top">The box's top edge.</param>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <returns><see langword="true"/> when something is drawn there.</returns>
    private static bool HasInk(IPixelCollection<byte> pixels, int left, int top, int width, int height)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                if (Alpha(pixels, x, y) > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
