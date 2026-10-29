using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Infrastructure.Drive;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Drive;

/// <summary>
/// The live Drive folder check (T-4c): a folder inside the agency's Shared Drive is read recursively and its videos,
/// documents and scout files are counted. Folders elsewhere, missing ones and plain files are reported as such, and
/// a Drive that cannot be reached is an error, never a silent pass.
/// </summary>
public sealed class GoogleDriveFolderReaderTests
{
    /// <summary>The agency's Shared Drive.</summary>
    private const string SharedDrive = "0AAgencyDrive";

    /// <summary>Drive's folder MIME type.</summary>
    private const string Folder = "application/vnd.google-apps.folder";

    /// <summary>The stand-in Drive API.</summary>
    private readonly FakeDriveApi _api = new();

    /// <summary>The reader under test.</summary>
    private readonly GoogleDriveFolderReader _reader;

    /// <summary>Initializes the reader over the stand-in API.</summary>
    public GoogleDriveFolderReaderTests() =>
        _reader = new GoogleDriveFolderReader(_api, Options.Create(new GoogleDriveOptions { SharedDriveId = SharedDrive }));

    /// <summary>A player folder with nested subfolders: every file is counted by kind, and sizes add up.</summary>
    [Fact]
    public async Task Counts_files_in_the_folder_and_its_subfolders()
    {
        _api.Add(new DriveItem("player", "SS-SPORT Ali Hoshmand／OH", Folder, null, SharedDrive), parent: null);
        _api.Add(new DriveItem("hl", "Highlights", Folder, null, SharedDrive), "player");
        _api.Add(new DriveItem("fm", "Full matches", Folder, null, SharedDrive), "player");
        _api.Add(new DriveItem("match", "LEMESOS VS AEL", Folder, null, SharedDrive), "fm");
        _api.Add(new DriveItem("v1", "hl 1.mp4", "video/mp4", 100, SharedDrive), "hl");
        _api.Add(new DriveItem("v2", "hl 2.MOV", "video/quicktime", 200, SharedDrive), "hl");
        _api.Add(new DriveItem("v3", "game.mp4", "video/mp4", 5000, SharedDrive), "match");
        _api.Add(new DriveItem("s1", "&game.dvw", "application/octet-stream", 30, SharedDrive), "match");
        _api.Add(new DriveItem("d1", "stats.pdf", "application/pdf", 40, SharedDrive), "player");
        _api.Add(new DriveItem("d2", "stats.PNG", "image/png", 50, SharedDrive), "player");
        _api.Add(new DriveItem("x1", "notes.docx", "application/vnd.openxmlformats", 60, SharedDrive), "player");

        var summary = await _reader.ReadAsync("player", TestContext.Current.CancellationToken);

        summary.Status.Should().Be(DriveFolderStatus.Found);
        summary.Name.Should().Be("SS-SPORT Ali Hoshmand／OH");
        summary.VideoCount.Should().Be(3);
        summary.DocumentCount.Should().Be(2);
        summary.ScoutCount.Should().Be(1);
        summary.OtherCount.Should().Be(1);
        summary.TotalBytes.Should().Be(5480);
        summary.IsTruncated.Should().BeFalse();
    }

    /// <summary>Long folders come in several pages; every page is read.</summary>
    [Fact]
    public async Task Reads_every_page()
    {
        _api.PageSize = 2;
        _api.Add(new DriveItem("player", "SS-SPORT Paged", Folder, null, SharedDrive), parent: null);
        for (var i = 0; i < 5; i++)
        {
            _api.Add(new DriveItem($"v{i}", $"clip {i}.mp4", "video/mp4", 10, SharedDrive), "player");
        }

        var summary = await _reader.ReadAsync("player", TestContext.Current.CancellationToken);

        summary.VideoCount.Should().Be(5);
        _api.ListCalls.Should().Be(3);
    }

    /// <summary>A folder that is not in the agency's Shared Drive is refused (owner, 2026-10-29).</summary>
    [Fact]
    public async Task Folder_outside_the_shared_drive_is_reported()
    {
        _api.Add(new DriveItem("mine", "My own folder", Folder, null, DriveId: null), parent: null);

        (await _reader.ReadAsync("mine", TestContext.Current.CancellationToken)).Status.Should().Be(DriveFolderStatus.OutsideSharedDrive);
    }

