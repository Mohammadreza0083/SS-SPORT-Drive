using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Assets;

/// <summary>Where an asset's file came from.</summary>
public enum AssetSource
{
    /// <summary>Found in a linked Drive folder by the sync poller or an analyst's submission.</summary>
    Drive,

    /// <summary>Imported from the existing Drive → R2 backup (<c>ssdrivebackup</c>) during the archive migration.</summary>
    Archive,
}

/// <summary>Whether viewers can see an asset.</summary>
public enum AssetVisibility
{
    /// <summary>Shown on the player page.</summary>
    Visible,

    /// <summary>Hidden from viewers; kept in storage and restorable.</summary>
    Hidden,
}

/// <summary>Where an asset is in its processing pipeline.</summary>
public enum ProcessingState
{
    /// <summary>Waiting for a worker.</summary>
    Queued,

    /// <summary>The original is being copied from Drive to storage.</summary>
    Copying,

    /// <summary>The original is in storage; videos are transcoding, documents are being rendered.</summary>
    Processing,

    /// <summary>Everything is done; the asset can be shown.</summary>
    Ready,

    /// <summary>Processing failed; see <see cref="Asset.FailureReason"/>.</summary>
    Failed,
}

/// <summary>Raised when an asset is added to a player.</summary>
/// <param name="AssetId">The asset.</param>
/// <param name="PlayerId">The player.</param>
public sealed record AssetAdded(Guid AssetId, Guid PlayerId) : IDomainEvent;

/// <summary>Raised when an asset finishes processing.</summary>
/// <param name="AssetId">The asset.</param>
/// <param name="PlayerId">The player.</param>
public sealed record AssetReady(Guid AssetId, Guid PlayerId) : IDomainEvent;

/// <summary>Raised when an asset fails processing.</summary>
/// <param name="AssetId">The asset.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="Reason">Why it failed.</param>
public sealed record AssetFailed(Guid AssetId, Guid PlayerId, string Reason) : IDomainEvent;

/// <summary>Raised when an asset is hidden, by hand or because it was removed from Drive.</summary>
/// <param name="AssetId">The asset.</param>
/// <param name="PlayerId">The player.</param>
public sealed record AssetHidden(Guid AssetId, Guid PlayerId) : IDomainEvent;

/// <summary>Raised when an asset's file is replaced by new content and must be processed again.</summary>
/// <param name="AssetId">The asset.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="Version">The new version number.</param>
public sealed record AssetReplaced(Guid AssetId, Guid PlayerId, int Version) : IDomainEvent;

/// <summary>
/// One file of a player (video, document or scout file) with its processing state and, for videos, its renditions.
/// Files are never deleted: removed ones are hidden and can be restored.
/// </summary>
public sealed class Asset : AggregateRoot<Guid>
{
    /// <summary>The longest title.</summary>
    private const int MaxTitleLength = 200;

    /// <summary>The longest stored failure reason.</summary>
    private const int MaxReasonLength = 1000;

    /// <summary>The qualities a video is encoded in, lowest first.</summary>
    private static readonly RenditionQuality[] _ladder = [RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080];

    /// <summary>The planned renditions of a video.</summary>
    private readonly List<Rendition> _renditions = [];

