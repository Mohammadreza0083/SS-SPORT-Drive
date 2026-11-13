using Ardalis.Specification;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;

namespace SsSport.Drive.Application.UnitTests.Ingest;

/// <summary>
/// I-6: one sync of a linked Drive folder compares the I-1 listing with the player's assets. New files become queued
/// assets (tag and title from <see cref="AssetTagger"/>, appended after the player's last file); renamed, replaced,
/// removed and returned files follow the D-6 rules; a folder that cannot be read is marked lost and nothing else
/// changes. A truncated listing never removes anything, and running the same listing twice changes nothing.
/// </summary>
public sealed class SyncDriveFolderTests
{
    /// <summary>The player's assets as the repository holds them.</summary>
    private readonly List<Asset> _store = [];

    /// <summary>The folders.</summary>
    private readonly IRepository<DriveFolder, Guid> _folders = Substitute.For<IRepository<DriveFolder, Guid>>();

    /// <summary>The assets.</summary>
    private readonly IRepository<Asset, Guid> _assets = Substitute.For<IRepository<Asset, Guid>>();

    /// <summary>The Drive listing.</summary>
    private readonly IDriveFolderLister _lister = Substitute.For<IDriveFolderLister>();

    /// <summary>The player.</summary>
    private readonly Guid _playerId = Guid.CreateVersion7(IngestData.Now);

    /// <summary>The linked folder.</summary>
    private readonly DriveFolder _folder;

    /// <summary>Links a folder and wires the repositories to <see cref="_store"/>.</summary>
    public SyncDriveFolderTests()
    {
        _folder = DriveFolder.Link(_playerId, "drive-folder-1", "SS-SPORT Ali Hoshmand", IngestData.Now.AddDays(-1));
        _folder.ClearDomainEvents();
        _folders.GetByIdAsync(_folder.Id, Arg.Any<CancellationToken>()).Returns(_folder);
        _assets.ListAsync(Arg.Any<ISpecification<Asset>>(), Arg.Any<CancellationToken>()).Returns(_ => _store.ToList());
        _assets.When(repository => repository.Add(Arg.Any<Asset>())).Do(call => _store.Add(call.Arg<Asset>()));
    }

    /// <summary>New files are added, queued, tagged from their subfolder and appended after the player's last file.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task New_files_are_added_after_the_last_file()
    {
        _store.Add(IngestData.QueuedAsset(_playerId, Guid.CreateVersion7(IngestData.Now), "old.mp4", "old-1", sortOrder: 4));
        Listing(
            Entry("f-1", "clip 1.mp4", IngestData.Md5, "Full matches", "LEMESOS VS AEL"),
            Entry("f-2", "stats.pdf", IngestData.OtherMd5, "STATS"));

        var result = await Sync();

        result.Outcome.Should().Be(FolderSyncOutcome.Synced);
        result.Added.Should().Be(2);
        var added = _store.Where(asset => asset.FolderId == _folder.Id).OrderBy(asset => asset.SortOrder).ToList();
        added.Select(asset => (asset.DriveFileId, asset.SortOrder, asset.Title, asset.Tags, asset.Kind)).Should().Equal(
            ("f-1", 5, "LEMESOS VS AEL", AssetTags.FullMatch, AssetKind.Video),
            ("f-2", 6, "stats", AssetTags.Stats, AssetKind.Document));
        added.Should().AllSatisfy(asset =>
        {
            asset.PlayerId.Should().Be(_playerId);
            asset.Source.Should().Be(AssetSource.Drive);
            asset.State.Should().Be(ProcessingState.Queued);
            asset.DomainEvents.Should().ContainSingle(e => e is AssetAdded);
        });
    }

    /// <summary>A file Drive gives no md5 for cannot be deduplicated or checked, so it is skipped and counted.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_file_without_an_md5_is_skipped()
    {
        Listing(Entry("f-1", "clip.mp4", md5: null));

        var result = await Sync();

        result.Added.Should().Be(0);
        result.Skipped.Should().Be(1);
        _store.Should().BeEmpty();
    }

    /// <summary>A renamed file keeps its asset; the title follows unless someone edited it by hand.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_renamed_file_keeps_its_asset_and_a_custom_title()
    {
        var followed = Known("f-1", "old name.mp4");
        var custom = Known("f-2", "game.mp4");
        custom.ChangeTitle("Final vs Paykan");
        Listing(Entry("f-1", "new name.mp4"), Entry("f-2", "game renamed.mp4", IngestData.Md5));

        var result = await Sync();

        result.Renamed.Should().Be(2);
        result.Added.Should().Be(0);
        followed.File.OriginalName.Should().Be("new name.mp4");
        followed.Title.Should().Be("new name");
        custom.Title.Should().Be("Final vs Paykan");
    }

