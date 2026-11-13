using System.Globalization;
using System.Text.Json;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Infrastructure.Media;

/// <summary>Builds the ffmpeg and ffprobe argument lists (I-3). Each argument is passed whole, so a file name can never inject anything.</summary>
public static class FfmpegCommands
{
    /// <summary>The length of one HLS segment in seconds (decisions §6).</summary>
    public const int SegmentSeconds = 6;

    /// <summary>Builds the arguments that encode one HLS rendition with the watermark burned in.</summary>
    /// <param name="inputPath">The source video.</param>
    /// <param name="probe">The probe of the source.</param>
    /// <param name="quality">The quality to encode.</param>
    /// <param name="watermarkPath">The full-frame transparent watermark PNG, already the size of the rendition.</param>
    /// <param name="outputFolder">The folder that receives <c>index.m3u8</c> and the segments.</param>
    /// <returns>The ffmpeg arguments.</returns>
    public static IReadOnlyList<string> Transcode(string inputPath, VideoProbe probe, RenditionQuality quality, string watermarkPath, string outputFolder)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var (width, height) = RenditionLadder.FrameSize(quality, probe.Width, probe.Height);
        var rung = RenditionLadder.For(quality);
        var maxRate = rung.VideoKbps * 107 / 100;
        var bufferSize = rung.VideoKbps * 3 / 2;
        return
        [
            "-y", "-v", "error", "-nostdin",
            "-i", inputPath,
            "-i", watermarkPath,
            "-filter_complex", $"[0:v:0]scale={width}:{height}:flags=bicubic,setsar=1[scaled];[scaled][1:v]overlay=0:0:format=auto[out]",
            "-map", "[out]", "-map", "0:a?",
            "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high",
            "-b:v", $"{rung.VideoKbps}k", "-maxrate", $"{maxRate}k", "-bufsize", $"{bufferSize}k",
            "-pix_fmt", "yuv420p",
            "-force_key_frames", $"expr:gte(t,n_forced*{SegmentSeconds})",
            "-c:a", "aac", "-b:a", $"{rung.AudioKbps}k", "-ac", "2",
            "-map_metadata", "-1",
            "-f", "hls",
            "-hls_time", SegmentSeconds.ToString(CultureInfo.InvariantCulture),
            "-hls_playlist_type", "vod",
            "-hls_segment_filename", Path.Combine(outputFolder, "seg_%05d.ts"),
            Path.Combine(outputFolder, "index.m3u8"),
        ];
    }

    /// <summary>Builds the ffprobe arguments that print a video's streams and format as JSON.</summary>
    /// <param name="inputPath">The video.</param>
    /// <returns>The ffprobe arguments.</returns>
    public static IReadOnlyList<string> Probe(string inputPath) =>
        ["-v", "error", "-show_streams", "-show_format", "-of", "json", inputPath];
}

/// <summary>Draws the burned-in watermark (decisions §6, S-11): the logo top-left and the slogan bottom-right, each with a soft shadow.</summary>
public static class WatermarkImage
{
    /// <summary>The brightness of the shadow's blur, as a share of the frame height.</summary>
    private const double ShadowBlurShare = 0.002;

    /// <summary>Renders the watermark as one transparent, full-frame PNG.</summary>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <returns>The PNG.</returns>
    public static byte[] RenderPng(int width, int height)
    {
        var layout = WatermarkLayout.For(width, height);
        using var canvas = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);

        using var logo = new MagickImage(MediaAssets.Read("logo_horizontal_white.png"));
        logo.Resize((uint)layout.LogoWidth, 0);
        Stamp(canvas, logo, layout.Margin, layout.Margin, layout.LogoOpacity, layout.ShadowOpacity, height);

        using var slogan = RenderSlogan(layout.SloganHeight);
        Stamp(canvas, slogan, width - layout.Margin - (int)slogan.Width, height - layout.Margin - (int)slogan.Height, layout.SloganOpacity, layout.ShadowOpacity, height);

        canvas.Format = MagickFormat.Png;
        return canvas.ToByteArray();
    }

    /// <summary>Draws the slogan in white, trimmed to its ink and scaled so the ink is exactly <paramref name="textHeight"/> tall.</summary>
    /// <param name="textHeight">The height of the text in pixels.</param>
    /// <returns>The slogan image.</returns>
    private static MagickImage RenderSlogan(int textHeight)
    {
        var settings = new MagickReadSettings
        {
            Font = MediaAssets.FontPath,
            FontPointsize = 200,
            FillColor = MagickColors.White,
            BackgroundColor = MagickColors.Transparent,
        };
        var text = new MagickImage($"label:{WatermarkLayout.Slogan}", settings);
        text.Trim();
        text.ResetPage();
        var scaledWidth = (uint)Math.Max(1, (int)Math.Round((double)text.Width * textHeight / text.Height, MidpointRounding.AwayFromZero));
        text.Resize(new MagickGeometry(scaledWidth, (uint)textHeight) { IgnoreAspectRatio = true });
        return text;
    }

    /// <summary>Lays an image on the canvas with a soft shadow under it.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="image">The white image.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="opacity">The image's opacity.</param>
    /// <param name="shadowOpacity">The shadow's opacity.</param>
    /// <param name="frameHeight">The frame height; the shadow scales with it.</param>
    private static void Stamp(MagickImage canvas, MagickImage image, int x, int y, double opacity, double shadowOpacity, int frameHeight)
    {
        using var shadow = (MagickImage)image.Clone();
        shadow.Colorize(MagickColors.Black, new Percentage(100));
        shadow.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, shadowOpacity);
        shadow.Blur(0, Math.Max(1, frameHeight * ShadowBlurShare));
        var offset = Math.Max(1, frameHeight / 360);
        canvas.Composite(shadow, x + offset, y + offset, CompositeOperator.Over);

        image.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, opacity);
        canvas.Composite(image, x, y, CompositeOperator.Over);
    }
}

