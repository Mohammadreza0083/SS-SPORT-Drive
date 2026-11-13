using System.Globalization;
using System.Text;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Infrastructure.Media;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Ingest;

/// <summary>
/// I-4: documents become WebP page images, <c>1.webp</c>, <c>2.webp</c>, …, at most 1600 pixels wide, plus a
/// 480-pixel-wide thumbnail of the first page. PDFs are rendered with PDFium (no AGPL Ghostscript); stats screenshots
/// are one page, never enlarged, with their metadata stripped. Broken files and PDFs over 200 pages are processing errors.
/// </summary>
public sealed class PdfDocumentRendererTests : IDisposable
{
    /// <summary>The work folder.</summary>
    private readonly string _work = Directory.CreateTempSubdirectory("ssd-pdf-test-").FullName;

    /// <summary>The renderer under test.</summary>
    private readonly PdfDocumentRenderer _renderer = new(NullLogger<PdfDocumentRenderer>.Instance);

    /// <summary>A two-page PDF becomes two 1600-pixel-wide WebP pages and a 480-pixel thumbnail.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_pdf_becomes_one_webp_per_page()
    {
        var pdf = await WriteAsync("stats.pdf", BuildPdf(2));

        var rendered = await _renderer.RenderAsync(pdf, "Stats 2025.PDF", Path.Combine(_work, "out"), TestContext.Current.CancellationToken);

        rendered.Pages.Select(Path.GetFileName).Should().Equal("1.webp", "2.webp");
        foreach (var page in rendered.Pages)
        {
            using var image = new MagickImage(page);
            image.Format.Should().Be(MagickFormat.WebP);
            image.Width.Should().Be(1600);
        }

        using var thumbnail = new MagickImage(rendered.Thumbnail);
        thumbnail.Width.Should().Be(480);
    }

    /// <summary>A large screenshot is one page scaled down to 1600 pixels, with no metadata left.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_large_screenshot_is_one_scaled_page()
    {
        var png = await WriteAsync("stats.png", Image(3000, 2000, withComment: true));

        var rendered = await _renderer.RenderAsync(png, "stats.PNG", Path.Combine(_work, "out"), TestContext.Current.CancellationToken);

        rendered.Pages.Should().ContainSingle();
        using var page = new MagickImage(rendered.Pages[0]);
        page.Width.Should().Be(1600);
        page.Comment.Should().BeNullOrEmpty();
        page.GetExifProfile().Should().BeNull();
    }

    /// <summary>A small screenshot is never enlarged.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_small_screenshot_keeps_its_size()
    {
        var jpg = await WriteAsync("small.jpg", Image(800, 600, withComment: false, MagickFormat.Jpeg));

        var rendered = await _renderer.RenderAsync(jpg, "small.jpg", Path.Combine(_work, "out"), TestContext.Current.CancellationToken);

        using var page = new MagickImage(rendered.Pages[0]);
        page.Width.Should().Be(800);
    }

    /// <summary>A corrupt PDF is a processing error with a reason.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_corrupt_pdf_is_a_processing_error()
    {
        var pdf = await WriteAsync("broken.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 this is not a document"));

        var act = () => _renderer.RenderAsync(pdf, "broken.pdf", Path.Combine(_work, "out"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<MediaProcessingException>()).Which.Message.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>More than 200 pages is refused (proposed limit), so one huge file cannot fill the bucket.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_pdf_over_the_page_limit_is_refused()
    {
        var pdf = await WriteAsync("huge.pdf", BuildPdf(PdfDocumentRenderer.MaxPages + 1));

        var act = () => _renderer.RenderAsync(pdf, "huge.pdf", Path.Combine(_work, "out"), TestContext.Current.CancellationToken);

        PdfDocumentRenderer.MaxPages.Should().Be(200);
        (await act.Should().ThrowAsync<MediaProcessingException>()).Which.Message.Should().Contain("200");
    }

    /// <summary>Deletes the work folder.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    /// <summary>Writes a file into the work folder.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="bytes">The content.</param>
    /// <returns>The path.</returns>
    private async Task<string> WriteAsync(string name, byte[] bytes)
    {
        var path = Path.Combine(_work, name);
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        return path;
    }

    /// <summary>Builds a plain image, optionally carrying a comment (metadata that must be stripped).</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="withComment">Whether to add a comment.</param>
    /// <param name="format">The format.</param>
    /// <returns>The encoded image.</returns>
    private static byte[] Image(uint width, uint height, bool withComment, MagickFormat format = MagickFormat.Png)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, width, height);
        if (withComment)
        {
            image.Comment = "secret metadata";
        }

        return image.ToByteArray(format);
    }

    /// <summary>Builds a valid PDF with US-Letter pages, each with one filled square, and a correct cross-reference table.</summary>
    /// <param name="pages">How many pages.</param>
    /// <returns>The PDF bytes.</returns>
    private static byte[] BuildPdf(int pages)
    {
        // Objects: 1 catalog, 2 page tree, then per page a page object and its content stream.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages).Select(i => $"{3 + (i * 2)} 0 R"))}] /Count {pages} >>",
        };
        const string content = "0 0 1 rg 100 100 200 200 re f";
        for (var i = 0; i < pages; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {4 + (i * 2)} 0 R /Resources << >> >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