    /// <summary>The same Drive file with a new md5 is new content: the version goes up and it is queued again.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_replaced_file_is_queued_again_as_a_new_version()
    {
        var asset = Known("f-1", "game.mp4");
        asset.AttachOriginal(Domain.Primitives.StorageKey.Create($"originals/{IngestData.Md5}.mp4"));
        Listing(Entry("f-1", "game.mp4", IngestData.OtherMd5));

        var result = await Sync();

        result.Replaced.Should().Be(1);
        asset.Version.Should().Be(2);
        asset.State.Should().Be(ProcessingState.Queued);
        asset.File.Md5.Should().Be(IngestData.OtherMd5);
    }

    /// <summary>A replacement of another kind cannot reuse the asset: the old one is marked removed and a new one is added.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_replacement_of_another_kind_becomes_a_new_asset()
    {
        var old = Known("f-1", "report.mp4");
        Listing(Entry("f-1", "report.pdf", IngestData.OtherMd5));

        var result = await Sync();

        old.RemovedFromSourceAt.Should().Be(IngestData.Now);
        old.Visibility.Should().Be(AssetVisibility.Hidden);
        result.Added.Should().Be(1);
        _store.Should().ContainSingle(asset => asset.Kind == AssetKind.Document && asset.DriveFileId == "f-1");
    }

    /// <summary>
    /// A file of this folder that is gone from Drive is hidden and marked removed, never deleted. Files of the player's
    /// other folders and archive imports are not this folder's to remove.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_file_gone_from_the_folder_is_hidden()
    {
        var gone = Known("f-1", "game.mp4");
        var otherFolder = IngestData.QueuedAsset(_playerId, Guid.CreateVersion7(IngestData.Now), "other.mp4", "f-9");
        var archive = Asset.Add(_playerId, null, AssetSource.Archive, null, AssetFile.Create("old.mp4", IngestData.Md5, 10), "Old", AssetTags.Untagged, 1, IngestData.Now);
        _store.Add(otherFolder);
        _store.Add(archive);
        Listing();

        var result = await Sync();

        result.Removed.Should().Be(1);
        gone.RemovedFromSourceAt.Should().Be(IngestData.Now);
        gone.Visibility.Should().Be(AssetVisibility.Hidden);
        otherFolder.RemovedFromSourceAt.Should().BeNull();
        archive.RemovedFromSourceAt.Should().BeNull();
        _assets.DidNotReceive().Remove(Arg.Any<Asset>());
    }

    /// <summary>A listing cut off at the file limit may miss files, so nothing is marked removed from it (decisions §4).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_truncated_listing_removes_nothing()
    {
        var known = Known("f-1", "game.mp4");
        Listing(truncated: true, Entry("f-2", "new.mp4", IngestData.OtherMd5));

        var result = await Sync();

        result.Removed.Should().Be(0);
        result.Added.Should().Be(1);
        known.RemovedFromSourceAt.Should().BeNull();
    }

    /// <summary>A removed file that is put back in Drive is shown again.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_file_put_back_is_shown_again()
    {
        var asset = Known("f-1", "game.mp4");
        asset.MarkRemovedFromSource(IngestData.Now.AddDays(-2));
        Listing(Entry("f-1", "game.mp4"));

        var result = await Sync();

        result.Returned.Should().Be(1);
        asset.RemovedFromSourceAt.Should().BeNull();
        asset.Visibility.Should().Be(AssetVisibility.Visible);
    }

    /// <summary>A folder the service account can no longer read is marked lost (admins are alerted by the event); no file changes.</summary>
    /// <param name="status">How Drive answered.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(DriveFolderStatus.NotFound)]
    [InlineData(DriveFolderStatus.OutsideSharedDrive)]
    [InlineData(DriveFolderStatus.NotAFolder)]
    public async Task A_folder_that_cannot_be_read_is_marked_lost(DriveFolderStatus status)
    {
        var known = Known("f-1", "game.mp4");
        _lister.ListAsync(_folder.DriveFolderId, Arg.Any<CancellationToken>()).Returns(DriveFolderListing.Missing(status));

        var result = await Sync();

        result.Outcome.Should().Be(FolderSyncOutcome.AccessLost);
        _folder.Access.Should().Be(FolderAccess.Lost);
        _folder.DomainEvents.Should().ContainSingle(e => e is FolderAccessLost);
        known.RemovedFromSourceAt.Should().BeNull();
        _folder.LastSyncedAt.Should().BeNull();
    }

