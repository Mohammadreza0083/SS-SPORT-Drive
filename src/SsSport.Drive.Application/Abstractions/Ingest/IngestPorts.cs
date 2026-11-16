using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.Abstractions.Ingest;

/// <summary>One step of the ingest pipeline; each is a background job (Epic 3).</summary>
public enum IngestStep
{
    /// <summary>Compares a linked Drive folder with the player's assets (I-6).</summary>
    SyncFolder,

    /// <summary>Stores an asset's original in the media bucket (I-2).</summary>
    CopyOriginal,

    /// <summary>Encodes a video or renders a document (I-3, I-4).</summary>
    ProcessAsset,

    /// <summary>Makes a player's QR code (I-8).</summary>
    RenderQrCode,

    /// <summary>Removes the background of a player's photo (I-7).</summary>
    CutOutPhoto,

    /// <summary>Makes a player's card image (I-5).</summary>
    RenderCard,

    /// <summary>Scans the Shared Drive's archive and matches it to the backup (M-1).</summary>
    ScanArchive,
}

/// <summary>
/// Which lane an asset's copy and processing run on (M-4). Archive players are processed on their own lane, which runs a
/// limited number of jobs and lets the team's new uploads go first.
/// </summary>
public enum IngestLane
{
    /// <summary>The team's players: processed as soon as a worker is free.</summary>
    Live,

    /// <summary>Players imported from the archive: throttled and behind the live lane.</summary>
    Archive,
}

/// <summary>Picks a player's ingest lane.</summary>
public static class IngestLanes
{
    /// <summary>Gets the lane of a player's files.</summary>
    /// <param name="player">The player.</param>
    /// <returns><see cref="IngestLane.Archive"/> for archive players, otherwise <see cref="IngestLane.Live"/>.</returns>
    public static IngestLane For(Domain.Players.Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Origin == Domain.Players.PlayerOrigin.Archive ? IngestLane.Archive : IngestLane.Live;
    }
}

/// <summary>Queues ingest steps as background jobs; Infrastructure implements it with Hangfire.</summary>
public interface IIngestScheduler
{
    /// <summary>Queues a step for a target.</summary>
    /// <param name="step">The step.</param>
    /// <param name="targetId">The folder, asset or player the step works on.</param>
    /// <param name="cancellationToken">Cancels the enqueueing.</param>
    /// <returns>A task that completes when the step is queued.</returns>
    Task EnqueueAsync(IngestStep step, Guid targetId, CancellationToken cancellationToken);

    /// <summary>Queues an asset's copy or processing on its lane (M-4).</summary>
    /// <param name="step">The step: <see cref="IngestStep.CopyOriginal"/> or <see cref="IngestStep.ProcessAsset"/>.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="lane">The lane of the asset's player.</param>
    /// <param name="cancellationToken">Cancels the enqueueing.</param>
    /// <returns>A task that completes when the step is queued.</returns>
    Task EnqueueAssetAsync(IngestStep step, Guid assetId, IngestLane lane, CancellationToken cancellationToken);
}

/// <summary>Downloads a file's bytes from Google Drive (I-2).</summary>
public interface IDriveFileDownloader
{
    /// <summary>Opens a file for reading.</summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The content, or <see langword="null"/> when Drive no longer has the file; the caller disposes it.</returns>
    /// <exception cref="Exceptions.ServiceUnavailableException"><c>drive.unavailable</c> when Drive cannot be reached.</exception>
    Task<Stream?> OpenAsync(string fileId, CancellationToken cancellationToken);
}

/// <summary>The read-only rclone backup of the Drive (decisions §4): searched by md5 and copied from, never written.</summary>
public interface IBackupArchive
{
    /// <summary>Finds a backed-up file by its md5.</summary>
    /// <param name="md5">The lower-case md5.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The key in the backup bucket, or <see langword="null"/> when there is none.</returns>
    Task<string?> FindByMd5Async(string md5, CancellationToken cancellationToken);

    /// <summary>Copies a backed-up file into the media bucket, inside storage.</summary>
    /// <param name="backupKey">The key in the backup bucket.</param>
    /// <param name="mediaKey">The key in the media bucket.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>A task that completes when the copy exists.</returns>
    Task CopyToMediaAsync(string backupKey, string mediaKey, CancellationToken cancellationToken);
}

/// <summary>Hands out temporary work folders for downloads and encodes.</summary>
public interface IWorkspace
{
    /// <summary>Creates an empty work folder.</summary>
    /// <returns>The folder; disposing it deletes it and everything in it.</returns>
    IWorkFolder Create();
}

/// <summary>A temporary folder that is deleted when disposed.</summary>
public interface IWorkFolder : IDisposable
{
    /// <summary>Gets the full path of the folder.</summary>
    string Path { get; }
}