    /// <summary>Initializes an asset; use <see cref="Add"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="playerId">The owning player.</param>
    /// <param name="folderId">The linked Drive folder, if any.</param>
    /// <param name="source">Where the file came from.</param>
    /// <param name="driveFileId">The Google Drive file id, if known.</param>
    /// <param name="file">The source file.</param>
    /// <param name="title">The validated title.</param>
    /// <param name="tags">The tags.</param>
    /// <param name="sortOrder">The position on the page.</param>
    /// <param name="createdAt">When it was added (UTC).</param>
    private Asset(
        Guid id,
        Guid playerId,
        Guid? folderId,
        AssetSource source,
        string? driveFileId,
        AssetFile file,
        string title,
        AssetTags tags,
        int sortOrder,
        DateTimeOffset createdAt)
        : base(id)
    {
        PlayerId = playerId;
        FolderId = folderId;
        Source = source;
        DriveFileId = driveFileId;
        File = file;
        Title = title;
        Tags = tags;
        SortOrder = sortOrder;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the owning player.</summary>
    public Guid PlayerId { get; }

    /// <summary>Gets the linked Drive folder the file sits in, or <see langword="null"/> for archive imports.</summary>
    public Guid? FolderId { get; }

    /// <summary>Gets where the file came from.</summary>
    public AssetSource Source { get; }

    /// <summary>Gets the Google Drive file id, or <see langword="null"/> when unknown.</summary>
    public string? DriveFileId { get; }

    /// <summary>Gets the current source file.</summary>
    public AssetFile File { get; private set; }

    /// <summary>Gets the kind of file.</summary>
    public AssetKind Kind => File.Kind;

    /// <summary>Gets the title shown to viewers.</summary>
    public string Title { get; private set; }

    /// <summary>Gets a value indicating whether the title was edited by hand; Drive renames then leave it alone.</summary>
    public bool TitleIsCustom { get; private set; }

    /// <summary>Gets the tags.</summary>
    public AssetTags Tags { get; private set; }

    /// <summary>Gets the position on the player page (lower first).</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets whether viewers can see the asset.</summary>
    public AssetVisibility Visibility { get; private set; } = AssetVisibility.Visible;

    /// <summary>Gets when the file was removed from Drive (UTC), or <see langword="null"/> while it is still there.</summary>
    public DateTimeOffset? RemovedFromSourceAt { get; private set; }

    /// <summary>Gets the processing state.</summary>
    public ProcessingState State { get; private set; } = ProcessingState.Queued;

    /// <summary>Gets why processing failed, or <see langword="null"/>.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Gets the content version; it goes up each time the file is replaced.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>Gets the storage key of the original file, or <see langword="null"/> until it is in storage.</summary>
    public StorageKey? OriginalKey { get; private set; }

    /// <summary>Gets a video's duration in seconds, or <see langword="null"/>.</summary>
    public double? DurationSeconds { get; private set; }

    /// <summary>Gets a document's page count, or <see langword="null"/>.</summary>
    public int? PageCount { get; private set; }

    /// <summary>Gets when the asset was added (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the planned renditions of a video, lowest quality first.</summary>
    public IReadOnlyList<Rendition> Renditions => _renditions.AsReadOnly();

    /// <summary>Gets a value indicating whether the asset is processed and can be shown.</summary>
    public bool IsReady => State == ProcessingState.Ready;

    /// <summary>Adds a file to a player, queued for processing, and raises <see cref="AssetAdded"/>.</summary>
    /// <param name="playerId">The owning player.</param>
    /// <param name="folderId">The linked Drive folder, if any.</param>
    /// <param name="source">Where the file came from.</param>
    /// <param name="driveFileId">The Google Drive file id, if known.</param>
    /// <param name="file">The source file.</param>
    /// <param name="title">The title (usually from <see cref="AssetTagger"/>).</param>
    /// <param name="tags">The tags (usually from <see cref="AssetTagger"/>).</param>
    /// <param name="sortOrder">The position on the page.</param>
    /// <param name="createdAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The asset.</returns>
    /// <exception cref="DomainException"><c>asset.title_invalid</c> or <c>asset.sort_order_invalid</c>.</exception>
    public static Asset Add(
        Guid playerId,
        Guid? folderId,
        AssetSource source,
        string? driveFileId,
        AssetFile file,
        string title,
        AssetTags tags,
        int sortOrder,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(file);
        var asset = new Asset(
            Guid.CreateVersion7(createdAt),
            playerId,
            folderId,
            source,
            string.IsNullOrWhiteSpace(driveFileId) ? null : driveFileId.Trim(),
            file,
            ValidTitle(title),
            tags,
            ValidSortOrder(sortOrder),
            createdAt);
        asset.RaiseDomainEvent(new AssetAdded(asset.Id, playerId));
        return asset;
    }

    /// <summary>Sets the title by hand; later Drive renames no longer change it.</summary>
    /// <param name="title">The new title.</param>
    public void ChangeTitle(string title)
    {
        Title = ValidTitle(title);
        TitleIsCustom = true;
    }

    /// <summary>Sets the tags by hand.</summary>
    /// <param name="tags">The new tags.</param>
    public void ChangeTags(AssetTags tags) => Tags = tags;

    /// <summary>Moves the asset to a position on the page.</summary>
    /// <param name="sortOrder">The new position.</param>
    public void MoveTo(int sortOrder) => SortOrder = ValidSortOrder(sortOrder);

    /// <summary>Hides the asset from viewers and raises <see cref="AssetHidden"/> if it was visible.</summary>
    public void Hide()
    {
        if (Visibility == AssetVisibility.Hidden)
        {
            return;
        }

        Visibility = AssetVisibility.Hidden;
        RaiseDomainEvent(new AssetHidden(Id, PlayerId));
    }

    /// <summary>Shows the asset again, including one that was removed from Drive (restored from storage).</summary>
    public void Show() => Visibility = AssetVisibility.Visible;

    /// <summary>Records that the file was renamed in Drive; the title follows unless it was edited by hand.</summary>
    /// <param name="originalName">The new file name.</param>
    /// <param name="suggestedTitle">The title suggested for the new name.</param>
    public void RenameInSource(string originalName, string suggestedTitle)
    {
        File = AssetFile.Create(originalName, File.Md5, File.SizeBytes);
        if (!TitleIsCustom)
        {
            Title = ValidTitle(suggestedTitle);
        }
    }

    /// <summary>
    /// Replaces the content with a new version of the file (same Drive file, new md5): bumps the version, drops the
    /// processed output and queues it again. The same md5 changes nothing.
    /// </summary>
    /// <param name="file">The new file.</param>
    /// <exception cref="DomainException"><c>asset.kind_changed</c> when the new file is a different kind.</exception>
    public void ReplaceContent(AssetFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Md5 == File.Md5)
        {
            return;
        }

        if (file.Kind != Kind)
        {
            throw new DomainException("asset.kind_changed", "A replacement must be the same kind of file.");
        }

        File = file;
        Version++;
        ResetProcessing();
        RaiseDomainEvent(new AssetReplaced(Id, PlayerId, Version));
    }

