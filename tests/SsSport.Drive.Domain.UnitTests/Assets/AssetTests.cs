using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Assets;

/// <summary>
/// The Asset aggregate: one file of a player, its processing lifecycle (queued → copying → processing → ready / failed),
/// its renditions, and the sync rules (rename, replace, removed from Drive).
/// </summary>
public sealed class AssetTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The owning player.</summary>
    private static readonly Guid _playerId = Guid.CreateVersion7(_now);

    /// <summary>A valid md5.</summary>
    private const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>A different valid md5.</summary>
    private const string OtherMd5 = "e4d909c290d0fb1ca068ffaddf22cbd0";

    /// <summary>A new asset is queued, visible, version 1, and raises AssetAdded.</summary>
    [Fact]
    public void A_new_asset_is_queued_and_visible()
    {
        var folderId = Guid.CreateVersion7(_now);
        var asset = Asset.Add(_playerId, folderId, AssetSource.Drive, "drive-file-1", AssetFile.Create("game.mp4", Md5, 1000), "Game", AssetTags.FullMatch, 3, _now);

        asset.Id.Version.Should().Be(7);
        asset.PlayerId.Should().Be(_playerId);
        asset.FolderId.Should().Be(folderId);
        asset.DriveFileId.Should().Be("drive-file-1");
        asset.Source.Should().Be(AssetSource.Drive);
        asset.Kind.Should().Be(AssetKind.Video);
        asset.Title.Should().Be("Game");
        asset.TitleIsCustom.Should().BeFalse();
        asset.Tags.Should().Be(AssetTags.FullMatch);
        asset.SortOrder.Should().Be(3);
        asset.Visibility.Should().Be(AssetVisibility.Visible);
        asset.State.Should().Be(ProcessingState.Queued);
        asset.Version.Should().Be(1);
        asset.Renditions.Should().BeEmpty();
        asset.IsReady.Should().BeFalse();
        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetAdded(asset.Id, _playerId));
    }

    /// <summary>Titles are trimmed, required and at most 200 characters.</summary>
    /// <param name="title">The title.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_titles_are_rejected(string title)
    {
        var add = () => Video(title: title);

        add.Should().Throw<DomainException>().Which.Code.Should().Be("asset.title_invalid");
    }

    /// <summary>Overlong titles are rejected.</summary>
    [Fact]
    public void Overlong_titles_are_rejected()
    {
        var asset = Video();

        var change = () => asset.ChangeTitle(new string('a', 201));

        change.Should().Throw<DomainException>().Which.Code.Should().Be("asset.title_invalid");
    }

    /// <summary>The sort order cannot be negative.</summary>
    [Fact]
    public void Negative_sort_order_is_rejected()
    {
        var asset = Video();

        var move = () => asset.MoveTo(-1);

        move.Should().Throw<DomainException>().Which.Code.Should().Be("asset.sort_order_invalid");
    }

    /// <summary>A video goes queued → copying → processing → ready once every planned rendition is ready.</summary>
    [Fact]
    public void A_video_becomes_ready_when_every_rendition_is_ready()
    {
        var asset = Video();
        asset.ClearDomainEvents();

        asset.StartCopying();
        asset.AttachOriginal(StorageKey.Create("originals/p/a/game.mp4"));
        asset.RecordVideoInfo(sourceHeight: 1080, durationSeconds: 5400);

        asset.State.Should().Be(ProcessingState.Processing);
        asset.OriginalKey!.Value.Should().Be("originals/p/a/game.mp4");
        asset.DurationSeconds.Should().Be(5400);
        asset.Renditions.Select(r => r.Quality).Should().Equal(RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080);

        asset.MarkRenditionReady(RenditionQuality.P360, StorageKey.Create("hls/a/360/index.m3u8"));
        asset.MarkRenditionReady(RenditionQuality.P720, StorageKey.Create("hls/a/720/index.m3u8"));
        asset.IsReady.Should().BeFalse();

        asset.MarkRenditionReady(RenditionQuality.P1080, StorageKey.Create("hls/a/1080/index.m3u8"));

        asset.State.Should().Be(ProcessingState.Ready);
        asset.IsReady.Should().BeTrue();
        asset.Renditions.Should().OnlyContain(r => r.Status == RenditionStatus.Ready && r.PlaylistKey != null);
        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetReady(asset.Id, _playerId));
    }

    /// <summary>Originals already in the Drive backup are used directly, skipping the copy step.</summary>
    [Fact]
    public void An_original_from_the_backup_skips_copying()
    {
        var asset = Asset.Add(_playerId, null, AssetSource.Archive, null, AssetFile.Create("game.mp4", Md5, 1000), "Game", AssetTags.FullMatch, 0, _now);

        asset.AttachOriginal(StorageKey.Create("Men Volleyball/SS-SPORT Ali Hoshmand／OH/Full matches/game.mp4"));

        asset.State.Should().Be(ProcessingState.Processing);
    }

    /// <summary>Renditions are never upscaled above the source height.</summary>
    /// <param name="height">The source height.</param>
    /// <param name="expected">The planned qualities.</param>
    [Theory]
    [InlineData(2160, new[] { RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080 })]
    [InlineData(1080, new[] { RenditionQuality.P360, RenditionQuality.P720, RenditionQuality.P1080 })]
    [InlineData(720, new[] { RenditionQuality.P360, RenditionQuality.P720 })]
    [InlineData(1000, new[] { RenditionQuality.P360, RenditionQuality.P720 })]
    [InlineData(480, new[] { RenditionQuality.P360 })]
    [InlineData(240, new[] { RenditionQuality.P360 })]
    public void Renditions_are_not_upscaled(int height, RenditionQuality[] expected)
    {
        var asset = Processing(Video());

        asset.RecordVideoInfo(height, 60);

        asset.Renditions.Select(r => r.Quality).Should().Equal(expected);
    }

    /// <summary>Video info only applies to videos, and only while processing.</summary>
    [Fact]
    public void Video_info_requires_a_processing_video()
    {
        var document = Processing(Asset.Add(_playerId, null, AssetSource.Drive, "f", AssetFile.Create("r.pdf", Md5, 10), "R", AssetTags.Untagged, 0, _now));
        var queued = Video();

        var onDocument = () => document.RecordVideoInfo(1080, 60);
        var onQueued = () => queued.RecordVideoInfo(1080, 60);

        onDocument.Should().Throw<DomainException>().Which.Code.Should().Be("asset.not_a_video");
        onQueued.Should().Throw<DomainException>().Which.Code.Should().Be("asset.invalid_state");
    }

    /// <summary>A rendition that was not planned cannot be marked ready.</summary>
    [Fact]
    public void Unplanned_renditions_cannot_be_marked_ready()
    {
        var asset = Processing(Video());
        asset.RecordVideoInfo(720, 60);

        var mark = () => asset.MarkRenditionReady(RenditionQuality.P1080, StorageKey.Create("hls/a/1080/index.m3u8"));

        mark.Should().Throw<DomainException>().Which.Code.Should().Be("asset.rendition_not_planned");
    }

    /// <summary>A document becomes ready with its page count; scout files need no page count.</summary>
    [Fact]
    public void Documents_become_ready_with_their_page_count()
    {
        var pdf = Processing(Asset.Add(_playerId, null, AssetSource.Drive, "f", AssetFile.Create("r.pdf", Md5, 10), "R", AssetTags.Stats, 0, _now));
        var scout = Processing(Asset.Add(_playerId, null, AssetSource.Drive, "g", AssetFile.Create("&m.dvw", Md5, 10), "M", AssetTags.FullMatch, 0, _now));

        pdf.CompleteDocument(pageCount: 12);
        scout.CompleteDocument(pageCount: null);

        pdf.State.Should().Be(ProcessingState.Ready);
        pdf.PageCount.Should().Be(12);
        scout.IsReady.Should().BeTrue();
    }

    /// <summary>A video cannot be completed as a document.</summary>
    [Fact]
    public void Videos_cannot_be_completed_as_documents()
    {
        var asset = Processing(Video());

        var complete = () => asset.CompleteDocument(1);

        complete.Should().Throw<DomainException>().Which.Code.Should().Be("asset.not_a_document");
    }

    /// <summary>Failing records the reason and raises AssetFailed; retrying re-queues and clears the plan.</summary>
    [Fact]
    public void Fail_and_retry()
    {
        var asset = Processing(Video());
        asset.RecordVideoInfo(1080, 60);
        asset.ClearDomainEvents();

        asset.Fail(" ffmpeg exited with code 1 ");

        asset.State.Should().Be(ProcessingState.Failed);
        asset.FailureReason.Should().Be("ffmpeg exited with code 1");
        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetFailed(asset.Id, _playerId, "ffmpeg exited with code 1"));

        asset.Retry();

        asset.State.Should().Be(ProcessingState.Queued);
        asset.FailureReason.Should().BeNull();
        asset.Renditions.Should().BeEmpty();
    }

    /// <summary>Only failed assets can be retried, and a ready asset cannot fail.</summary>
    [Fact]
    public void Retry_and_fail_guard_their_states()
    {
        var queued = Video();
        var ready = Processing(Asset.Add(_playerId, null, AssetSource.Drive, "f", AssetFile.Create("r.pdf", Md5, 10), "R", AssetTags.Stats, 0, _now));
        ready.CompleteDocument(1);

        var retry = () => queued.Retry();
        var fail = () => ready.Fail("late error");

        retry.Should().Throw<DomainException>().Which.Code.Should().Be("asset.invalid_state");
        fail.Should().Throw<DomainException>().Which.Code.Should().Be("asset.invalid_state");
    }

    /// <summary>A rename in Drive updates the title, unless someone has edited the title by hand.</summary>
    [Fact]
    public void Renames_do_not_overwrite_a_custom_title()
    {
        var asset = Video();

        asset.RenameInSource("final v2.mp4", "final v2");
        asset.Title.Should().Be("final v2");
        asset.File.OriginalName.Should().Be("final v2.mp4");

        asset.ChangeTitle("Final vs Zenit");
        asset.RenameInSource("final v3.mp4", "final v3");

        asset.Title.Should().Be("Final vs Zenit");
        asset.TitleIsCustom.Should().BeTrue();
        asset.File.OriginalName.Should().Be("final v3.mp4");
    }

    /// <summary>Replacing the content bumps the version and starts processing again; the same md5 changes nothing.</summary>
    [Fact]
    public void Replacing_the_content_reprocesses_a_new_version()
    {
        var asset = Processing(Video());
        asset.RecordVideoInfo(1080, 60);
        asset.ClearDomainEvents();

        asset.ReplaceContent(AssetFile.Create("game.mp4", Md5, 1000));
        asset.Version.Should().Be(1);
        asset.DomainEvents.Should().BeEmpty();

        asset.ReplaceContent(AssetFile.Create("game.mp4", OtherMd5, 2000));

        asset.Version.Should().Be(2);
        asset.File.Md5.Should().Be(OtherMd5);
        asset.State.Should().Be(ProcessingState.Queued);
        asset.OriginalKey.Should().BeNull();
        asset.DurationSeconds.Should().BeNull();
        asset.Renditions.Should().BeEmpty();
        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetReplaced(asset.Id, _playerId, 2));
    }

    /// <summary>A replacement must keep the same kind of file.</summary>
    [Fact]
    public void A_replacement_keeps_the_kind()
    {
        var asset = Video();

        var replace = () => asset.ReplaceContent(AssetFile.Create("game.pdf", OtherMd5, 10));

        replace.Should().Throw<DomainException>().Which.Code.Should().Be("asset.kind_changed");
    }

    /// <summary>A file removed from Drive is hidden (never deleted) and can be shown again.</summary>
    [Fact]
    public void Removed_from_source_hides_and_can_be_restored()
    {
        var asset = Video();
        asset.ClearDomainEvents();

        asset.MarkRemovedFromSource(_now.AddDays(1));

        asset.Visibility.Should().Be(AssetVisibility.Hidden);
        asset.RemovedFromSourceAt.Should().Be(_now.AddDays(1));
        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetHidden(asset.Id, _playerId));

        asset.Show();

        asset.Visibility.Should().Be(AssetVisibility.Visible);
    }

    /// <summary>Hiding twice raises one event; tags and order can be edited by hand.</summary>
    [Fact]
    public void Hide_tags_and_order()
    {
        var asset = Video();
        asset.ClearDomainEvents();

        asset.Hide();
        asset.Hide();
        asset.ChangeTags(AssetTags.Highlight | AssetTags.Spike);
        asset.MoveTo(7);

        asset.DomainEvents.Should().ContainSingle().Which.Should().Be(new AssetHidden(asset.Id, _playerId));
        asset.Tags.Should().Be(AssetTags.Highlight | AssetTags.Spike);
        asset.SortOrder.Should().Be(7);
    }

    /// <summary>Builds a queued Drive video.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The asset.</returns>
    private static Asset Video(string title = "Game") =>
        Asset.Add(_playerId, null, AssetSource.Drive, "drive-file", AssetFile.Create("game.mp4", Md5, 1000), title, AssetTags.FullMatch, 0, _now);

    /// <summary>Moves an asset into processing.</summary>
    /// <param name="asset">The queued asset.</param>
    /// <returns>The same asset.</returns>
    private static Asset Processing(Asset asset)
    {
        asset.StartCopying();
        asset.AttachOriginal(StorageKey.Create("originals/p/a/file"));
        return asset;
    }
}
