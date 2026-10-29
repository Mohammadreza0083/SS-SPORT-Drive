using System.Collections.Concurrent;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// Stands in for Google Drive in API tests. Every folder id is a readable folder in the Shared Drive named
/// <c>SS-SPORT {id}</c>, unless a test says otherwise.
/// </summary>
public sealed class FakeDriveFolderReader : IDriveFolderReader
{
    /// <summary>Folders that answer something other than "found".</summary>
    private readonly ConcurrentDictionary<string, DriveFolderStatus> _statuses = new(StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether Drive is unreachable.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Makes a folder answer with a status.</summary>
    /// <param name="folderId">The Drive folder id.</param>
    /// <param name="status">The status.</param>
    public void Set(string folderId, DriveFolderStatus status) => _statuses[folderId] = status;

    /// <inheritdoc/>
    public Task<DriveFolderSummary> ReadAsync(string folderId, CancellationToken cancellationToken = default)
    {
        if (Unavailable)
        {
            throw new ServiceUnavailableException("drive.unavailable", "Google Drive cannot be reached right now.");
        }

        var status = _statuses.GetValueOrDefault(folderId, DriveFolderStatus.Found);
        return Task.FromResult(status == DriveFolderStatus.Found
            ? new DriveFolderSummary(status, $"SS-SPORT {folderId}", VideoCount: 12, DocumentCount: 3, ScoutCount: 1, OtherCount: 0, TotalBytes: 4_500_000_000, IsTruncated: false)
            : DriveFolderSummary.Missing(status));
    }
}
