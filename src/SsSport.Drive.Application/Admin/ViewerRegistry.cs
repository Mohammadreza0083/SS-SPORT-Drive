using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Admin;

/// <summary>
/// Makes sure every email that can watch has a viewer row (A-7, A-11), so the console can address it by id. Inserting is
/// idempotent: an address that is already registered is left alone, so concurrent requests never clash.
/// </summary>
public interface IViewerRegistry
{
    /// <summary>Registers the addresses that are not registered yet.</summary>
    /// <param name="emails">The addresses.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when every address has a row.</returns>
    Task EnsureAsync(IEnumerable<EmailAddress> emails, DateTimeOffset now, CancellationToken cancellationToken = default);
}