/// <summary>What ffprobe found in a video.</summary>
/// <param name="Width">The display width in pixels, rotation applied.</param>
/// <param name="Height">The display height in pixels, rotation applied.</param>
/// <param name="DurationSeconds">The duration in seconds.</param>
public sealed record VideoProbe(int Width, int Height, double DurationSeconds);

/// <summary>The preview files made from a video.</summary>
/// <param name="Poster">The poster WebP.</param>
/// <param name="Thumbnail">The thumbnail WebP.</param>
/// <param name="Sprite">The scrub sprite WebP.</param>
/// <param name="SpriteVtt">The WebVTT map of the sprite.</param>
public sealed record VideoPreviewFiles(string Poster, string Thumbnail, string Sprite, string SpriteVtt);

/// <summary>Probes and encodes videos (ffmpeg).</summary>
public interface IVideoTranscoder
{
    /// <summary>Reads a video's size and duration.</summary>
    /// <param name="inputPath">The video file.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The facts.</returns>
    /// <exception cref="MediaProcessingException">The file is not a video.</exception>
    Task<VideoProbe> ProbeAsync(string inputPath, CancellationToken cancellationToken);

    /// <summary>Encodes one HLS rendition with the watermark burned in.</summary>
    /// <param name="inputPath">The video file.</param>
    /// <param name="probe">The probe of the video.</param>
    /// <param name="quality">The quality to encode.</param>
    /// <param name="outputFolder">The folder that receives <c>index.m3u8</c> and the segments.</param>
    /// <param name="cancellationToken">Cancels the encode.</param>
    /// <returns>A task that completes when the rendition is written.</returns>
    /// <exception cref="MediaProcessingException">The encode failed.</exception>
    Task TranscodeAsync(string inputPath, VideoProbe probe, RenditionQuality quality, string outputFolder, CancellationToken cancellationToken);

    /// <summary>Makes the poster, thumbnail and scrub sprite.</summary>
    /// <param name="inputPath">The video file.</param>
    /// <param name="probe">The probe of the video.</param>
    /// <param name="outputFolder">The folder that receives the files.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The files.</returns>
    /// <exception cref="MediaProcessingException">A frame could not be read.</exception>
    Task<VideoPreviewFiles> ExtractPreviewsAsync(string inputPath, VideoProbe probe, string outputFolder, CancellationToken cancellationToken);
}

/// <summary>The page images of a document.</summary>
/// <param name="Pages">The page files, in order.</param>
/// <param name="Thumbnail">The thumbnail file of the first page.</param>
public sealed record RenderedDocument(IReadOnlyList<string> Pages, string Thumbnail);

/// <summary>Renders PDFs and screenshots to page images.</summary>
public interface IDocumentRenderer
{
    /// <summary>Renders a document.</summary>
    /// <param name="inputPath">The document file.</param>
    /// <param name="originalName">The file's original name; its extension decides how it is read.</param>
    /// <param name="outputFolder">The folder that receives <c>1.webp</c>, <c>2.webp</c>, … and <c>thumb.webp</c>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The files.</returns>
    /// <exception cref="MediaProcessingException">The file cannot be rendered.</exception>
    Task<RenderedDocument> RenderAsync(string inputPath, string originalName, string outputFolder, CancellationToken cancellationToken);
}

/// <summary>A media file could not be processed; the message is safe to store and to show.</summary>
/// <param name="reason">Why it failed.</param>
public sealed class MediaProcessingException(string reason) : Exception(reason);

/// <summary>Draws a QR code.</summary>
public interface IQrCodeRenderer
{
    /// <summary>Draws the QR code of an address with the SS-SPORT mark in the centre.</summary>
    /// <param name="target">The address the code opens.</param>
    /// <returns>The PNG.</returns>
    byte[] RenderPng(Uri target);
}

/// <summary>Removes the background of a player photo.</summary>
public interface IBackgroundRemover
{
    /// <summary>Cuts the person out of a photo.</summary>
    /// <param name="photo">The photo.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The cut-out WebP, or <see langword="null"/> when no model is configured.</returns>
    Task<byte[]?> CutOutAsync(Stream photo, CancellationToken cancellationToken);
}

/// <summary>What the player card shows.</summary>
/// <param name="Name">The player's name.</param>
/// <param name="Nationality">The country name.</param>
/// <param name="Positions">The position codes, primary first.</param>
/// <param name="Category">The category name (Men or Women).</param>
/// <param name="Photo">The photo or cut-out bytes, if any.</param>
public sealed record PlayerCardFacts(string Name, string Nationality, IReadOnlyList<string> Positions, string Category, byte[]? Photo);

/// <summary>Draws the player card.</summary>
public interface IPlayerCardRenderer
{
    /// <summary>Draws the card.</summary>
    /// <param name="facts">What it shows.</param>
    /// <returns>The PNG.</returns>
    byte[] RenderPng(PlayerCardFacts facts);
}
