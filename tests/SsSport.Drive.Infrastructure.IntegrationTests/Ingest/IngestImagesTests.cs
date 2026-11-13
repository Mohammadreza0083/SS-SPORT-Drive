using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Infrastructure.Media;
using SsSport.Drive.Infrastructure.Web;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Ingest;

/// <summary>
/// The images made from a player outside ffmpeg:
/// <list type="bullet">
/// <item>I-8: the QR code's target is the home search prefilled with the player's name (decisions §4a);</item>
/// <item>I-7: background removal runs an ONNX person-segmentation model in-process (model file from
/// <c>Media__CutoutModelPath</c>); without a model it answers nothing; with one (opt-in live test) it returns a WebP
/// with transparency;</item>
/// <item>I-5: the player card is a 1080×1350 PNG (proposed; the design waits for the owner's artboard).</item>
/// </list>
/// </summary>
public sealed class IngestImagesTests
{
    /// <summary>The QR target is the home page with the name as the search, escaped.</summary>
    [Fact]
    public void The_qr_target_is_the_home_search()
    {
        var links = new WebLinks(Options.Create(new WebOptions { PublicBaseUrl = "https://drive.ss-sport.co/" }));

        links.PlayerSearch("Ali Hoshmand").AbsoluteUri.Should().Be("https://drive.ss-sport.co/?q=Ali%20Hoshmand");
    }

    /// <summary>Without a configured model, background removal answers nothing (the viewer keeps its fallback).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Without_a_model_nothing_is_cut_out()
    {
        var remover = new OnnxBackgroundRemover(Options.Create(new MediaOptions { CutoutModelPath = string.Empty }), NullLogger<OnnxBackgroundRemover>.Instance);

        var cutout = await remover.CutOutAsync(new MemoryStream(Photo()), TestContext.Current.CancellationToken);

        cutout.Should().BeNull();
    }

    /// <summary>Opt-in (<c>SSD_LIVE_CUTOUT_MODEL_PATH</c>): with the real model the result is a WebP of the same size with transparent pixels.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task With_the_model_the_cutout_has_transparency()
    {
        var model = Environment.GetEnvironmentVariable("SSD_LIVE_CUTOUT_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(model))
        {
            Assert.Skip("Set SSD_LIVE_CUTOUT_MODEL_PATH to the person-segmentation model to run this test.");
        }

        var remover = new OnnxBackgroundRemover(Options.Create(new MediaOptions { CutoutModelPath = model }), NullLogger<OnnxBackgroundRemover>.Instance);

        var cutout = await remover.CutOutAsync(new MemoryStream(Photo()), TestContext.Current.CancellationToken);

        using var image = new MagickImage(cutout!);
        image.Format.Should().Be(MagickFormat.WebP);
        (image.Width, image.Height).Should().Be((1000u, 1250u));
        image.HasAlpha.Should().BeTrue();
    }

    /// <summary>The card is a 1080×1350 PNG, with or without a photo.</summary>
    /// <param name="withPhoto">Whether a photo is given.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_card_is_a_portrait_png(bool withPhoto)
    {
        var facts = new PlayerCardFacts("Ali Hoshmand", "Iran", ["OH", "OPP"], "Men", withPhoto ? Photo() : null);

        using var card = new MagickImage(new MagickPlayerCardRenderer().RenderPng(facts));

        card.Format.Should().Be(MagickFormat.Png);
        (card.Width, card.Height).Should().Be((1080u, 1350u));
    }

    /// <summary>Builds a plain 1000×1250 portrait "photo".</summary>
    /// <returns>The WebP bytes.</returns>
    private static byte[] Photo()
    {
        using var image = new MagickImage(MagickColors.LightGray, 1000, 1250);
        return image.ToByteArray(MagickFormat.WebP);
    }
}
