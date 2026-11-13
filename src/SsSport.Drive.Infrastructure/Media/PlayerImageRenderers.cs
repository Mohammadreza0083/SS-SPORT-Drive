using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using QRCoder;
using SsSport.Drive.Application.Abstractions.Ingest;

namespace SsSport.Drive.Infrastructure.Media;

/// <summary>
/// <see cref="IQrCodeRenderer"/> with QRCoder (I-8): a square PNG with the highest error correction, a white quiet zone and
/// the SS-SPORT mark in the centre. Modules are drawn at a whole number of pixels, so the code is sharp when printed.
/// </summary>
public sealed class QrCoderQrCodeRenderer : IQrCodeRenderer
{
    /// <summary>The side of the PNG in pixels.</summary>
    public const uint SizePixels = 1200;

    /// <summary>The quiet zone in modules.</summary>
    private const int QuietModules = 4;

    /// <summary>The mark's width as a share of the PNG's width.</summary>
    private const double MarkShare = 0.20;

    /// <inheritdoc/>
    public byte[] RenderPng(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(target.AbsoluteUri, QRCodeGenerator.ECCLevel.H);
        var modules = data.ModuleMatrix;
        var size = (int)SizePixels;
        var module = size / (modules.Count + (2 * QuietModules));
        var origin = (size - (modules.Count * module)) / 2;

        using var image = new MagickImage(MagickColors.White, SizePixels, SizePixels);
        var drawing = new Drawables().FillColor(MagickColors.Black);
        for (var row = 0; row < modules.Count; row++)
        {
            for (var column = 0; column < modules.Count; column++)
            {
                if (modules[row][column])
                {
                    var x = origin + (column * module);
                    var y = origin + (row * module);
                    drawing.Rectangle(x, y, x + module - 1, y + module - 1);
                }
            }
        }

        drawing.Draw(image);
        PlaceMark(image);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    /// <summary>Lays the brand mark on a white pad in the middle of the code.</summary>
    /// <param name="image">The QR code.</param>
    private static void PlaceMark(MagickImage image)
    {
        using var mark = new MagickImage(MediaAssets.Read("mark_color.png"));
        mark.Resize((uint)(SizePixels * MarkShare), 0);
        const int padding = 16;
        using var pad = new MagickImage(MagickColors.White, mark.Width + (2 * padding), mark.Height + (2 * padding));
        pad.Composite(mark, padding, padding, CompositeOperator.Over);
        image.Composite(pad, Gravity.Center, CompositeOperator.Over);
    }
}

/// <summary>
/// <see cref="IBackgroundRemover"/> on ONNX Runtime (I-7) with a person-segmentation model such as <c>u2net_human_seg</c>
/// (Apache-2.0). The model file comes from <c>Media__CutoutModelPath</c>; without it nothing is cut out and the viewer
/// keeps its fallback.
/// </summary>
/// <param name="options">The media settings.</param>
/// <param name="logger">The logger.</param>
public sealed partial class OnnxBackgroundRemover(IOptions<MediaOptions> options, ILogger<OnnxBackgroundRemover> logger) : IBackgroundRemover, IDisposable
{
    /// <summary>The side of the square the model reads and writes.</summary>
    private const int ModelSide = 320;

    /// <summary>The per-channel mean the model was trained with.</summary>
    private static readonly float[] _mean = [0.485f, 0.456f, 0.406f];

    /// <summary>The per-channel standard deviation the model was trained with.</summary>
    private static readonly float[] _deviation = [0.229f, 0.224f, 0.225f];

    /// <summary>Loads the model on first use.</summary>
    private readonly Lazy<InferenceSession?> _session = new(() => Load(options.Value.CutoutModelPath, logger));

    /// <inheritdoc/>
    public Task<byte[]?> CutOutAsync(Stream photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (_session.Value is not { } session)
        {
            return Task.FromResult<byte[]?>(null);
        }

        using var copy = new MemoryStream();
        photo.CopyTo(copy);
        return Task.Run<byte[]?>(() => CutOut(session, copy.ToArray()), cancellationToken);
    }