    /// <summary>Records that the file was removed from Drive: it is hidden, never deleted.</summary>
    /// <param name="removedAt">When the removal was detected (UTC).</param>
    public void MarkRemovedFromSource(DateTimeOffset removedAt)
    {
        RemovedFromSourceAt ??= removedAt;
        Hide();
    }

    /// <summary>Starts copying the original from Drive.</summary>
    public void StartCopying()
    {
        EnsureState(ProcessingState.Queued);
        State = ProcessingState.Copying;
    }

    /// <summary>Records where the original is stored (copied, or already in the Drive backup) and starts processing.</summary>
    /// <param name="originalKey">The key of the original.</param>
    public void AttachOriginal(StorageKey originalKey)
    {
        ArgumentNullException.ThrowIfNull(originalKey);
        EnsureState(ProcessingState.Queued, ProcessingState.Copying);
        OriginalKey = originalKey;
        State = ProcessingState.Processing;
    }

    /// <summary>Records a video's duration and plans its renditions, never above the source height (360p is always made).</summary>
    /// <param name="sourceHeight">The source frame height in pixels.</param>
    /// <param name="durationSeconds">The duration in seconds.</param>
    /// <exception cref="DomainException"><c>asset.not_a_video</c> or <c>asset.invalid_state</c>.</exception>
    public void RecordVideoInfo(int sourceHeight, double durationSeconds)
    {
        EnsureKind(AssetKind.Video, "asset.not_a_video", "Only videos have renditions.");
        EnsureState(ProcessingState.Processing);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(durationSeconds);

        DurationSeconds = durationSeconds;
        _renditions.Clear();
        foreach (var quality in _ladder.Where((quality, index) => index == 0 || (int)quality <= sourceHeight))
        {
            _renditions.Add(new Rendition(Guid.CreateVersion7(), quality));
        }
    }

