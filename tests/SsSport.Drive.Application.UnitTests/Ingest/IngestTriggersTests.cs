using Ardalis.Specification;
using NSubstitute;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.UnitTests.Ingest;

/// <summary>
/// What starts each ingest step. Every step is a background job (<see cref="IIngestScheduler"/>), queued after the
/// change that caused it is saved:
/// <list type="bullet">
/// <item>every 10 minutes, and as soon as a folder is linked, its folder sync (I-6);</item>
/// <item>nothing is copied or processed before a player is published (decisions §5); publishing queues the copy of
/// every waiting file, and later new or replaced files of a published player are copied right away (I-2);</item>
/// <item>publishing and every later change of a published player's details remake the QR code (I-8) and the card (I-5);</item>
/// <item>a new photo is cut out (I-7).</item>
/// </list>
/// </summary>
public sealed class IngestTriggersTests
{
    /// <summary>No team member caused the event.</summary>
    private static readonly EventContext _system = new(null);

    /// <summary>The scheduler.</summary>
    private readonly IIngestScheduler _scheduler = Substitute.For<IIngestScheduler>();

    /// <summary>The players.</summary>
    private readonly IRepository<Player, Guid> _players = Substitute.For<IRepository<Player, Guid>>();

    /// <summary>The assets.</summary>
    private readonly IRepository<Asset, Guid> _assets = Substitute.For<IRepository<Asset, Guid>>();

    /// <summary>The folders.</summary>
    private readonly IRepository<DriveFolder, Guid> _folders = Substitute.For<IRepository<DriveFolder, Guid>>();

    /// <summary>The recurring round queues one sync per linked folder, reading the folders a page at a time.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_recurring_round_queues_every_folder_page_by_page()
    {
        var first = Folders(ScheduleFolderSyncsCommandHandler.PageSize);
        var second = Folders(20);
        _folders.ListAsync(Arg.Any<ISpecification<DriveFolder>>(), Arg.Any<CancellationToken>()).Returns(first, second);

        var queued = await new ScheduleFolderSyncsCommandHandler(_folders, _scheduler)
            .Handle(new ScheduleFolderSyncsCommand(), TestContext.Current.CancellationToken);

        queued.Should().Be(ScheduleFolderSyncsCommandHandler.PageSize + 20);
        await _folders.Received(2).ListAsync(Arg.Any<ISpecification<DriveFolder>>(), Arg.Any<CancellationToken>());
        foreach (var folder in first.Concat(second))
        {
            await _scheduler.Received(1).EnqueueAsync(IngestStep.SyncFolder, folder.Id, Arg.Any<CancellationToken>());
        }
    }

    /// <summary>The page size keeps one read small (decisions §2b).</summary>
    [Fact]
    public void The_round_reads_at_most_a_hundred_folders_at_a_time() =>
        ScheduleFolderSyncsCommandHandler.PageSize.Should().BeInRange(1, 100);

