using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.Telegram.Progress;

/// <summary>One file of a player as the build progress needs it (T-6b): read as an <c>AsNoTracking</c> projection.</summary>
/// <param name="Kind">Video, document or scout file.</param>
/// <param name="State">Where it is in the pipeline.</param>
/// <param name="PlannedRenditions">How many renditions are planned (videos; 0 before the source was probed).</param>
/// <param name="ReadyRenditions">How many of them are encoded.</param>
public sealed record AssetBuildRow(AssetKind Kind, ProcessingState State, int PlannedRenditions, int ReadyRenditions);

/// <summary>
/// Where the build of a player's files stands (T-6b): the counts behind the Copy, Transcode and Pages lines of the
/// progress message, the failed files, the overall percentage and the time-left estimate.
/// </summary>
/// <param name="Files">Every file counted (files removed from Drive are left out by the query).</param>
/// <param name="Copied">Files whose original is in storage (processing or ready).</param>
/// <param name="CopyDone">No file is still queued or copying.</param>
/// <param name="Videos">Videos.</param>
/// <param name="VideosReady">Videos ready (transcoded, thumbnails made).</param>
/// <param name="VideosDone">No video is still in the pipeline.</param>
/// <param name="Documents">Documents and scout files.</param>
/// <param name="DocumentsReady">Documents and scout files ready.</param>
/// <param name="DocumentsDone">No document or scout file is still in the pipeline.</param>
/// <param name="Failed">Files that failed.</param>
/// <param name="Percent">The overall progress, 0 to 100, rounded down.</param>
public sealed record BuildProgress(
    int Files,
    int Copied,
    bool CopyDone,
    int Videos,
    int VideosReady,
    bool VideosDone,
    int Documents,
    int DocumentsReady,
    bool DocumentsDone,
    int Failed,
    int Percent)
{
    /// <summary>Gets the progress of a player without files: nothing to show.</summary>
    public static BuildProgress None { get; } = new(0, 0, true, 0, 0, true, 0, 0, true, 0, 0);

    /// <summary>Gets a value indicating whether any file is still queued, copying or processing.</summary>
    public bool IsBuilding => !CopyDone || !VideosDone || !DocumentsDone;

    /// <summary>Gets a value indicating whether the player has files and none is still in the pipeline.</summary>
    public bool IsFinished => Files > 0 && !IsBuilding;

    /// <summary>Works out the progress from a player's files.</summary>
    /// <param name="rows">The files.</param>
    /// <returns>The progress.</returns>
    public static BuildProgress From(IEnumerable<AssetBuildRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var files = rows.ToList();
        if (files.Count == 0)
        {
            return None;
        }

        var videos = files.Where(row => row.Kind == AssetKind.Video).ToList();
        var documents = files.Where(row => row.Kind != AssetKind.Video).ToList();
        var weight = files.Sum(Weight);
        return new BuildProgress(
            Files: files.Count,
            Copied: files.Count(row => row.State is ProcessingState.Processing or ProcessingState.Ready),
            CopyDone: files.TrueForAll(row => row.State is not (ProcessingState.Queued or ProcessingState.Copying)),
            Videos: videos.Count,
            VideosReady: videos.Count(row => row.State == ProcessingState.Ready),
            VideosDone: videos.TrueForAll(IsSettled),
            Documents: documents.Count,
            DocumentsReady: documents.Count(row => row.State == ProcessingState.Ready),
            DocumentsDone: documents.TrueForAll(IsSettled),
            Failed: files.Count(row => row.State == ProcessingState.Failed),
            Percent: (int)decimal.Floor(100m * weight / files.Count));
    }

    /// <summary>Estimates the time left from the time spent so far and the percentage done.</summary>
    /// <param name="startedAt">When the build started, or <see langword="null"/> when unknown.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>The time left, or <see langword="null"/> when it cannot be estimated yet.</returns>
    public TimeSpan? EstimateRemaining(DateTimeOffset? startedAt, DateTimeOffset now)
    {
        if (!IsBuilding || Percent <= 0 || startedAt is not { } start)
        {
            return null;
        }

        var spent = now - start;
        return spent <= TimeSpan.Zero ? null : spent * (100 - Percent) / Percent;
    }

    /// <summary>Tells whether a file is out of the pipeline (ready or failed).</summary>
    /// <param name="row">The file.</param>
    /// <returns><see langword="true"/> when nothing is left to do for it.</returns>
    private static bool IsSettled(AssetBuildRow row) => row.State is ProcessingState.Ready or ProcessingState.Failed;

    /// <summary>Gets how far along one file is, from 0 to 1.</summary>
    /// <param name="row">The file.</param>
    /// <returns>Queued 0, copying 0.1, processing 0.3 (videos add 0.7 by their ready renditions), ready or failed 1.</returns>
    private static decimal Weight(AssetBuildRow row) => row.State switch
    {
        ProcessingState.Queued => 0m,
        ProcessingState.Copying => 0.1m,
        ProcessingState.Processing when row.Kind == AssetKind.Video && row.PlannedRenditions > 0 =>
            0.3m + (0.7m * row.ReadyRenditions / row.PlannedRenditions),
        ProcessingState.Processing => 0.3m,
        _ => 1m,
    };
}
