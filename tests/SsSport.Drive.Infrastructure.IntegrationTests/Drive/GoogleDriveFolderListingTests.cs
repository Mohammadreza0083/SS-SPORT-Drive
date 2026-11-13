using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Infrastructure.Drive;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Drive;

/// <summary>
/// The Drive reader for ingest (I-1): every usable file in a player folder and its subfolders is listed with its id,
/// kind, size, md5, change time and subfolder path. The fixture follows the real archive layout (decisions §4a).
/// </summary>
public sealed class GoogleDriveFolderListingTests
{
    /// <summary>The agency's Shared Drive.</summary>
    private const string SharedDrive = "0AAgencyDrive";

    /// <summary>Drive's folder MIME type.</summary>
    private const string Folder = DriveItem.FolderMimeType;

    /// <summary>A fixed change time.</summary>
    private static readonly DateTimeOffset _changed = new(2026, 11, 1, 10, 30, 0, TimeSpan.Zero);

    /// <summary>The stand-in Drive API.</summary>
    private readonly FakeDriveApi _api = new();

    /// <summary>The reader under test.</summary>
    private readonly GoogleDriveFolderReader _reader;

    /// <summary>Initializes the reader over the stand-in API.</summary>
    public GoogleDriveFolderListingTests() =>
        _reader = new GoogleDriveFolderReader(_api, Options.Create(new GoogleDriveOptions { SharedDriveId = SharedDrive }));

    /// <summary>Every supported file is listed with its subfolder path, so the tagger sees the folder names.</summary>
    [Fact]
    public async Task Lists_files_with_their_subfolder_path()
    {
        SeedArchivePlayer();

        var listing = await _reader.ListAsync("player", TestContext.Current.CancellationToken);

        listing.Status.Should().Be(DriveFolderStatus.Found);
        listing.Name.Should().Be("SS-SPORT Ali Hoshmand／OH");
        listing.IsTruncated.Should().BeFalse();
        listing.Files.Select(file => (file.Name, string.Join('/', file.Subfolders))).Should().BeEquivalentTo(
        [
            ("stats.pdf", string.Empty),
            ("stats.PNG", string.Empty),
            ("hl 1.mp4", "Highlights 2025／2026"),
            ("hl 2.MOV", "Highlights 2025／2026"),
            ("table.jpg", "STATS"),
            ("game.mp4", "FULL MATCHES /LEMESOS VS AEL"),
            ("&game.dvw", "FULL MATCHES /LEMESOS VS AEL"),
        ]);
    }

    /// <summary>Each entry carries what ingest and sync need: id, kind, size, md5 and change time.</summary>
    [Fact]
    public async Task Entries_carry_kind_size_md5_and_change_time()
    {
        SeedArchivePlayer();

        var listing = await _reader.ListAsync("player", TestContext.Current.CancellationToken);

        var game = listing.Files.Single(file => file.Name == "game.mp4");
        game.FileId.Should().Be("v3");
        game.Kind.Should().Be(AssetKind.Video);
        game.SizeBytes.Should().Be(5000);
        game.Md5.Should().Be("0123456789abcdef0123456789abcdef");
        game.ModifiedAt.Should().Be(_changed);
        listing.Files.Single(file => file.Name == "&game.dvw").Kind.Should().Be(AssetKind.Scout);
        listing.Files.Single(file => file.Name == "stats.PNG").Kind.Should().Be(AssetKind.Document);
    }

    /// <summary>Drive md5s are stored lower-case, whatever case they come in.</summary>
    [Fact]
    public async Task Md5_is_lower_case()
    {
        _api.Add(new DriveItem("player", "SS-SPORT Case", Folder, null, SharedDrive), parent: null);
        _api.Add(new DriveItem("v", "clip.mp4", "video/mp4", 10, SharedDrive, Md5Checksum: "ABCDEF0123456789ABCDEF0123456789"), "player");

        var listing = await _reader.ListAsync("player", TestContext.Current.CancellationToken);

        listing.Files.Single().Md5.Should().Be("abcdef0123456789abcdef0123456789");
    }