    /// <summary>Disposes the model if it was loaded.</summary>
    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value?.Dispose();
        }
    }

    /// <summary>Loads the model, or logs why it cannot be used.</summary>
    /// <param name="path">The model file.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The session, or <see langword="null"/> when no model is configured.</returns>
    private static InferenceSession? Load(string path, ILogger logger)
    {
        if (path.Length == 0 || !File.Exists(path))
        {
            LogNoModel(logger);
            return null;
        }

        return new InferenceSession(path);
    }

    /// <summary>Predicts the person mask and applies it as the photo's alpha channel.</summary>
    /// <param name="session">The model.</param>
    /// <param name="photo">The photo bytes.</param>
    /// <returns>The cut-out WebP, the size of the photo.</returns>
    private static byte[] CutOut(InferenceSession session, byte[] photo)
    {
        using var image = new MagickImage(photo);
        image.AutoOrient();
        image.Strip();

        using var small = (MagickImage)image.Clone();
        small.Resize(new MagickGeometry(ModelSide, ModelSide) { IgnoreAspectRatio = true });
        var rgb = small.GetPixels().ToByteArray(PixelMapping.RGB) ?? throw new InvalidOperationException("The photo has no pixels.");
        var input = new DenseTensor<float>([1, 3, ModelSide, ModelSide]);
        for (var i = 0; i < ModelSide * ModelSide; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                input[0, channel, i / ModelSide, i % ModelSide] = ((rgb[(i * 3) + channel] / 255f) - _mean[channel]) / _deviation[channel];
            }
        }

        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var output = results[0].AsTensor<float>().ToArray();
        var min = output.Min();
        var range = Math.Max(output.Max() - min, 1e-6f);
        var gray = new byte[ModelSide * ModelSide * 3];
        for (var i = 0; i < ModelSide * ModelSide; i++)
        {
            var value = (byte)Math.Clamp((output[i] - min) / range * 255f, 0f, 255f);
            gray[i * 3] = gray[(i * 3) + 1] = gray[(i * 3) + 2] = value;
        }

        using var mask = new MagickImage();
        mask.ReadPixels(gray, new PixelReadSettings(ModelSide, ModelSide, StorageType.Char, PixelMapping.RGB));
        mask.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
        mask.Alpha(AlphaOption.Off);
        image.Alpha(AlphaOption.Set);
        image.Composite(mask, CompositeOperator.CopyAlpha);
        image.Quality = 90;
        image.Format = MagickFormat.WebP;
        return image.ToByteArray();
    }

    /// <summary>Logs that background removal is off.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "No cut-out model is configured (Media__CutoutModelPath); player photos keep their background")]
    private static partial void LogNoModel(ILogger logger);
}

/// <summary>
/// <see cref="IPlayerCardRenderer"/> with ImageMagick (I-5): a 1080 × 1350 PNG on the site's dark background with the
/// photo, the name in Barlow Condensed, the details and the reversed logo. The layout lives here, in one place, so the
/// approved artboard is a small change.
/// </summary>
public sealed class MagickPlayerCardRenderer : IPlayerCardRenderer
{
    /// <summary>The card's width in pixels.</summary>
    private const uint Width = 1080;

    /// <summary>The card's height in pixels.</summary>
    private const uint Height = 1350;

    /// <summary>The distance of the content from the card's edges.</summary>
    private const int Margin = 64;

    /// <inheritdoc/>
    public byte[] RenderPng(PlayerCardFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        using var card = new MagickImage(new MagickColor("#07080B"), Width, Height);

        if (facts.Photo is { Length: > 0 } photoBytes)
        {
            using var photo = new MagickImage(photoBytes);
            photo.Resize(new MagickGeometry(Width, (uint)(Height * 0.8)));
            card.Composite(photo, Gravity.Southeast, CompositeOperator.Over);
        }

        using var logo = new MagickImage(MediaAssets.Read("logo_horizontal_reversed.png"));
        logo.Resize(300, 0);
        card.Composite(logo, Margin, Margin, CompositeOperator.Over);

        using var name = Text(facts.Name.ToUpperInvariant(), 150, MagickColors.White, Width - (2 * Margin));
        card.Composite(name, Margin, (int)Height - Margin - 140 - (int)name.Height, CompositeOperator.Over);

        var details = string.Join("  ·  ", new[] { facts.Nationality.ToUpperInvariant(), string.Join(" / ", facts.Positions), facts.Category.ToUpperInvariant() }.Where(part => part.Length > 0));
        using var line = Text(details, 56, new MagickColor("#CC0000"), Width - (2 * Margin));
        card.Composite(line, Margin, (int)Height - Margin - (int)line.Height, CompositeOperator.Over);

        card.Format = MagickFormat.Png;
        return card.ToByteArray();
    }

    /// <summary>Draws a line of text on a transparent background.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The font size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="width">The width the text wraps at.</param>
    /// <returns>The text image.</returns>
    private static MagickImage Text(string text, int size, IMagickColor<byte> color, uint width)
    {
        var settings = new MagickReadSettings
        {
            Font = MediaAssets.FontPath,
            FontPointsize = size,
            FillColor = color,
            BackgroundColor = MagickColors.Transparent,
            Width = width,
            TextGravity = Gravity.West,
        };
        return new MagickImage($"caption:{text}", settings);
    }
}
