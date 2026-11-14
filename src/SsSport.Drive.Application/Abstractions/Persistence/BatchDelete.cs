using Ardalis.Specification;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Persistence;

/// <summary>Deletes expired rows in capped SQL batches; shared by the gate clean-up (L-2b) and the retention clean-up (W-3).</summary>
internal static class BatchDelete
{
    /// <summary>Deletes batch after batch until one comes back short, or the run's cap is reached.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="rows">The repository.</param>
    /// <param name="batch">One batch of rows to delete.</param>
    /// <param name="cancellationToken">Cancels the deletes.</param>
    /// <returns>How many rows went.</returns>
    public static async Task<int> InBatchesAsync<T>(IRepository<T, Guid> rows, ISpecification<T> batch, CancellationToken cancellationToken)
        where T : AggregateRoot<Guid>
    {
        var total = 0;
        for (var round = 0; round < GateRetention.MaxBatches; round++)
        {
            var deleted = await rows.DeleteAsync(batch, cancellationToken);
            total += deleted;
            if (deleted < GateRetention.BatchSize)
            {
                break;
            }
        }

        return total;
    }
}