    /// <summary>Marks one rendition encoded; when all are, the video is ready and <see cref="AssetReady"/> is raised.</summary>
    /// <param name="quality">The encoded quality.</param>
    /// <param name="playlistKey">The key of its playlist.</param>
    /// <exception cref="DomainException"><c>asset.rendition_not_planned</c> or <c>asset.invalid_state</c>.</exception>
    public void MarkRenditionReady(RenditionQuality quality, StorageKey playlistKey)
    {
        ArgumentNullException.ThrowIfNull(playlistKey);
        EnsureState(ProcessingState.Processing);
        var rendition = _renditions.Find(r => r.Quality == quality)
            ?? throw new DomainException("asset.rendition_not_planned", $"No {(int)quality}p rendition is planned for this video.");

        rendition.MarkReady(playlistKey);
        if (_renditions.TrueForAll(r => r.Status == RenditionStatus.Ready))
        {
            BecomeReady();
        }
    }

    /// <summary>Finishes a document or scout file and raises <see cref="AssetReady"/>.</summary>
    /// <param name="pageCount">The page count for PDFs and images; <see langword="null"/> for scout files.</param>
    /// <exception cref="DomainException"><c>asset.not_a_document</c> or <c>asset.invalid_state</c>.</exception>
    public void CompleteDocument(int? pageCount)
    {
        if (Kind == AssetKind.Video)
        {
            throw new DomainException("asset.not_a_document", "Videos become ready through their renditions.");
        }

        EnsureState(ProcessingState.Processing);
        if (pageCount is { } pages)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pages, nameof(pageCount));
        }

        PageCount = pageCount;
        BecomeReady();
    }

    /// <summary>Marks processing failed and raises <see cref="AssetFailed"/>. Ready assets cannot fail.</summary>
    /// <param name="reason">Why it failed, e.g. the ffmpeg error.</param>
    public void Fail(string reason)
    {
        EnsureState(ProcessingState.Queued, ProcessingState.Copying, ProcessingState.Processing);
        var text = (reason ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            text = "Unknown error.";
        }

        FailureReason = text.Length > MaxReasonLength ? text[..MaxReasonLength] : text;
        State = ProcessingState.Failed;
        RaiseDomainEvent(new AssetFailed(Id, PlayerId, FailureReason));
    }

    /// <summary>Queues a failed asset again from the start.</summary>
    public void Retry()
    {
        EnsureState(ProcessingState.Failed);
        ResetProcessing();
    }

    /// <summary>Validates a title.</summary>
    /// <param name="title">The raw title.</param>
    /// <returns>The trimmed title.</returns>
    private static string ValidTitle(string? title)
    {
        var text = (title ?? string.Empty).Trim();
        return text.Length is > 0 and <= MaxTitleLength
            ? text
            : throw new DomainException("asset.title_invalid", $"A title needs 1 to {MaxTitleLength} characters.");
    }

    /// <summary>Validates a sort order.</summary>
    /// <param name="sortOrder">The position.</param>
    /// <returns>The position.</returns>
    private static int ValidSortOrder(int sortOrder) =>
        sortOrder >= 0 ? sortOrder : throw new DomainException("asset.sort_order_invalid", "The position cannot be negative.");

    /// <summary>Throws unless the asset is of the given kind.</summary>
    /// <param name="kind">The required kind.</param>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    private void EnsureKind(AssetKind kind, string code, string message)
    {
        if (Kind != kind)
        {
            throw new DomainException(code, message);
        }
    }

    /// <summary>Throws unless the asset is in one of the given states.</summary>
    /// <param name="allowed">The allowed states.</param>
    private void EnsureState(params ProcessingState[] allowed)
    {
        if (!allowed.Contains(State))
        {
            throw new DomainException("asset.invalid_state", $"This step is not possible while the asset is {State}.");
        }
    }

    /// <summary>Marks the asset ready and raises <see cref="AssetReady"/>.</summary>
    private void BecomeReady()
    {
        State = ProcessingState.Ready;
        FailureReason = null;
        RaiseDomainEvent(new AssetReady(Id, PlayerId));
    }

    /// <summary>Drops all processed output and queues the asset again.</summary>
    private void ResetProcessing()
    {
        State = ProcessingState.Queued;
        FailureReason = null;
        OriginalKey = null;
        DurationSeconds = null;
        PageCount = null;
        _renditions.Clear();
    }
}