    /// <summary>A newly linked folder is synced at once, not up to 10 minutes later.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_linked_folder_is_synced_at_once()
    {
        var folderId = Guid.CreateVersion7(IngestData.Now);

        await new SyncLinkedFolder(_scheduler).HandleAsync(new DriveFolderLinked(folderId, Guid.CreateVersion7(IngestData.Now)), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAsync(IngestStep.SyncFolder, folderId, Arg.Any<CancellationToken>());
    }

    /// <summary>A new file of a published player is copied right away.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_new_file_of_a_published_player_is_copied()
    {
        var player = Stored(IngestData.PublishedPlayer());
        var assetId = Guid.CreateVersion7(IngestData.Now);

        await new QueueAssetIngest(_players, _scheduler).HandleAsync(new AssetAdded(assetId, player.Id), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAssetAsync(IngestStep.CopyOriginal, assetId, IngestLane.Live, Arg.Any<CancellationToken>());
    }

    /// <summary>A replaced file of a published player is copied again.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_replaced_file_of_a_published_player_is_copied_again()
    {
        var player = Stored(IngestData.PublishedPlayer());
        var assetId = Guid.CreateVersion7(IngestData.Now);

        await new QueueAssetIngest(_players, _scheduler).HandleAsync(new AssetReplaced(assetId, player.Id, 2), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAssetAsync(IngestStep.CopyOriginal, assetId, IngestLane.Live, Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing is copied for a player still on the waiting list; publishing will queue it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_new_file_of_a_waiting_player_waits_for_publish()
    {
        var player = Stored(IngestData.WaitingPlayer());

        await new QueueAssetIngest(_players, _scheduler).HandleAsync(new AssetAdded(Guid.CreateVersion7(IngestData.Now), player.Id), _system, TestContext.Current.CancellationToken);

        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAssetAsync(default, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Publishing queues the copy of every waiting file, the QR code and the card.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Publishing_queues_the_files_the_qr_code_and_the_card()
    {
        var player = Stored(IngestData.PublishedPlayer());
        var waiting = new[]
        {
            IngestData.QueuedAsset(player.Id, null, "a.mp4", "d-1"),
            IngestData.QueuedAsset(player.Id, null, "b.pdf", "d-2"),
        };
        _assets.ListAsync(Arg.Any<ISpecification<Asset>>(), Arg.Any<CancellationToken>()).Returns(waiting);

        await new IngestPublishedPlayer(_players, _assets, _scheduler).HandleAsync(new PlayerPublished(player.Id), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAssetAsync(IngestStep.CopyOriginal, waiting[0].Id, IngestLane.Live, Arg.Any<CancellationToken>());
        await _scheduler.Received(1).EnqueueAssetAsync(IngestStep.CopyOriginal, waiting[1].Id, IngestLane.Live, Arg.Any<CancellationToken>());
        await _scheduler.Received(1).EnqueueAsync(IngestStep.RenderQrCode, player.Id, Arg.Any<CancellationToken>());
        await _scheduler.Received(1).EnqueueAsync(IngestStep.RenderCard, player.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>A published player's changed details (e.g. a new name) remake the QR code and the card.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Changed_details_of_a_published_player_remake_its_images()
    {
        var player = Stored(IngestData.PublishedPlayer());

        await new RefreshPlayerImages(_players, _scheduler).HandleAsync(new PlayerDetailsChanged(player.Id), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAsync(IngestStep.RenderQrCode, player.Id, Arg.Any<CancellationToken>());
        await _scheduler.Received(1).EnqueueAsync(IngestStep.RenderCard, player.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>A waiting player has no QR code or card yet; publishing makes them.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Changed_details_of_a_waiting_player_make_nothing()
    {
        var player = Stored(IngestData.WaitingPlayer());

        await new RefreshPlayerImages(_players, _scheduler).HandleAsync(new PlayerDetailsChanged(player.Id), _system, TestContext.Current.CancellationToken);

        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Every new photo is cut out, published or not, so the cut-out is ready when the player goes live.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_new_photo_is_cut_out()
    {
        var playerId = Guid.CreateVersion7(IngestData.Now);

        await new CutOutNewPhoto(_scheduler).HandleAsync(new PlayerPhotoChanged(playerId), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAsync(IngestStep.CutOutPhoto, playerId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// M-4: the files of an archive player are copied and processed on the archive lane, which yields to the team's new
    /// uploads; the QR code and the card stay on the normal queue.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Publishing_an_archive_player_queues_its_files_on_the_archive_lane()
    {
        var player = Stored(IngestData.PublishedArchivePlayer());
        var waiting = IngestData.QueuedAsset(player.Id, null, "a.mp4", "d-1");
        _assets.ListAsync(Arg.Any<ISpecification<Asset>>(), Arg.Any<CancellationToken>()).Returns([waiting]);

        await new IngestPublishedPlayer(_players, _assets, _scheduler).HandleAsync(new PlayerPublished(player.Id), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAssetAsync(IngestStep.CopyOriginal, waiting.Id, IngestLane.Archive, Arg.Any<CancellationToken>());
        await _scheduler.Received(1).EnqueueAsync(IngestStep.RenderQrCode, player.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>M-4: a new file of a published archive player goes on the archive lane too.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_new_file_of_an_archive_player_goes_on_the_archive_lane()
    {
        var player = Stored(IngestData.PublishedArchivePlayer());
        var assetId = Guid.CreateVersion7(IngestData.Now);

        await new QueueAssetIngest(_players, _scheduler).HandleAsync(new AssetAdded(assetId, player.Id), _system, TestContext.Current.CancellationToken);

        await _scheduler.Received(1).EnqueueAssetAsync(IngestStep.CopyOriginal, assetId, IngestLane.Archive, Arg.Any<CancellationToken>());
    }

    /// <summary>Makes the players repository answer a player however the handler reads it.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The player.</returns>
    private Player Stored(Player player)
    {
        _players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        _players.FirstOrDefaultAsync(Arg.Any<ISpecification<Player>>(), Arg.Any<CancellationToken>()).Returns(player);
        return player;
    }

    /// <summary>Builds linked folders.</summary>
    /// <param name="count">How many.</param>
    /// <returns>The folders.</returns>
    private static IReadOnlyList<DriveFolder> Folders(int count) =>
        [.. Enumerable.Range(0, count).Select(i => DriveFolder.Link(Guid.CreateVersion7(IngestData.Now), $"drive-folder-{i:D4}-{Guid.NewGuid():N}", $"SS-SPORT Player {i}", IngestData.Now))];
}
