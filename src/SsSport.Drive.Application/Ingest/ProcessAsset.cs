using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Ingest;

/// <summary>
/// Processes a stored original (I-3, I-4, I-9): a video becomes HLS renditions, a poster, a thumbnail and a scrub sprite; a
/// document becomes page images and a thumbnail; a scout file needs nothing. A media error fails the asset with its reason.
/// </summary>
/// <param name="AssetId">The asset.</param>
public sealed record ProcessAssetCommand(Guid AssetId) : ICommand;

/// <summary>Handles <see cref="ProcessAssetCommand"/>.</summary>
/// <param name="assets">The assets.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="workspace">Hands out the work folder.</param>
/// <param name="transcoder">Probes and encodes videos.</param>
/// <param name="renderer">Renders documents.</param>
public sealed class ProcessAssetCommandHandler(
    IRepository<Asset, Guid> assets,
    IObjectStorage storage,
    IWorkspace workspace,
    IVideoTranscoder transcoder,
    IDocumentRenderer renderer) : ICommandHandler<ProcessAssetCommand>
{
    /// <summary>The MIME type of an HLS playlist.</summary>
    private const string PlaylistContentType = "application/vnd.apple.mpegurl";

    /// <summary>Processes the asset.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the asset is ready or failed.</returns>
    public async Task Handle(ProcessAssetCommand request, CancellationToken cancellationToken)
    {
        // A job may run twice: only an asset whose original is stored is worked on.
        if (await assets.GetByIdAsync(request.AssetId, cancellationToken) is not { State: ProcessingState.Processing, OriginalKey: { } original } asset)
        {
            return;
        }

        if (asset.Kind == AssetKind.Scout)
        {
            asset.CompleteDocument(null);
            return;
        }

        using var folder = workspace.Create();
        var input = Path.Combine(folder.Path, "original" + Path.GetExtension(asset.File.OriginalName).ToLowerInvariant());
        await storage.DownloadToFileAsync(original.Value, input, cancellationToken);
        try
        {
            if (asset.Kind == AssetKind.Video)
            {
                await ProcessVideoAsync(asset, input, folder.Path, cancellationToken);
            }
            else
            {
                await ProcessDocumentAsync(asset, input, folder.Path, cancellationToken);
            }
        }
        catch (MediaProcessingException exception)
        {
            asset.Fail(exception.Message);
        }
    }

    /// <summary>Encodes the renditions, writes the master playlist, makes the previews and marks everything ready.</summary>
    /// <param name="asset">The processing video.</param>
    /// <param name="input">The original on disk.</param>
    /// <param name="workPath">The work folder.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the video is ready.</returns>
    private async Task ProcessVideoAsync(Asset asset, string input, string workPath, CancellationToken cancellationToken)
    {
        var probe = await transcoder.ProbeAsync(input, cancellationToken);
        asset.RecordVideoInfo(Math.Min(probe.Width, probe.Height), probe.DurationSeconds);
        var hls = MediaFolders.VideoHls(asset.PlayerId, asset.Id, asset.Version);
        var qualities = asset.Renditions.Select(rendition => rendition.Quality).ToList();

        foreach (var quality in qualities)
        {
            var output = Path.Combine(workPath, $"{(int)quality}p");
            await transcoder.TranscodeAsync(input, probe, quality, output, cancellationToken);
            foreach (var file in Directory.EnumerateFiles(output))
            {
                var name = Path.GetFileName(file);
                var contentType = name.EndsWith(".m3u8", StringComparison.Ordinal) ? PlaylistContentType : "video/mp2t";
                await storage.UploadFileAsync(file, $"{hls}/{(int)quality}p/{name}", contentType, cancellationToken);
            }
        }

        // The master playlist goes last, so a stream never points at renditions that are not there yet.
        using var master = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(HlsMaster.Build(probe, qualities)));
        await storage.PutAsync($"{hls}/{MediaFolders.MasterPlaylist}", master, PlaylistContentType, cancellationToken);

        var root = MediaFolders.VideoRoot(asset.PlayerId, asset.Id, asset.Version);
        var previews = await transcoder.ExtractPreviewsAsync(input, probe, Path.Combine(workPath, "previews"), cancellationToken);
        await storage.UploadFileAsync(previews.Poster, $"{root}/poster.webp", "image/webp", cancellationToken);
        await storage.UploadFileAsync(previews.Thumbnail, $"{root}/thumb.webp", "image/webp", cancellationToken);
        await storage.UploadFileAsync(previews.Sprite, $"{root}/sprite.webp", "image/webp", cancellationToken);
        await storage.UploadFileAsync(previews.SpriteVtt, $"{root}/sprite.vtt", "text/vtt", cancellationToken);
        asset.AttachVideoPreviews(StorageKey.Create($"{root}/poster.webp"), StorageKey.Create($"{root}/thumb.webp"), StorageKey.Create($"{root}/sprite.webp"));

        // The last rendition makes the asset ready, so a ready video always has its master and its previews.
        foreach (var quality in qualities)
        {
            asset.MarkRenditionReady(quality, StorageKey.Create($"{hls}/{(int)quality}p/index.m3u8"));
        }
    }

    /// <summary>Renders the page images and the thumbnail and completes the document.</summary>
    /// <param name="asset">The processing document.</param>
    /// <param name="input">The original on disk.</param>
    /// <param name="workPath">The work folder.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the document is ready.</returns>
    private async Task ProcessDocumentAsync(Asset asset, string input, string workPath, CancellationToken cancellationToken)
    {
        var rendered = await renderer.RenderAsync(input, asset.File.OriginalName, Path.Combine(workPath, "pages"), cancellationToken);
        var pages = MediaFolders.DocumentPages(asset.PlayerId, asset.Id, asset.Version);
        for (var index = 0; index < rendered.Pages.Count; index++)
        {
            await storage.UploadFileAsync(rendered.Pages[index], $"{pages}/{index + 1}.webp", "image/webp", cancellationToken);
        }

        var thumbnail = $"{MediaFolders.DocumentRoot(asset.PlayerId, asset.Id, asset.Version)}/thumb.webp";
        await storage.UploadFileAsync(rendered.Thumbnail, thumbnail, "image/webp", cancellationToken);
        asset.AttachDocumentThumbnail(StorageKey.Create(thumbnail));
        asset.CompleteDocument(rendered.Pages.Count);
    }
}