    /// <summary>A link to a file instead of a folder is reported.</summary>
    [Fact]
    public async Task File_is_not_a_folder()
    {
        _api.Add(new DriveItem("file", "clip.mp4", "video/mp4", 10, SharedDrive), parent: null);

        (await _reader.ReadAsync("file", TestContext.Current.CancellationToken)).Status.Should().Be(DriveFolderStatus.NotAFolder);
    }

    /// <summary>Drive answers "not found" both for missing folders and for folders the service account cannot see.</summary>
    [Fact]
    public async Task Missing_or_hidden_folder_is_not_found()
    {
        (await _reader.ReadAsync("nothing", TestContext.Current.CancellationToken)).Status.Should().Be(DriveFolderStatus.NotFound);
    }

    /// <summary>A trashed folder counts as gone.</summary>
    [Fact]
    public async Task Trashed_folder_is_not_found()
    {
        _api.Add(new DriveItem("old", "Old", Folder, null, SharedDrive, Trashed: true), parent: null);

        (await _reader.ReadAsync("old", TestContext.Current.CancellationToken)).Status.Should().Be(DriveFolderStatus.NotFound);
    }

    /// <summary>A huge tree is not walked to the end: counting stops at the limit and says so.</summary>
    [Fact]
    public async Task Stops_counting_at_the_limit()
    {
        _api.Add(new DriveItem("player", "SS-SPORT Huge", Folder, null, SharedDrive), parent: null);
        for (var i = 0; i < GoogleDriveFolderReader.MaxItems + 10; i++)
        {
            _api.Add(new DriveItem($"v{i}", $"clip {i}.mp4", "video/mp4", 1, SharedDrive), "player");
        }

        var summary = await _reader.ReadAsync("player", TestContext.Current.CancellationToken);

        summary.IsTruncated.Should().BeTrue();
        summary.VideoCount.Should().Be(GoogleDriveFolderReader.MaxItems);
    }

    /// <summary>When Drive cannot be reached, the check fails loudly (owner, 2026-10-29: creation stops).</summary>
    [Fact]
    public async Task Unreachable_drive_is_an_error()
    {
        _api.Failure = new HttpRequestException("No route to host");

        var read = () => _reader.ReadAsync("player", TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<ServiceUnavailableException>().Where(exception => exception.Code == "drive.unavailable");
    }

    /// <summary>Without a configured Shared Drive nothing can be checked, which is also "unavailable".</summary>
    [Fact]
    public async Task Missing_configuration_is_an_error()
    {
        var reader = new GoogleDriveFolderReader(_api, Options.Create(new GoogleDriveOptions()));

        var read = () => reader.ReadAsync("player", TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<ServiceUnavailableException>();
    }

    /// <summary>A stand-in for the Drive API: an in-memory tree with paging.</summary>
    private sealed class FakeDriveApi : IDriveApi
    {
        /// <summary>The items by id.</summary>
        private readonly Dictionary<string, DriveItem> _items = [];

        /// <summary>The children of each folder, in insertion order.</summary>
        private readonly Dictionary<string, List<string>> _children = [];

        /// <summary>Gets or sets how many children one page holds.</summary>
        public int PageSize { get; set; } = 1000;

        /// <summary>Gets or sets an exception every call throws, to simulate an outage.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Gets how many pages were listed.</summary>
        public int ListCalls { get; private set; }

        /// <summary>Adds an item under a parent.</summary>
        /// <param name="item">The item.</param>
        /// <param name="parent">The parent folder id, or <see langword="null"/> for a top-level item.</param>
        public void Add(DriveItem item, string? parent)
        {
            _items[item.Id] = item;
            if (parent is not null)
            {
                (_children.TryGetValue(parent, out var list) ? list : _children[parent] = []).Add(item.Id);
            }
        }

        /// <inheritdoc/>
        public Task<DriveItem?> GetAsync(string id, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(_items.GetValueOrDefault(id));
        }

        /// <inheritdoc/>
        public Task<DrivePage> ListChildrenAsync(string folderId, string? pageToken, CancellationToken cancellationToken)
        {
            ListCalls++;
            var all = _children.GetValueOrDefault(folderId) ?? [];
            var start = pageToken is null ? 0 : int.Parse(pageToken, System.Globalization.CultureInfo.InvariantCulture);
            var page = all.Skip(start).Take(PageSize).Select(id => _items[id]).ToList();
            var next = start + PageSize < all.Count ? (start + PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return Task.FromResult(new DrivePage(page, next));
        }
    }
}
