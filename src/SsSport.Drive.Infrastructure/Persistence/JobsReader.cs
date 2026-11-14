using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Emails;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>Counts the jobs page's tiles (A-9) with aggregate queries.</summary>
/// <param name="context">The database.</param>
public sealed class JobsReader(DbContext context) : IJobsReader
{
    /// <inheritdoc />
    public async Task<JobsSummaryDto> SummaryAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var assets = context.Set<Asset>().AsNoTracking();
        var folders = context.Set<DriveFolder>().AsNoTracking();
        var emails = context.Set<OutgoingEmail>().AsNoTracking();
        var since = now.AddHours(-24);
        return new JobsSummaryDto
        {
            Queued = await assets.CountAsync(asset => asset.State == ProcessingState.Queued, cancellationToken),
            Copying = await assets.CountAsync(asset => asset.State == ProcessingState.Copying, cancellationToken),
            Processing = await assets.CountAsync(asset => asset.State == ProcessingState.Processing, cancellationToken),
            Failed = await assets.CountAsync(asset => asset.State == ProcessingState.Failed, cancellationToken),
            Folders = await folders.CountAsync(cancellationToken),
            LostFolders = await folders.CountAsync(folder => folder.Access == FolderAccess.Lost, cancellationToken),
            LastDriveSyncAt = await folders.MaxAsync(folder => folder.LastSyncedAt, cancellationToken),
            EmailsSentLast24Hours = await emails.CountAsync(email => email.Status == EmailStatus.Sent && email.SentAt >= since, cancellationToken),
            EmailsFailed = await emails.CountAsync(email => email.Status == EmailStatus.Failed, cancellationToken),
            EmailsQueued = await emails.CountAsync(email => email.Status == EmailStatus.Queued, cancellationToken),
        };
    }
}
