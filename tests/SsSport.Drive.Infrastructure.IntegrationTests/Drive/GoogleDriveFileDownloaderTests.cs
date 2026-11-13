using System.Text;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Infrastructure.Drive;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Drive;

/// <summary>
/// I-2: downloading a file's bytes from Drive (<c>files.get?alt=media</c> in <see cref="GoogleDriveApi"/>). A file that
/// is gone is <see langword="null"/>, so the asset fails with a reason; an outage is <c>drive.unavailable</c>, so the job
/// is retried later.
/// </summary>
public sealed class GoogleDriveFileDownloaderTests
{
    /// <summary>The stand-in Drive.</summary>
    private readonly FakeDriveApi _api = new();

    /// <summary>A file's bytes come back as a stream.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_file_is_downloaded()
    {
        var bytes = Encoding.UTF8.GetBytes("video bytes");
        _api.Contents["file-1"] = bytes;

        await using var stream = await new GoogleDriveFileDownloader(_api).OpenAsync("file-1", TestContext.Current.CancellationToken);

        using var copy = new MemoryStream();
        await stream!.CopyToAsync(copy, TestContext.Current.CancellationToken);
        copy.ToArray().Should().Equal(bytes);
    }

    /// <summary>A file Drive no longer has is <see langword="null"/>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_missing_file_is_null() =>
        (await new GoogleDriveFileDownloader(_api).OpenAsync("gone", TestContext.Current.CancellationToken)).Should().BeNull();

    /// <summary>An outage is <c>drive.unavailable</c>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_outage_is_drive_unavailable()
    {
        _api.Failure = new HttpRequestException("Connection reset");

        var act = () => new GoogleDriveFileDownloader(_api).OpenAsync("file-1", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ServiceUnavailableException>().Where(exception => exception.Code == "drive.unavailable");
    }
}