    /// <summary>A good sync records the time, takes the folder's new Drive name and restores lost access.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_good_sync_records_the_time_and_restores_access()
    {
        _folder.MarkAccessLost(IngestData.Now.AddHours(-1));
        _folder.ClearDomainEvents();
        _lister.ListAsync(_folder.DriveFolderId, Arg.Any<CancellationToken>())
            .Returns(new DriveFolderListing(DriveFolderStatus.Found, "SS-SPORT Ali Hoshmand／OH", [], 0, false));

        await Sync();

        _folder.LastSyncedAt.Should().Be(IngestData.Now);
        _folder.Name.Should().Be("SS-SPORT Ali Hoshmand／OH");
        _folder.Access.Should().Be(FolderAccess.Ok);
        _folder.DomainEvents.Should().ContainSingle(e => e is FolderAccessRestored);
    }

    /// <summary>Hangfire runs a job at least once: the same listing twice adds and changes nothing the second time.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_same_listing_twice_changes_nothing()
    {
        Listing(Entry("f-1", "clip.mp4"), Entry("f-2", "stats.pdf", IngestData.OtherMd5, "STATS"));
        await Sync();

        var second = await Sync();

        _store.Should().HaveCount(2);
        (second.Added, second.Renamed, second.Replaced, second.Removed, second.Returned).Should().Be((0, 0, 0, 0, 0));
    }

    /// <summary>A Drive outage fails the job (Hangfire retries it) and changes nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_Drive_outage_fails_the_job_and_changes_nothing()
    {
        _lister.ListAsync(_folder.DriveFolderId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceUnavailableException("drive.unavailable", "Google Drive could not be reached."));

        var act = Sync;

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _folder.LastSyncedAt.Should().BeNull();
        _folder.Access.Should().Be(FolderAccess.Ok);
    }

    /// <summary>A folder that was unlinked between scheduling and running is skipped.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_folder_is_skipped()
    {
        var result = await Handler().Handle(new SyncDriveFolderCommand(Guid.CreateVersion7(IngestData.Now)), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(FolderSyncOutcome.NotLinked);
        await _lister.DidNotReceiveWithAnyArgs().ListAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>Runs one sync of the folder.</summary>
    /// <returns>The result.</returns>
    private Task<FolderSyncResult> Sync() =>
        Handler().Handle(new SyncDriveFolderCommand(_folder.Id), TestContext.Current.CancellationToken);

    /// <summary>Builds the handler.</summary>
    /// <returns>The handler.</returns>
    private SyncDriveFolderCommandHandler Handler() => new(_folders, _assets, _lister, new StuckClock(IngestData.Now));

    /// <summary>Stores a known asset of this folder.</summary>
    /// <param name="driveFileId">Its Drive file id.</param>
    /// <param name="name">Its file name.</param>
    /// <returns>The asset.</returns>
    private Asset Known(string driveFileId, string name)
    {
        var asset = IngestData.QueuedAsset(_playerId, _folder.Id, name, driveFileId, sortOrder: _store.Count);
        asset.ClearDomainEvents();
        _store.Add(asset);
        return asset;
    }

    /// <summary>Makes Drive answer a listing of the folder.</summary>
    /// <param name="files">The files.</param>
    private void Listing(params DriveFileEntry[] files) => Listing(false, files);

    /// <summary>Makes Drive answer a listing of the folder.</summary>
    /// <param name="truncated">Whether the walk hit the file limit.</param>
    /// <param name="files">The files.</param>
    private void Listing(bool truncated, params DriveFileEntry[] files) =>
        _lister.ListAsync(_folder.DriveFolderId, Arg.Any<CancellationToken>())
            .Returns(new DriveFolderListing(DriveFolderStatus.Found, _folder.Name, files, 0, truncated));

    /// <summary>Builds one listed file.</summary>
    /// <param name="id">The Drive file id.</param>
    /// <param name="name">The name.</param>
    /// <param name="md5">The md5 Drive reports.</param>
    /// <param name="subfolders">The subfolders from the player folder down.</param>
    /// <returns>The entry.</returns>
    private static DriveFileEntry Entry(string id, string name, string? md5 = IngestData.Md5, params string[] subfolders)
    {
        AssetFile.TryDetectKind(name, out var kind);
        return new DriveFileEntry(id, name, kind, 1234, md5, IngestData.Now.AddHours(-3), subfolders);
    }
}