    /// <summary>Unsupported types, Google Docs, empty files and extension-less markers are skipped and counted.</summary>
    [Fact]
    public async Task Junk_is_skipped_and_counted()
    {
        _api.Add(new DriveItem("player", "SS-SPORT Junk", Folder, null, SharedDrive), parent: null);
        _api.Add(new DriveItem("ok", "clip.mp4", "video/mp4", 10, SharedDrive), "player");
        _api.Add(new DriveItem("docx", "notes.docx", "application/vnd.openxmlformats", 60, SharedDrive), "player");
        _api.Add(new DriveItem("gdoc", "Plan", "application/vnd.google-apps.document", null, SharedDrive), "player");
        _api.Add(new DriveItem("empty", "empty.mp4", "video/mp4", 0, SharedDrive), "player");
        _api.Add(new DriveItem("marker", "Full matches", "application/octet-stream", 0, SharedDrive), "player");

        var listing = await _reader.ListAsync("player", TestContext.Current.CancellationToken);

        listing.Files.Select(file => file.FileId).Should().Equal("ok");
        listing.SkippedCount.Should().Be(4);
    }

    /// <summary>Long folders come in several pages; every page of every subfolder is read.</summary>
    [Fact]
    public async Task Reads_every_page()
    {
        _api.PageSize = 2;
        _api.Add(new DriveItem("player", "SS-SPORT Paged", Folder, null, SharedDrive), parent: null);
        _api.Add(new DriveItem("hl", "Highlights", Folder, null, SharedDrive), "player");
        for (var i = 0; i < 5; i++)
        {
            _api.Add(new DriveItem($"v{i}", $"clip {i}.mp4", "video/mp4", 10, SharedDrive), "hl");
        }

        var listing = await _reader.ListAsync("player", TestContext.Current.CancellationToken);

        listing.Files.Should().HaveCount(5).And.OnlyContain(file => file.Subfolders.SequenceEqual(new[] { "Highlights" }));
    }

    /// <summary>A huge tree stops at the limit and says so, so ingest never mistakes it for the whole folder.</summary>
    [Fact]
    public async Task Stops_at_the_limit_and_says_so()
    {
        _api.Add(new DriveItem("player", "SS-SPORT Huge", Folder, null, SharedDrive), parent: null);
        for (var i = 0; i < GoogleDriveFolderReader.MaxItems + 10; i++)
        {
            _api.Add(new DriveItem($"v{i}", $"clip {i}.mp4", "video/mp4", 1, SharedDrive), "player");
        }

        var listing = await _reader.ListAsync("player", TestContext.Current.CancellationToken);

        listing.IsTruncated.Should().BeTrue();
        listing.Files.Should().HaveCount(GoogleDriveFolderReader.MaxItems);
    }

    /// <summary>Subfolders deeper than the limit are not walked.</summary>
    [Fact]
    public async Task Does_not_walk_below_the_depth_limit()
    {
        _api.Add(new DriveItem("player", "SS-SPORT Deep", Folder, null, SharedDrive), parent: null);
        var parent = "player";
        for (var level = 1; level <= GoogleDriveFolderReader.MaxDepth + 1; level++)
        {
            _api.Add(new DriveItem($"f{level}", $"L{level}", Folder, null, SharedDrive), parent);
            _api.Add(new DriveItem($"v{level}", $"clip {level}.mp4", "video/mp4", 1, SharedDrive), $"f{level}");
            parent = $"f{level}";
        }

        var listing = await _reader.ListAsync("player", TestContext.Current.CancellationToken);

        listing.Files.Max(file => file.Subfolders.Count).Should().Be(GoogleDriveFolderReader.MaxDepth - 1);
    }

    /// <summary>Folders that cannot be used get the same answers as the folder check.</summary>
    /// <param name="folderId">The folder to list.</param>
    /// <param name="expected">The status.</param>
    [Theory]
    [InlineData("nothing", DriveFolderStatus.NotFound)]
    [InlineData("trashed", DriveFolderStatus.NotFound)]
    [InlineData("file", DriveFolderStatus.NotAFolder)]
    [InlineData("mine", DriveFolderStatus.OutsideSharedDrive)]
    public async Task Unusable_folders_are_reported(string folderId, DriveFolderStatus expected)
    {
        _api.Add(new DriveItem("trashed", "Old", Folder, null, SharedDrive, Trashed: true), parent: null);
        _api.Add(new DriveItem("file", "clip.mp4", "video/mp4", 10, SharedDrive), parent: null);
        _api.Add(new DriveItem("mine", "My own folder", Folder, null, DriveId: null), parent: null);

        var listing = await _reader.ListAsync(folderId, TestContext.Current.CancellationToken);

        listing.Status.Should().Be(expected);
        listing.Files.Should().BeEmpty();
    }

