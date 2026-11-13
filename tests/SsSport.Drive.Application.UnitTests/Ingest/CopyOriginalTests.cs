using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.UnitTests.Ingest;

/// <summary>
/// I-2: an asset's original is stored once per content, under <c>originals/{md5}{ext}</c> in the media bucket. In order:
/// an original already in the bucket is reused (another player's identical file, or an earlier run); one the rclone
/// backup already holds is copied inside R2 (the backup bucket is read-only); otherwise it is downloaded from Drive into
/// a work folder, its md5 checked, then uploaded. The asset then moves to processing and the processing step is queued.
/// </summary>
public sealed class CopyOriginalTests : IDisposable
{
    /// <summary>The bytes Drive serves for the test file.</summary>
    private static readonly byte[] _content = Encoding.UTF8.GetBytes("pretend this is a full match in mp4");

    /// <summary>The md5 of <see cref="_content"/>.</summary>
    private static readonly string _contentMd5 = Convert.ToHexStringLower(MD5.HashData(_content));

    /// <summary>The assets.</summary>
    private readonly IRepository<Asset, Guid> _assets = Substitute.For<IRepository<Asset, Guid>>();

    /// <summary>The media bucket.</summary>
    private readonly FakeObjectStorage _storage = new();

    /// <summary>The rclone backup.</summary>
    private readonly IBackupArchive _backup = Substitute.For<IBackupArchive>();

    /// <summary>Google Drive downloads.</summary>
    private readonly IDriveFileDownloader _drive = Substitute.For<IDriveFileDownloader>();

    /// <summary>Work folders.</summary>
    private readonly TestWorkspace _workspace = new();

    /// <summary>The scheduler.</summary>
    private readonly IIngestScheduler _scheduler = Substitute.For<IIngestScheduler>();

    /// <summary>The asset under test.</summary>
    private readonly Asset _asset;

    /// <summary>The key the original belongs under.</summary>
    private readonly string _key;

