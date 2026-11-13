using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Infrastructure.Media;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Ingest;

/// <summary>
/// I-3 / I-4 against the real ffmpeg (the Workers image ships it; GitHub's Ubuntu runners have it). The tests make an
/// 8-second black clip with a tone, then probe, encode and preview it. They are skipped where ffmpeg is not installed.
/// </summary>
public sealed partial class FfmpegVideoTranscoderTests : IDisposable
{
    /// <summary>The work folder.</summary>
    private readonly string _work = Directory.CreateTempSubdirectory("ssd-ffmpeg-test-").FullName;

    /// <summary>The transcoder under test.</summary>
    private readonly FfmpegVideoTranscoder _transcoder = new(Options.Create(new MediaOptions()), NullLogger<FfmpegVideoTranscoder>.Instance);

    /// <summary>Probing reads the frame size and the duration.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Probing_reads_size_and_duration()
    {
        var source = await SourceAsync();

        var probe = await _transcoder.ProbeAsync(source, TestContext.Current.CancellationToken);

        (probe.Width, probe.Height).Should().Be((1280, 720));
        probe.DurationSeconds.Should().BeApproximately(8, 0.3);
    }

    /// <summary>A file that is not a video fails with a processing error, not a crash.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_file_that_is_not_a_video_fails_cleanly()
    {
        RequireFfmpeg();
        var broken = Path.Combine(_work, "broken.mp4");
        await File.WriteAllTextAsync(broken, "not a video", TestContext.Current.CancellationToken);

        var act = () => _transcoder.ProbeAsync(broken, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<MediaProcessingException>();
    }

    /// <summary>A rendition is a VOD playlist of segments no longer than 6 seconds, all present.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_rendition_is_six_second_hls()
    {
        var source = await SourceAsync();
        var probe = await _transcoder.ProbeAsync(source, TestContext.Current.CancellationToken);
        var output = Path.Combine(_work, "360p");

        await _transcoder.TranscodeAsync(source, probe, RenditionQuality.P360, output, TestContext.Current.CancellationToken);

        var playlist = await File.ReadAllTextAsync(Path.Combine(output, "index.m3u8"), TestContext.Current.CancellationToken);
        playlist.Should().Contain("#EXT-X-PLAYLIST-TYPE:VOD").And.Contain("#EXT-X-ENDLIST");
        var durations = ExtInf().Matches(playlist).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        durations.Should().HaveCountGreaterThanOrEqualTo(2).And.OnlyContain(d => d <= 6.5);
        foreach (var segment in playlist.Split('\n').Where(line => line.EndsWith(".ts", StringComparison.Ordinal)))
        {
            File.Exists(Path.Combine(output, segment.Trim())).Should().BeTrue();
        }
    }

    /// <summary>The watermark is burned in: on a black clip the top-left corner has the logo, the middle stays black.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_watermark_is_burned_in()
    {
        var source = await SourceAsync();
        var probe = await _transcoder.ProbeAsync(source, TestContext.Current.CancellationToken);
        var output = Path.Combine(_work, "720p");
        await _transcoder.TranscodeAsync(source, probe, RenditionQuality.P720, output, TestContext.Current.CancellationToken);
        var frame = Path.Combine(_work, "frame.png");
        await RunAsync("ffmpeg", "-v", "error", "-i", Path.Combine(output, "index.m3u8"), "-ss", "2", "-frames:v", "1", frame);

        using var image = new MagickImage(frame);
        using var pixels = image.GetPixels();
        var layout = WatermarkLayout.For(1280, 720);

        Brightest(pixels, layout.Margin, layout.Margin, layout.LogoWidth, layout.LogoWidth / 3).Should().BeGreaterThan(40, "the logo is drawn top-left");
        Brightest(pixels, 560, 300, 160, 120).Should().BeLessThan(20, "the middle of a black clip stays black");
    }

    /// <summary>Previews: a WebP poster, a 480-pixel-wide WebP thumbnail, a WebP sprite and its WebVTT map.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Previews_are_webp_with_a_sprite_map()
    {
        var source = await SourceAsync();
        var probe = await _transcoder.ProbeAsync(source, TestContext.Current.CancellationToken);

        var previews = await _transcoder.ExtractPreviewsAsync(source, probe, Path.Combine(_work, "previews"), TestContext.Current.CancellationToken);

        foreach (var image in new[] { previews.Poster, previews.Thumbnail, previews.Sprite })
        {
            var header = (await File.ReadAllBytesAsync(image, TestContext.Current.CancellationToken))[..12];
            System.Text.Encoding.ASCII.GetString(header, 8, 4).Should().Be("WEBP");
        }

        using var thumbnail = new MagickImage(previews.Thumbnail);
        thumbnail.Width.Should().Be(480);
        var vtt = await File.ReadAllTextAsync(previews.SpriteVtt, TestContext.Current.CancellationToken);
        vtt.Should().StartWith("WEBVTT").And.Contain("sprite.webp#xywh=");
    }

    /// <summary>Deletes the work folder.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    /// <summary>Makes the 8-second 720p black test clip with a 440 Hz tone, or skips the test without ffmpeg.</summary>
    /// <returns>The clip's path.</returns>
    private async Task<string> SourceAsync()
    {
        RequireFfmpeg();
        var source = Path.Combine(_work, "source clip.mp4");
        await RunAsync(
            "ffmpeg", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=1280x720:d=8:r=25", "-f", "lavfi", "-i", "sine=frequency=440:duration=8",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source);
        return source;
    }

    /// <summary>Skips the test when ffmpeg or ffprobe is not installed.</summary>
    private static void RequireFfmpeg()
    {
        if (!Runs("ffmpeg") || !Runs("ffprobe"))
        {
            Assert.Skip("ffmpeg and ffprobe are not installed here.");
        }
    }

    /// <summary>Tells whether a tool answers <c>-version</c>.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns><see langword="true"/> when it runs.</returns>
    private static bool Runs(string tool)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool, "-version") { RedirectStandardOutput = true, RedirectStandardError = true });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Runs a tool and fails the test when it fails.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task that completes when the tool has exited.</returns>
    private static async Task RunAsync(string tool, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var errors = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(0, errors);
    }

    /// <summary>Finds the brightest grey level in a box (0–255).</summary>
    /// <param name="pixels">The pixels.</param>
    /// <param name="left">The box's left edge.</param>
    /// <param name="top">The box's top edge.</param>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <returns>The brightest level.</returns>
    private static int Brightest(IPixelCollection<byte> pixels, int left, int top, int width, int height)
    {
        var brightest = 0;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var color = pixels.GetPixel(x, y).ToColor()!;
                brightest = Math.Max(brightest, (color.R + color.G + color.B) / 3);
            }
        }

        return brightest;
    }

    /// <summary>An <c>#EXTINF:</c> duration.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"#EXTINF:([0-9.]+),", RegexOptions.CultureInvariant)]
    private static partial Regex ExtInf();
}