    /// <summary>A Drive outage in the middle of the walk is an error, never a partial listing.</summary>
    [Fact]
    public async Task Outage_during_the_walk_is_an_error()
    {
        _api.Add(new DriveItem("player", "SS-SPORT Outage", Folder, null, SharedDrive), parent: null);
        var api = new FailingListApi(_api);
        var reader = new GoogleDriveFolderReader(api, Options.Create(new GoogleDriveOptions { SharedDriveId = SharedDrive }));

        var list = () => reader.ListAsync("player", TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<ServiceUnavailableException>().Where(exception => exception.Code == "drive.unavailable");
    }

    /// <summary>Without a configured Shared Drive nothing can be listed.</summary>
    [Fact]
    public async Task Missing_configuration_is_an_error()
    {
        var reader = new GoogleDriveFolderReader(_api, Options.Create(new GoogleDriveOptions()));

        var list = () => reader.ListAsync("player", TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<ServiceUnavailableException>();
    }

    /// <summary>Builds a player folder shaped like the real archive: messy subfolder names, nested match folders, files at the top.</summary>
    private void SeedArchivePlayer()
    {
        const string md5 = "0123456789abcdef0123456789abcdef";
        _api.Add(new DriveItem("player", "SS-SPORT Ali Hoshmand／OH", Folder, null, SharedDrive), parent: null);
        _api.Add(new DriveItem("d1", "stats.pdf", "application/pdf", 40, SharedDrive, Md5Checksum: md5), "player");
        _api.Add(new DriveItem("hl", "Highlights 2025／2026", Folder, null, SharedDrive), "player");
        _api.Add(new DriveItem("fm", "FULL MATCHES ", Folder, null, SharedDrive), "player");
        _api.Add(new DriveItem("st", "STATS", Folder, null, SharedDrive), "player");
        _api.Add(new DriveItem("d2", "stats.PNG", "image/png", 50, SharedDrive, Md5Checksum: md5), "player");
        _api.Add(new DriveItem("match", "LEMESOS VS AEL", Folder, null, SharedDrive), "fm");
        _api.Add(new DriveItem("v1", "hl 1.mp4", "video/mp4", 100, SharedDrive, Md5Checksum: md5), "hl");
        _api.Add(new DriveItem("v2", "hl 2.MOV", "video/quicktime", 200, SharedDrive, Md5Checksum: md5), "hl");
        _api.Add(new DriveItem("d3", "table.jpg", "image/jpeg", 70, SharedDrive, Md5Checksum: md5), "st");
        _api.Add(new DriveItem("v3", "game.mp4", "video/mp4", 5000, SharedDrive, Md5Checksum: md5, ModifiedTime: _changed), "match");
        _api.Add(new DriveItem("s1", "&game.dvw", "application/octet-stream", 30, SharedDrive, Md5Checksum: md5), "match");
    }

    /// <summary>Reads the root fine but fails every listing, to simulate Drive dropping mid-walk.</summary>
    /// <param name="inner">The API that answers the root read.</param>
    private sealed class FailingListApi(IDriveApi inner) : IDriveApi
    {
        /// <inheritdoc/>
        public Task<DriveItem?> GetAsync(string id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

        /// <inheritdoc/>
        public Task<DrivePage> ListChildrenAsync(string folderId, string? pageToken, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection reset");

        /// <summary>Downloads through the wrapped Drive (I-2's member of <see cref="IDriveApi"/>).</summary>
        /// <param name="fileId">The Drive file id.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The bytes, or <see langword="null"/>.</returns>
        public Task<Stream?> DownloadAsync(string fileId, CancellationToken cancellationToken) => inner.DownloadAsync(fileId, cancellationToken);
    }
}
