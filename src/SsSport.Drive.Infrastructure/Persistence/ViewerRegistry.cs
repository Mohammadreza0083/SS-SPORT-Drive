using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Viewers;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>Registers viewers with <c>INSERT … ON CONFLICT DO NOTHING</c>, so two requests for one address never clash.</summary>
/// <param name="context">The database.</param>
public sealed class ViewerRegistry(DbContext context) : IViewerRegistry
{
    /// <inheritdoc />
    public async Task EnsureAsync(IEnumerable<EmailAddress> emails, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emails);
        foreach (var email in emails.DistinctBy(address => address.Value))
        {
            var viewer = Viewer.Register(email, now);
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO viewers (id, email, first_seen_at) VALUES ({viewer.Id}, {email.Value}, {now}) ON CONFLICT (email) DO NOTHING",
                cancellationToken);
        }
    }
}
