namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>
/// Commits every change staged through the repositories of the current request as one database transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Writes all staged changes to the database.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The number of rows written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