/// <summary>
/// <see cref="IVideoTranscoder"/> over the ffmpeg and ffprobe command-line tools (I-3, I-4). Only the last line of a tool's
/// error output reaches an asset's failure reason; paths and the full command are never logged.
/// </summary>
/// <param name="options">The media settings.</param>
/// <param name="logger">The logger.</param>
public sealed partial class FfmpegVideoTranscoder(IOptions<MediaOptions> options, ILogger<FfmpegVideoTranscoder> logger) : IVideoTranscoder
{
    /// <summary>How long ffprobe may run.</summary>
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The width of a poster at most.</summary>
    private const uint PosterMaxWidth = 1280;

    /// <summary>The width of a thumbnail.</summary>
    private const uint ThumbnailWidth = 480;

    /// <summary>The width of one tile of the scrub sprite.</summary>
    private const int TileWidth = 160;

    /// <summary>The number of tile columns of the scrub sprite.</summary>
    private const int TileColumns = 10;

    /// <inheritdoc/>
    public async Task<VideoProbe> ProbeAsync(string inputPath, CancellationToken cancellationToken)
    {
        var (exitCode, output, error) = await ToolRunner.RunAsync(options.Value.FfprobePath, FfmpegCommands.Probe(inputPath), _probeTimeout, cancellationToken);
        if (exitCode != 0)
        {
            LogToolFailed(logger, "ffprobe", exitCode);
            throw new MediaProcessingException($"The file is not a readable video: {error}");
        }

        try
        {
            using var json = JsonDocument.Parse(output);
            var stream = json.RootElement.GetProperty("streams").EnumerateArray()
                .FirstOrDefault(s => s.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
            if (stream.ValueKind != JsonValueKind.Object)
            {
                throw new MediaProcessingException("The file has no video stream.");
            }

            var width = stream.GetProperty("width").GetInt32();
            var height = stream.GetProperty("height").GetInt32();
            if (IsRotatedSideways(stream))
            {
                (width, height) = (height, width);
            }

            return new VideoProbe(width, height, Duration(json.RootElement, stream));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new MediaProcessingException("The video's details could not be read.");
        }
    }

    /// <inheritdoc/>
    public async Task TranscodeAsync(string inputPath, VideoProbe probe, RenditionQuality quality, string outputFolder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var (width, height) = RenditionLadder.FrameSize(quality, probe.Width, probe.Height);
        var watermark = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputFolder))!, $"watermark-{width}x{height}.png");
        if (!File.Exists(watermark))
        {
            await File.WriteAllBytesAsync(watermark, WatermarkImage.RenderPng(width, height), cancellationToken);
        }

        var timeout = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(probe.DurationSeconds * 3);
        await RunFfmpegAsync(FfmpegCommands.Transcode(inputPath, probe, quality, watermark, outputFolder), timeout, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<VideoPreviewFiles> ExtractPreviewsAsync(string inputPath, VideoProbe probe, string outputFolder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var frame = Path.Combine(outputFolder, "frame.png");
        var at = (probe.DurationSeconds * 0.1).ToString("0.###", CultureInfo.InvariantCulture);
        await RunFfmpegAsync(["-y", "-v", "error", "-nostdin", "-ss", at, "-i", inputPath, "-frames:v", "1", frame], _probeTimeout, cancellationToken);

        var files = new VideoPreviewFiles(
            Path.Combine(outputFolder, "poster.webp"),
            Path.Combine(outputFolder, "thumb.webp"),
            Path.Combine(outputFolder, "sprite.webp"),
            Path.Combine(outputFolder, "sprite.vtt"));
        await SaveWebpAsync(frame, files.Poster, PosterMaxWidth, cancellationToken);
        await SaveWebpAsync(frame, files.Thumbnail, ThumbnailWidth, cancellationToken);
        await MakeSpriteAsync(inputPath, probe, files, outputFolder, cancellationToken);
        return files;
    }

    /// <summary>Makes the scrub sprite and its WebVTT map: one tile every few seconds, ten tiles to a row.</summary>
    /// <param name="inputPath">The source video.</param>
    /// <param name="probe">The probe of the source.</param>
    /// <param name="files">The files to write.</param>
    /// <param name="outputFolder">The work folder.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the sprite and its map are written.</returns>
    private async Task MakeSpriteAsync(string inputPath, VideoProbe probe, VideoPreviewFiles files, string outputFolder, CancellationToken cancellationToken)
    {
        var interval = Math.Max(2, probe.DurationSeconds / 100);
        var count = Math.Max(1, (int)Math.Ceiling(probe.DurationSeconds / interval));
        var rows = (count + TileColumns - 1) / TileColumns;
        var tileHeight = Math.Max(2, (int)Math.Round((double)TileWidth * probe.Height / probe.Width / 2, MidpointRounding.AwayFromZero) * 2);
        var sheet = Path.Combine(outputFolder, "sprite.png");
        var filter = string.Create(CultureInfo.InvariantCulture, $"fps=1/{interval},scale={TileWidth}:{tileHeight},tile={TileColumns}x{rows}");
        await RunFfmpegAsync(["-y", "-v", "error", "-nostdin", "-i", inputPath, "-vf", filter, "-frames:v", "1", "-an", sheet], TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(probe.DurationSeconds), cancellationToken);
        await SaveWebpAsync(sheet, files.Sprite, 0, cancellationToken);

        var vtt = new System.Text.StringBuilder("WEBVTT\n\n");
        for (var index = 0; index < count; index++)
        {
            var start = index * interval;
            var end = Math.Min((index + 1) * interval, Math.Max(probe.DurationSeconds, start + 0.001));
            vtt.Append(CultureInfo.InvariantCulture, $"{Timestamp(start)} --> {Timestamp(end)}\n");
            vtt.Append(CultureInfo.InvariantCulture, $"sprite.webp#xywh={index % TileColumns * TileWidth},{index / TileColumns * tileHeight},{TileWidth},{tileHeight}\n\n");
        }

        await File.WriteAllTextAsync(files.SpriteVtt, vtt.ToString(), cancellationToken);
    }

    /// <summary>Converts an image to WebP, scaled down to a width when it is wider.</summary>
    /// <param name="source">The source image.</param>
    /// <param name="target">The WebP to write.</param>
    /// <param name="maxWidth">The width to scale down to, or 0 to keep the size.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private static async Task SaveWebpAsync(string source, string target, uint maxWidth, CancellationToken cancellationToken)
    {
        using var image = new MagickImage(source);
        if (maxWidth > 0 && image.Width > maxWidth)
        {
            image.Resize(maxWidth, 0);
        }

        image.Quality = 80;
        image.Format = MagickFormat.WebP;
        await image.WriteAsync(target, cancellationToken);
    }

    /// <summary>Runs ffmpeg and turns a failure into a processing error carrying the tool's last error line.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="timeout">How long it may run.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when ffmpeg succeeded.</returns>
    /// <exception cref="MediaProcessingException">ffmpeg failed or ran too long.</exception>
    private async Task RunFfmpegAsync(IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var (exitCode, _, error) = await ToolRunner.RunAsync(options.Value.FfmpegPath, arguments, timeout, cancellationToken);
            if (exitCode != 0)
            {
                LogToolFailed(logger, "ffmpeg", exitCode);
                throw new MediaProcessingException($"ffmpeg failed: {error}");
            }
        }
        catch (TimeoutException exception)
        {
            throw new MediaProcessingException(exception.Message);
        }
    }

    /// <summary>Tells whether the video is stored sideways and shown rotated by 90 degrees.</summary>
    /// <param name="stream">The video stream.</param>
    /// <returns><see langword="true"/> when width and height swap on screen.</returns>
    private static bool IsRotatedSideways(JsonElement stream)
    {
        double? rotation = null;
        if (stream.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var item in sideData.EnumerateArray().Where(item => item.TryGetProperty("rotation", out _)))
            {
                rotation = item.GetProperty("rotation").GetDouble();
            }
        }

        if (rotation is null && stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rotate)
            && double.TryParse(rotate.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tagged))
        {
            rotation = tagged;
        }

        return rotation is { } degrees && Math.Abs(Math.Round(degrees / 90)) % 2 == 1;
    }

    /// <summary>Reads the duration from the format, else from the stream.</summary>
    /// <param name="root">The ffprobe JSON.</param>
    /// <param name="stream">The video stream.</param>
    /// <returns>The duration in seconds.</returns>
    private static double Duration(JsonElement root, JsonElement stream)
    {
        foreach (var holder in new[] { root.TryGetProperty("format", out var format) ? format : default, stream })
        {
            if (holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty("duration", out var duration)
                && double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                return seconds;
            }
        }

        throw new MediaProcessingException("The video has no duration.");
    }

    /// <summary>Formats a WebVTT timestamp.</summary>
    /// <param name="seconds">The time in seconds.</param>
    /// <returns>The timestamp, <c>hh:mm:ss.mmm</c>.</returns>
    private static string Timestamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    /// <summary>Logs a tool that exited with an error; the message of the failure goes to the asset, not the log.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Tool">The tool.</param>
    /// <param name="ExitCode">The exit code.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Tool} exited with code {ExitCode}")]
    private static partial void LogToolFailed(ILogger logger, string Tool, int ExitCode);
}