    /// <summary>Stores a queued video whose md5 matches <see cref="_content"/>.</summary>
    public CopyOriginalTests()
    {
        _asset = IngestData.QueuedAsset(Guid.CreateVersion7(IngestData.Now), Guid.CreateVersion7(IngestData.Now), "Game 1.MP4", "drive-file-1", _contentMd5);
        _assets.GetByIdAsync(_asset.Id, Arg.Any<CancellationToken>()).Returns(_asset);
        _key = $"originals/{_contentMd5}.mp4";

        // NSubstitute would answer "" and a stand-in stream by default; nothing found is null.
        _backup.FindByMd5Async(default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs((string?)null);
        _drive.OpenAsync(default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs((Stream?)null);
    }

    /// <summary>The original key is the md5 plus the lower-case extension, so identical content is stored once.</summary>
    [Fact]
    public void The_original_key_is_the_md5_and_the_extension() =>
        OriginalKeys.For(_asset.File).Should().Be(_key);

    /// <summary>Each kind is stored with its MIME type.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="contentType">The expected MIME type.</param>
    [Theory]
    [InlineData("a.mp4", "video/mp4")]
    [InlineData("a.MOV", "video/quicktime")]
    [InlineData("a.m4v", "video/x-m4v")]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.PNG", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("&a.dvw", "text/plain")]
    public void Each_kind_has_its_content_type(string name, string contentType) =>
        OriginalKeys.ContentType(name).Should().Be(contentType);

    /// <summary>An original already in the bucket is reused: no backup lookup, no download.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_original_already_stored_is_reused()
    {
        _storage.Seed(_key, _content, "video/mp4");

        await Copy();

        _asset.OriginalKey!.Value.Should().Be(_key);
        _asset.State.Should().Be(ProcessingState.Processing);
        _storage.Writes.Should().BeEmpty();
        await _backup.DidNotReceiveWithAnyArgs().FindByMd5Async(default!, TestContext.Current.CancellationToken);
        await _drive.DidNotReceiveWithAnyArgs().OpenAsync(default!, TestContext.Current.CancellationToken);
        await _scheduler.Received(1).EnqueueAsync(IngestStep.ProcessAsset, _asset.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>An original the rclone backup holds is copied inside R2, never downloaded from Drive.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_original_in_the_backup_is_copied_inside_storage()
    {
        const string backupKey = "Men Volleyball/SS-SPORT Ali Hoshmand／OH/Full matches/Game 1.MP4";
        _backup.FindByMd5Async(_contentMd5, Arg.Any<CancellationToken>()).Returns(backupKey);

        await Copy();

        await _backup.Received(1).CopyToMediaAsync(backupKey, _key, Arg.Any<CancellationToken>());
        await _drive.DidNotReceiveWithAnyArgs().OpenAsync(default!, TestContext.Current.CancellationToken);
        _asset.OriginalKey!.Value.Should().Be(_key);
        _asset.State.Should().Be(ProcessingState.Processing);
        await _scheduler.Received(1).EnqueueAsync(IngestStep.ProcessAsset, _asset.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>Otherwise the file is downloaded from Drive, checked and uploaded with its MIME type; the work folder is cleaned up.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Otherwise_the_file_is_downloaded_from_Drive()
    {
        _drive.OpenAsync("drive-file-1", Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(_content));

        await Copy();

        _storage.Bytes(_key).Should().Equal(_content);
        _storage.ContentType(_key).Should().Be("video/mp4");
        _asset.OriginalKey!.Value.Should().Be(_key);
        _asset.State.Should().Be(ProcessingState.Processing);
        _workspace.AllCleanedUp.Should().BeTrue();
        await _scheduler.Received(1).EnqueueAsync(IngestStep.ProcessAsset, _asset.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>Content that does not match the md5 Drive listed is never stored; the asset fails with a reason.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Content_with_the_wrong_md5_is_refused()
    {
        _drive.OpenAsync("drive-file-1", Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("something else")));

        await Copy();

        _storage.Keys.Should().BeEmpty();
        _asset.State.Should().Be(ProcessingState.Failed);
        _asset.FailureReason.Should().Contain("md5");
        _workspace.AllCleanedUp.Should().BeTrue();
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A file deleted from Drive before it was copied fails the asset; the next sync will hide it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_file_gone_from_Drive_fails_the_asset()
    {
        _drive.OpenAsync("drive-file-1", Arg.Any<CancellationToken>()).Returns((Stream?)null);

        await Copy();

        _asset.State.Should().Be(ProcessingState.Failed);
        _asset.FailureReason.Should().Contain("Drive");
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>An archive import without a Drive id that is in neither bucket cannot be found anywhere.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_original_found_nowhere_fails_the_asset()
    {
        var archive = Asset.Add(Guid.CreateVersion7(IngestData.Now), null, AssetSource.Archive, null, AssetFile.Create("old.mp4", IngestData.Md5, 10), "Old", AssetTags.Untagged, 0, IngestData.Now);
        _assets.GetByIdAsync(archive.Id, Arg.Any<CancellationToken>()).Returns(archive);

        await Handler().Handle(new CopyOriginalCommand(archive.Id), TestContext.Current.CancellationToken);

        archive.State.Should().Be(ProcessingState.Failed);
        await _drive.DidNotReceiveWithAnyArgs().OpenAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>Hangfire may run a job twice: an asset that is no longer queued is left alone.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_asset_past_the_copy_is_left_alone()
    {
        _asset.AttachOriginal(Domain.Primitives.StorageKey.Create(_key));

        await Copy();

        _storage.Writes.Should().BeEmpty();
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>An asset deleted in the meantime is skipped.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_asset_is_skipped()
    {
        await Handler().Handle(new CopyOriginalCommand(Guid.CreateVersion7(IngestData.Now)), TestContext.Current.CancellationToken);

        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A Drive outage fails the job so Hangfire retries it later; nothing is stored or queued.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_Drive_outage_is_retried_later()
    {
        _drive.OpenAsync("drive-file-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceUnavailableException("drive.unavailable", "Google Drive could not be reached."));

        var act = Copy;

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _storage.Keys.Should().BeEmpty();
        _workspace.AllCleanedUp.Should().BeTrue();
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Deletes the work folders.</summary>
    public void Dispose() => _workspace.Dispose();

    /// <summary>Runs the copy of the test asset.</summary>
    /// <returns>A task that completes when the handler is done.</returns>
    private Task Copy() => Handler().Handle(new CopyOriginalCommand(_asset.Id), TestContext.Current.CancellationToken);

    /// <summary>Builds the handler.</summary>
    /// <returns>The handler.</returns>
    private CopyOriginalCommandHandler Handler() => new(_assets, _storage, _backup, _drive, _workspace, _scheduler);
}
