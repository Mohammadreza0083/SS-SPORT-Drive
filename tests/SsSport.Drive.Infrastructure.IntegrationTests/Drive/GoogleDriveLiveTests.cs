using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Infrastructure.Drive;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Drive;

/// <summary>
/// Read-only check against the real Shared Drive with the real service account (owner-approved, 2026-10-29).
/// Skipped unless <c>SSD_LIVE_DRIVE_KEY_PATH</c> and <c>SSD_LIVE_SHARED_DRIVE_ID</c> are set, so CI never runs it.
/// <c>SSD_LIVE_DRIVE_PROXY</c> routes it through a VPN proxy where Google is blocked. Nothing is written, and only
/// counts are reported, never file or folder names.
/// </summary>
public sealed class GoogleDriveLiveTests
{
    /// <summary>Reads the first player folder of the Shared Drive: it is found, inside the Shared Drive, and readable.</summary>
    [Fact]
    public async Task Reads_a_real_player_folder()
    {
        var keyPath = Environment.GetEnvironmentVariable("SSD_LIVE_DRIVE_KEY_PATH");
        var sharedDriveId = Environment.GetEnvironmentVariable("SSD_LIVE_SHARED_DRIVE_ID");
        if (string.IsNullOrEmpty(keyPath) || string.IsNullOrEmpty(sharedDriveId))
        {
            Assert.Skip("Set SSD_LIVE_DRIVE_KEY_PATH and SSD_LIVE_SHARED_DRIVE_ID to run the live Drive check.");
        }

        var options = Options.Create(new GoogleDriveOptions
        {
            ServiceAccountKeyPath = keyPath,
            SharedDriveId = sharedDriveId,
            Proxy = Environment.GetEnvironmentVariable("SSD_LIVE_DRIVE_PROXY") ?? string.Empty,
        });
        using var api = new GoogleDriveApi(options);
        var reader = new GoogleDriveFolderReader(api, options);
        var cancellationToken = TestContext.Current.CancellationToken;

        // The Shared Drive's root folder has the drive's id; its category folders hold the player folders.
        var categories = await api.ListChildrenAsync(sharedDriveId, null, cancellationToken);
        var category = categories.Items.First(item => item.IsFolder);
        var players = await api.ListChildrenAsync(category.Id, null, cancellationToken);
        var player = players.Items.First(item => item.IsFolder);

        var summary = await reader.ReadAsync(player.Id, cancellationToken);

        summary.Status.Should().Be(DriveFolderStatus.Found);
        summary.Name.Should().NotBeNullOrWhiteSpace();
        TestContext.Current.SendDiagnosticMessage(
            $"Live Drive folder: {summary.VideoCount} videos, {summary.DocumentCount} documents, {summary.ScoutCount} scout files, {summary.OtherCount} other, {summary.TotalBytes} bytes, truncated={summary.IsTruncated}");

        // I-1: the ingest listing agrees with the counts, and Drive gives an md5 for every binary file.
        var listing = await reader.ListAsync(player.Id, cancellationToken);

        listing.Status.Should().Be(DriveFolderStatus.Found);
        listing.Files.Count.Should().BeLessThanOrEqualTo(summary.VideoCount + summary.DocumentCount + summary.ScoutCount);
        (listing.Files.Count + listing.SkippedCount).Should().Be(summary.VideoCount + summary.DocumentCount + summary.ScoutCount + summary.OtherCount);
        listing.Files.Should().OnlyContain(file => file.Md5 != null && file.Md5.Length == 32 && file.SizeBytes > 0);
        TestContext.Current.SendDiagnosticMessage(
            $"Live Drive listing: {listing.Files.Count} files, {listing.SkippedCount} skipped, deepest subfolder level {listing.Files.Select(file => file.Subfolders.Count).DefaultIfEmpty().Max()}");
    }
}
