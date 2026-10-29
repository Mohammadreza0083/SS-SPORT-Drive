using System.Text;
using ImageMagick;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Images;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Images;

/// <summary>
/// Player photos (T-4b): JPEG, PNG, WebP or HEIC with a shorter side of at least 800 px become an upright WebP of at
/// most 2000 px with every bit of metadata (GPS included) removed. Anything else is refused with a stable code.
/// </summary>
public sealed class MagickPhotoProcessorTests
{
    /// <summary>The processor under test.</summary>
    private readonly MagickPhotoProcessor _processor = new();

    /// <summary>A large enough JPEG becomes a WebP of the same size, and the original's type is reported.</summary>
    [Fact]
    public void Converts_a_jpeg_to_webp()
    {
        var result = _processor.Process(Image(1000, 1200, MagickFormat.Jpeg));

        result.Width.Should().Be(1000);
        result.Height.Should().Be(1200);
        result.OriginalContentType.Should().Be("image/jpeg");
        using var output = new MagickImage(result.Webp);
        output.Format.Should().Be(MagickFormat.WebP);
        output.Width.Should().Be(1000u);
    }

    /// <summary>PNG and WebP are accepted too.</summary>
    /// <param name="format">The upload format.</param>
    /// <param name="contentType">The expected original type.</param>
    [Theory]
    [InlineData(MagickFormat.Png, "image/png")]
    [InlineData(MagickFormat.WebP, "image/webp")]
    public void Accepts_png_and_webp(MagickFormat format, string contentType)
    {
        _processor.Process(Image(900, 900, format)).OriginalContentType.Should().Be(contentType);
    }

    /// <summary>A big photo is scaled down so its longer side is 2000 px, keeping the aspect ratio.</summary>
    [Fact]
    public void Caps_the_longer_side_at_2000_px()
    {
        var result = _processor.Process(Image(4000, 3000, MagickFormat.Png));

        result.Width.Should().Be(2000);
        result.Height.Should().Be(1500);
    }

    /// <summary>The camera's orientation flag is applied, so a sideways phone photo comes out upright.</summary>
    [Fact]
    public void Applies_the_exif_orientation()
    {
        // A phone held upright stores a landscape sensor image with EXIF orientation 6 ("rotate 90° clockwise").
        using var image = new MagickImage(MagickColors.SteelBlue, 1200, 900);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        image.SetProfile(exif);
        image.Orientation = OrientationType.RightTop;
        var jpeg = image.ToByteArray(MagickFormat.Jpeg);
        new MagickImage(jpeg).Orientation.Should().Be(OrientationType.RightTop, "the test photo must carry the flag");

        var result = _processor.Process(new MemoryStream(jpeg));

        result.Width.Should().Be(900);
        result.Height.Should().Be(1200);
    }

    /// <summary>EXIF (with GPS), comments and other profiles never reach storage.</summary>
    [Fact]
    public void Strips_all_metadata()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 1000, 1000);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Artist, "Someone");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        image.SetProfile(exif);
        image.Comment = "secret comment";

        var result = _processor.Process(new MemoryStream(image.ToByteArray(MagickFormat.Jpeg)));

        using var output = new MagickImage(result.Webp);
        output.GetExifProfile().Should().BeNull();
        output.Comment.Should().BeNullOrEmpty();
    }

    /// <summary>A photo whose shorter side is under 800 px is too small for the player page.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    [Theory]
    [InlineData(799, 1200)]
    [InlineData(1600, 700)]
    public void Refuses_small_photos(int width, int height)
    {
        var process = () => _processor.Process(Image(width, height, MagickFormat.Jpeg));

        process.Should().Throw<DomainException>().Which.Code.Should().Be("player.photo_too_small");
    }

    /// <summary>Text, SVG (a script-capable format) and GIF are not photos.</summary>
    /// <param name="kind">What to send.</param>
    [Theory]
    [InlineData("text")]
    [InlineData("svg")]
    [InlineData("gif")]
    [InlineData("empty")]
    public void Refuses_other_content(string kind)
    {
        var content = kind switch
        {
            "text" => new MemoryStream(Encoding.UTF8.GetBytes("this is not an image")),
            "svg" => new MemoryStream(Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1000"><rect width="1000" height="1000"/></svg>""")),
            "gif" => Image(1000, 1000, MagickFormat.Gif),
            _ => new MemoryStream(),
        };

        var process = () => _processor.Process(content);

        process.Should().Throw<DomainException>().Which.Code.Should().Be("player.photo_unreadable");
    }

    /// <summary>Builds a plain image in a format.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="format">The format.</param>
    /// <returns>The encoded image.</returns>
    private static MemoryStream Image(int width, int height, MagickFormat format)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, (uint)width, (uint)height);
        return new MemoryStream(image.ToByteArray(format));
    }
}
