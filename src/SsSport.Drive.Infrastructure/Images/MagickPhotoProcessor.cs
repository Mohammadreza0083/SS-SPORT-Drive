using ImageMagick;
using SsSport.Drive.Application.Abstractions.Images;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.Images;

/// <summary>
/// Player photos with Magick.NET (T-4b; owner's choice). Only JPEG, PNG, WebP and HEIC/HEIF are read, and the format
/// is pinned before decoding, so no other ImageMagick coder (SVG, MVG, PostScript…) ever runs on an upload.
/// </summary>
public sealed class MagickPhotoProcessor : IPhotoProcessor
{
    /// <summary>The shortest accepted side, in pixels.</summary>
    public const int MinSide = 800;

    /// <summary>The longest side of the stored WebP, in pixels.</summary>
    public const int MaxSide = 2000;

    /// <summary>The WebP quality.</summary>
    private const int WebpQuality = 85;

    /// <summary>The largest image Magick may decode, against decompression bombs: 12 000 px per side.</summary>
    private const uint MaxDecodeSide = 12_000;

    /// <summary>The accepted upload formats and their MIME types.</summary>
    private static readonly Dictionary<MagickFormat, string> _accepted = new()
    {
        [MagickFormat.Jpeg] = "image/jpeg",
        [MagickFormat.Jpg] = "image/jpeg",
        [MagickFormat.Png] = "image/png",
        [MagickFormat.WebP] = "image/webp",
        [MagickFormat.Heic] = "image/heic",
        [MagickFormat.Heif] = "image/heif",
    };

    /// <summary>Caps what ImageMagick may allocate, once for the process.</summary>
    static MagickPhotoProcessor()
    {
        ResourceLimits.Width = MaxDecodeSide;
        ResourceLimits.Height = MaxDecodeSide;
    }

    /// <inheritdoc/>
    public ProcessedPhoto Process(Stream upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        using var buffer = new MemoryStream();
        upload.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0 || !TryDetect(bytes, out var format, out var contentType))
        {
            throw Unreadable();
        }

        MagickImage image;
        try
        {
            image = new MagickImage(bytes, new MagickReadSettings { Format = format });
        }
        catch (MagickException)
        {
            throw Unreadable();
        }

        using (image)
        {
            image.AutoOrient();
            if (Math.Min(image.Width, image.Height) < MinSide)
            {
                throw new DomainException("player.photo_too_small", $"The photo must be at least {MinSide} px on its shorter side.");
            }

            if (Math.Max(image.Width, image.Height) > MaxSide)
            {
                image.Resize(new MagickGeometry(MaxSide, MaxSide));
            }

            image.Strip();
            image.Quality = WebpQuality;
            var webp = image.ToByteArray(MagickFormat.WebP);
            return new ProcessedPhoto(webp, (int)image.Width, (int)image.Height, contentType);
        }
    }

    /// <summary>Identifies the upload's real format from its header, without decoding the pixels.</summary>
    /// <param name="bytes">The upload.</param>
    /// <param name="format">The detected format.</param>
    /// <param name="contentType">Its MIME type.</param>
    /// <returns><see langword="true"/> for an accepted format.</returns>
    private static bool TryDetect(byte[] bytes, out MagickFormat format, out string contentType)
    {
        format = MagickFormat.Unknown;
        contentType = string.Empty;
        try
        {
            format = new MagickImageInfo(bytes).Format;
        }
        catch (MagickException)
        {
            return false;
        }

        return _accepted.TryGetValue(format, out contentType!);
    }

    /// <summary>Builds the error for anything that is not an accepted photo.</summary>
    /// <returns>The exception.</returns>
    private static DomainException Unreadable() =>
        new("player.photo_unreadable", "Send a JPEG, PNG, WebP or HEIC photo.");
}
