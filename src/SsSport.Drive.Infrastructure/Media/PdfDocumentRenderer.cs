using ImageMagick;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SsSport.Drive.Application.Abstractions.Ingest;

namespace SsSport.Drive.Infrastructure.Media;

/// <summary>
/// <see cref="IDocumentRenderer"/> (I-4): PDFs are rendered with PDFium (PDFtoImage; Ghostscript is AGPL), stats screenshots
/// are read with ImageMagick. Pages are WebP images at most 1600 pixels wide, <c>1.webp</c>, <c>2.webp</c>, …, and
/// <c>thumb.webp</c> is a 480-pixel thumbnail of the first page. Metadata is stripped.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed partial class PdfDocumentRenderer(ILogger<PdfDocumentRenderer> logger) : IDocumentRenderer
{
    /// <summary>The most pages one PDF may have, so a huge file cannot fill the bucket.</summary>
    public const int MaxPages = 200;

    /// <summary>The width of a page image.</summary>
    private const int PageWidth = 1600;

    /// <summary>The width of the thumbnail.</summary>
    private const uint ThumbnailWidth = 480;

    /// <summary>The quality of the WebP images.</summary>
    private const uint WebpQuality = 82;

    /// <inheritdoc/>
    public Task<RenderedDocument> RenderAsync(string inputPath, string originalName, string outputFolder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var isPdf = string.Equals(Path.GetExtension(originalName), ".pdf", StringComparison.OrdinalIgnoreCase);
        return Task.Run(() => isPdf ? RenderPdf(inputPath, outputFolder, cancellationToken) : RenderImage(inputPath, outputFolder), cancellationToken);
    }

    /// <summary>Renders every page of a PDF.</summary>
    /// <param name="inputPath">The PDF.</param>
    /// <param name="outputFolder">The folder that receives the pages.</param>
    /// <param name="cancellationToken">Cancels the work between pages.</param>
    /// <returns>The pages and the thumbnail.</returns>
    /// <exception cref="MediaProcessingException">The PDF is unreadable, protected or too long.</exception>
#pragma warning disable CA1416 // PDFtoImage lists mobile and browser targets too; the workers run on Windows and Linux, which it supports.
    private RenderedDocument RenderPdf(string inputPath, string outputFolder, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var count = Conversion.GetPageCount(stream, leaveOpen: true);
            if (count > MaxPages)
            {
                throw new MediaProcessingException($"The PDF has {count} pages; at most {MaxPages} are supported.");
            }

            var pages = new List<string>();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = Conversion.ToImage(stream, index, leaveOpen: true, options: new RenderOptions(Width: PageWidth));
                using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var image = new MagickImage(data.ToArray());
                pages.Add(Save(image, Path.Combine(outputFolder, $"{index + 1}.webp")));
            }

            return new RenderedDocument(pages, Thumbnail(pages[0], outputFolder));
        }
        catch (Exception exception) when (exception is not (MediaProcessingException or OperationCanceledException))
        {
            LogUnreadable(logger, exception.GetType().Name);
            throw new MediaProcessingException("The PDF could not be read; it may be damaged or password-protected.");
        }
    }

#pragma warning restore CA1416

    /// <summary>Renders a screenshot as one page, never enlarged.</summary>
    /// <param name="inputPath">The image.</param>
    /// <param name="outputFolder">The folder that receives the page.</param>
    /// <returns>The page and the thumbnail.</returns>
    /// <exception cref="MediaProcessingException">The image is unreadable.</exception>
    private RenderedDocument RenderImage(string inputPath, string outputFolder)
    {
        try
        {
            using var image = new MagickImage(inputPath);
            image.AutoOrient();
            if (image.Width > PageWidth)
            {
                image.Resize(PageWidth, 0);
            }

            var page = Save(image, Path.Combine(outputFolder, "1.webp"));
            return new RenderedDocument([page], Thumbnail(page, outputFolder));
        }
        catch (MagickException exception)
        {
            LogUnreadable(logger, exception.GetType().Name);
            throw new MediaProcessingException("The image could not be read.");
        }
    }

    /// <summary>Strips an image's metadata and writes it as WebP.</summary>
    /// <param name="image">The image.</param>
    /// <param name="path">The file to write.</param>
    /// <returns>The path.</returns>
    private static string Save(MagickImage image, string path)
    {
        image.Strip();
        image.Quality = WebpQuality;
        image.Format = MagickFormat.WebP;
        image.Write(path);
        return path;
    }

    /// <summary>Makes the thumbnail of the first page.</summary>
    /// <param name="firstPage">The first page's file.</param>
    /// <param name="outputFolder">The folder that receives the thumbnail.</param>
    /// <returns>The thumbnail's path.</returns>
    private static string Thumbnail(string firstPage, string outputFolder)
    {
        using var image = new MagickImage(firstPage);
        if (image.Width > ThumbnailWidth)
        {
            image.Resize(ThumbnailWidth, 0);
        }

        return Save(image, Path.Combine(outputFolder, "thumb.webp"));
    }

    /// <summary>Logs an unreadable document: the kind of failure only, never the file name.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="FailureType">The exception type.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "A document could not be rendered ({FailureType})")]
    private static partial void LogUnreadable(ILogger logger, string FailureType);
}
