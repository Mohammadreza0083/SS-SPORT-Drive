using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Infrastructure.Drive;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Drive;

/// <summary>
/// M-1: the archive scan browses the Shared Drive from its root: the root's folders (<c>Men Volleyball</c>,
/// <c>Women Volleyball</c>…) and then each category folder's player folders, every page, with the loose files counted.
/// </summary>
public sealed class GoogleDriveArchiveBrowserTests
{
    /// <summary>The agency's Shared Drive; its id is also the id of its root folder.</summary>
    private const string SharedDrive = "0AAgencyDrive";

    /// <summary>The stand-in Drive API.</summary>
    private readonly FakeDriveApi _api = new();

    /// <summary>The browser under test.</summary>
    private readonly GoogleDriveFolderReader _reader;

    /// <summary>Initializes the reader over the stand-in API.</summary>
    public GoogleDriveArchiveBrowserTests() =>
        _reader = new GoogleDriveFolderReader(_api, Options.Create(new GoogleDriveOptions { SharedDriveId = SharedDrive }));

    /// <summary>The root lists its folders and counts its loose files (root junk such as <c>InShot_….mp4</c>).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_root_lists_its_folders_and_counts_loose_files()
    {
        _api.Add(Folder("men", "Men Volleyball"), SharedDrive);
        _api.Add(Folder("women", "Women Volleyball"), SharedDrive);
        _api.Add(new DriveItem("junk", "InShot_2025.mp4", "video/mp4", 100, SharedDrive), SharedDrive);

        var root = await _reader.ListRootAsync(TestContext.Current.CancellationToken);

        root.Folders.Should().BeEquivalentTo([new DriveSubfolder("men", "Men Volleyball"), new DriveSubfolder("women", "Women Volleyball")]);
        root.FileCount.Should().Be(1);
    }

    /// <summary>A category folder lists every player folder across pages.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_folder_lists_every_page()
    {
        _api.PageSize = 2;
        for (var i = 0; i < 5; i++)
        {
            _api.Add(Folder($"p{i}", $"SS-SPORT Player {i}"), "men");
        }

        _api.Add(new DriveItem("marker", "marker", "application/octet-stream", 0, SharedDrive), "men");

        var men = await _reader.ListFolderAsync("men", TestContext.Current.CancellationToken);

        men.Folders.Select(folder => folder.Id).Should().Equal("p0", "p1", "p2", "p3", "p4");
        men.FileCount.Should().Be(1);
    }

    /// <summary>A trashed folder is left out.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Trashed_folders_are_left_out()
    {
        _api.Add(Folder("p1", "SS-SPORT Kept"), "men");
        _api.Add(Folder("p2", "SS-SPORT Old") with { Trashed = true }, "men");

        var men = await _reader.ListFolderAsync("men", TestContext.Current.CancellationToken);

        men.Folders.Should().ContainSingle().Which.Id.Should().Be("p1");
    }

    /// <summary>A Drive outage is <c>drive.unavailable</c>, never a partial list.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_outage_is_drive_unavailable()
    {
        _api.Failure = new HttpRequestException("down");

        var list = () => _reader.ListRootAsync(TestContext.Current.CancellationToken);

        (await list.Should().ThrowAsync<ServiceUnavailableException>()).Which.Code.Should().Be("drive.unavailable");
    }

    /// <summary>Without a Shared Drive id there is nothing to browse.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Without_configuration_it_is_drive_unavailable()
    {
        var reader = new GoogleDriveFolderReader(_api, Options.Create(new GoogleDriveOptions()));

        var list = () => reader.ListRootAsync(TestContext.Current.CancellationToken);

        (await list.Should().ThrowAsync<ServiceUnavailableException>()).Which.Code.Should().Be("drive.unavailable");
    }

    /// <summary>Builds a folder in the Shared Drive.</summary>
    /// <param name="id">The id.</param>
    /// <param name="name">The name.</param>
    /// <returns>The folder.</returns>
    private static DriveItem Folder(string id, string name) => new(id, name, DriveItem.FolderMimeType, null, SharedDrive);
}
