using System.Globalization;
using System.Text;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.Ingest;

/// <summary>Where an asset's original is stored in the media bucket, and with which MIME type (I-2).</summary>
public static class OriginalKeys
{
    /// <summary>The MIME type of each supported extension.</summary>
    private static readonly Dictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".m4v"] = "video/x-m4v",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".dvw"] = "text/plain",
    };

    /// <summary>Gets the key of a file's original: its md5 and lower-case extension, so identical content is stored once.</summary>
    /// <param name="file">The source file.</param>
    /// <returns>The key, e.g. <c>originals/9e107d9d372bb6826bd81d3542a419d6.mp4</c>.</returns>
    public static string For(AssetFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return $"originals/{file.Md5}{Path.GetExtension(file.OriginalName).ToLowerInvariant()}";
    }

    /// <summary>Gets the MIME type stored with an original.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The MIME type, <c>application/octet-stream</c> for anything unknown.</returns>
    public static string ContentType(string fileName) =>
        _contentTypes.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");
}

/// <summary>The bitrates of one quality level.</summary>
/// <param name="VideoKbps">The video bitrate in kbit/s.</param>
/// <param name="AudioKbps">The audio bitrate in kbit/s.</param>
public sealed record RenditionRung(int VideoKbps, int AudioKbps);

/// <summary>The rendition ladder (I-3): bitrates and frame sizes of 360p, 720p and 1080p.</summary>
public static class RenditionLadder
{
    /// <summary>Gets the bitrates of a quality (proposed values, pending the owner's review).</summary>
    /// <param name="quality">The quality.</param>
    /// <returns>The bitrates.</returns>
    public static RenditionRung For(RenditionQuality quality) => quality switch
    {
        RenditionQuality.P360 => new RenditionRung(800, 96),
        RenditionQuality.P720 => new RenditionRung(2800, 128),
        RenditionQuality.P1080 => new RenditionRung(5000, 128),
        _ => throw new ArgumentOutOfRangeException(nameof(quality)),
    };

    /// <summary>Gets the declared bandwidth of a quality in bits per second.</summary>
    /// <param name="quality">The quality.</param>
    /// <returns>Video plus audio bitrate, in bit/s.</returns>
    public static int Bandwidth(RenditionQuality quality)
    {
        var rung = For(quality);
        return (rung.VideoKbps + rung.AudioKbps) * 1000;
    }

    /// <summary>
    /// Gets the frame size of a quality: the short side equals the quality (so a portrait phone clip is as sharp as a
    /// landscape one) and the long side keeps the aspect ratio, rounded to an even number as H.264 needs.
    /// </summary>
    /// <param name="quality">The quality.</param>
    /// <param name="sourceWidth">The source width.</param>
    /// <param name="sourceHeight">The source height.</param>
    /// <returns>The width and height to encode.</returns>
    public static (int Width, int Height) FrameSize(RenditionQuality quality, int sourceWidth, int sourceHeight)
    {
        var shortSide = (int)quality;
        var shortSource = Math.Min(sourceWidth, sourceHeight);
        var longSource = Math.Max(sourceWidth, sourceHeight);
        var longSide = (int)Math.Round(longSource * (double)shortSide / shortSource / 2, MidpointRounding.AwayFromZero) * 2;
        return sourceWidth >= sourceHeight ? (longSide, shortSide) : (shortSide, longSide);
    }
}

/// <summary>Builds the HLS master playlist of a video (I-3).</summary>
public static class HlsMaster
{
    /// <summary>Builds the playlist: every rendition lowest first, with bandwidth, frame size and relative playlist.</summary>
    /// <param name="probe">The source video.</param>
    /// <param name="qualities">The renditions that exist, lowest first.</param>
    /// <returns>The playlist text.</returns>
    public static string Build(VideoProbe probe, IReadOnlyList<RenditionQuality> qualities)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(qualities);
        var text = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-INDEPENDENT-SEGMENTS\n");
        foreach (var quality in qualities)
        {
            var (width, height) = RenditionLadder.FrameSize(quality, probe.Width, probe.Height);
            text.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={RenditionLadder.Bandwidth(quality)},RESOLUTION={width}x{height},CODECS=\"avc1.640028,mp4a.40.2\"\n");
            text.Append(CultureInfo.InvariantCulture, $"{(int)quality}p/index.m3u8\n");
        }

        return text.ToString();
    }
}

/// <summary>Where and how big the burned-in watermark is for a frame size (decisions §6, S-11).</summary>
/// <param name="LogoWidth">The logo's width in pixels.</param>
/// <param name="Margin">The distance from each edge in pixels.</param>
/// <param name="SloganHeight">The slogan's text height in pixels.</param>
/// <param name="LogoOpacity">The logo's opacity (0–1).</param>
/// <param name="SloganOpacity">The slogan's opacity (0–1).</param>
/// <param name="ShadowOpacity">The soft shadow's opacity (0–1).</param>
public sealed record WatermarkLayout(int LogoWidth, int Margin, int SloganHeight, double LogoOpacity, double SloganOpacity, double ShadowOpacity)
{
    /// <summary>The slogan text, bottom-right.</summary>
    public const string Slogan = "SS-SPORT · Fetch Your Dreams";

    /// <summary>Scales the approved watermark to a frame: logo 10 % of the width, margins 2.5 % of the width, slogan 3.2 % of the height.</summary>
    /// <param name="frameWidth">The frame width in pixels.</param>
    /// <param name="frameHeight">The frame height in pixels.</param>
    /// <returns>The layout.</returns>
    public static WatermarkLayout For(int frameWidth, int frameHeight) => new(
        Scaled(frameWidth, 0.10),
        Scaled(frameWidth, 0.025),
        Scaled(frameHeight, 0.032),
        0.45,
        0.55,
        0.35);

    /// <summary>Takes a share of a size, rounded to the nearest pixel.</summary>
    /// <param name="size">The size.</param>
    /// <param name="share">The share.</param>
    /// <returns>The pixels.</returns>
    private static int Scaled(int size, double share) => (int)Math.Round(size * share, MidpointRounding.AwayFromZero);
}
