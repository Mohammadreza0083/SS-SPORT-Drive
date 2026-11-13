using ImageMagick;
using SsSport.Drive.Infrastructure.Media;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Ingest;

/// <summary>
/// I-8 (decisions §4a): the QR code is made in-house with QRCoder (MIT). It is a print-ready square PNG (1200 px,
/// proposed) with a white quiet zone and the red SS-SPORT mark in the centre, which the highest error correction (H)
/// leaves readable.
/// </summary>
public sealed class QrCodeRendererTests
{
    /// <summary>The rendered QR code of a player search link.</summary>
    private static readonly byte[] _png = new QrCoderQrCodeRenderer().RenderPng(new Uri("https://drive.ss-sport.co/?q=Ali%20Hoshmand"));

    /// <summary>It is a 1200-pixel square PNG.</summary>
    [Fact]
    public void It_is_a_print_ready_square_png()
    {
        using var image = new MagickImage(_png);

        image.Format.Should().Be(MagickFormat.Png);
        image.Width.Should().Be(QrCoderQrCodeRenderer.SizePixels);
        image.Height.Should().Be(QrCoderQrCodeRenderer.SizePixels);
        QrCoderQrCodeRenderer.SizePixels.Should().BeGreaterThanOrEqualTo(1200u);
    }

    /// <summary>The corners are white: the quiet zone scanners need.</summary>
    [Fact]
    public void It_has_a_white_quiet_zone()
    {
        using var image = new MagickImage(_png);
        using var pixels = image.GetPixels();
        var last = (int)image.Width - 1;

        foreach (var (x, y) in new[] { (0, 0), (last, 0), (0, last), (last, last) })
        {
            var color = pixels.GetPixel(x, y).ToColor()!;
            (color.R, color.G, color.B).Should().Be(((byte)255, (byte)255, (byte)255));
        }
    }

    /// <summary>The SS-SPORT mark (brand red #CC0000) sits in the centre.</summary>
    [Fact]
    public void It_carries_the_brand_mark_in_the_centre()
    {
        using var image = new MagickImage(_png);
        using var pixels = image.GetPixels();
        var size = (int)image.Width;
        var box = size / 10;
        var red = 0;

        for (var y = (size / 2) - box; y < (size / 2) + box; y++)
        {
            for (var x = (size / 2) - box; x < (size / 2) + box; x++)
            {
                var color = pixels.GetPixel(x, y).ToColor()!;
                if (color.R > 150 && color.G < 80 && color.B < 80)
                {
                    red++;
                }
            }
        }

        red.Should().BeGreaterThan(box * box / 10, "the red mark covers part of the centre");
    }

    /// <summary>Different links give different codes.</summary>
    [Fact]
    public void Different_links_give_different_codes() =>
        new QrCoderQrCodeRenderer().RenderPng(new Uri("https://drive.ss-sport.co/?q=Sara%20Ahmadi")).Should().NotEqual(_png);
}
