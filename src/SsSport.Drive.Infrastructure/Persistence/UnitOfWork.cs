using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>: one <c>SaveChanges</c> on the request's context,
/// which EF Core runs in a single transaction. Domain event dispatch is added with the MediatR pipeline (backlog D-3).
/// </summary>
/// <param name="context">The request's EF Core context, shared with the repositories.</param>
public sealed class UnitOfWork(DbContext context) : IUnitOfWork
{
    /// <summary>Writes all staged changes to the database.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The number of rows written.</returns>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
