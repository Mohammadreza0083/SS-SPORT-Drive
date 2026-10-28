using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IPagedReader"/>. Offset pages use <c>OFFSET</c>/<c>LIMIT</c>; cursor pages use a
/// PostgreSQL row comparison (<c>(key, id) &lt; (@key, @id)</c>), which stays fast however deep the reader goes.
/// Every query is untracked and ends with the id, so the order is total.
/// </summary>
/// <param name="context">The request's EF Core context.</param>
public sealed class PagedReader(DbContext context) : IPagedReader
{
    /// <summary>Npgsql's <c>EF.Functions.LessThan(ITuple, ITuple)</c>, translated to a row comparison.</summary>
    private static readonly MethodInfo _rowLessThan = RowComparison(nameof(NpgsqlDbFunctionsExtensions.LessThan));

    /// <summary>Npgsql's <c>EF.Functions.GreaterThan(ITuple, ITuple)</c>, translated to a row comparison.</summary>
    private static readonly MethodInfo _rowGreaterThan = RowComparison(nameof(NpgsqlDbFunctionsExtensions.GreaterThan));

    /// <summary>Reads one offset page and the total count.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TItem">The item DTO.</typeparam>
    /// <param name="specification">Criteria, includes and the order; it must order and must not page.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="map">Maps an entity to its DTO.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidOperationException">The specification has no order, or pages itself.</exception>
    public async Task<PagedResult<TItem>> PageAsync<TEntity, TItem>(
        ISpecification<TEntity> specification,
        PageRequest request,
        Func<TEntity, TItem> map,
        CancellationToken cancellationToken = default)
        where TEntity : Entity<Guid>
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(map);
        RefuseOwnPaging(specification);
        if (!specification.OrderExpressions.Any())
        {
            throw new InvalidOperationException($"{specification.GetType().Name} has no order; an offset page needs one to be stable.");
        }

        var totalCount = await Evaluate(specification, criteriaOnly: true).CountAsync(cancellationToken);
        var ordered = (IOrderedQueryable<TEntity>)Evaluate(specification);
        var entities = await ordered.ThenBy(entity => entity.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return PagedResult<TItem>.Create([.. entities.Select(map)], request, totalCount);
    }

    /// <summary>Reads one cursor page, ordered by a key and then the id. One extra row is read to know whether more follow.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <typeparam name="TItem">The item DTO.</typeparam>
    /// <param name="specification">Criteria and includes; it must neither order nor page.</param>
    /// <param name="key">The sort key.</param>
    /// <param name="direction">The order to read in.</param>
    /// <param name="request">Where to continue from.</param>
    /// <param name="map">Maps an entity to its DTO.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page and the cursor for the next one.</returns>
    /// <exception cref="InvalidOperationException">The specification orders or pages itself.</exception>
    /// <exception cref="RequestValidationException">The cursor was made for a list with another key type.</exception>
    public async Task<CursorResult<TItem>> CursorPageAsync<TEntity, TKey, TItem>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TKey>> key,
        CursorDirection direction,
        CursorRequest request,
        Func<TEntity, TItem> map,
        CancellationToken cancellationToken = default)
        where TEntity : Entity<Guid>
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(map);
        RefuseOwnPaging(specification);
        if (specification.OrderExpressions.Any())
        {
            throw new InvalidOperationException($"{specification.GetType().Name} orders itself; a cursor list is ordered by its key.");
        }

        var query = Evaluate(specification);
        if (request.Cursor is not null)
        {
            if (!CursorToken.TryDecode<TKey>(request.Cursor, out var lastKey, out var lastId))
            {
                throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["cursor"] = ["The cursor does not belong to this list. Leave it out to start from the first page."],
                });
            }

            query = query.Where(After(key, direction, new CursorBoundary<TKey>(lastKey, lastId)));
        }

        var ordered = direction == CursorDirection.Descending
            ? query.OrderByDescending(key).ThenByDescending(entity => entity.Id)
            : query.OrderBy(key).ThenBy(entity => entity.Id);

        var rows = await ordered.Take(request.PageSize + 1).AsNoTracking().ToListAsync(cancellationToken);
        if (rows.Count <= request.PageSize)
        {
            return new CursorResult<TItem>([.. rows.Select(map)], null);
        }

        rows.RemoveAt(rows.Count - 1);
        var last = rows[^1];
        var nextCursor = CursorToken.Encode(key.Compile()(last), last.Id);
        return new CursorResult<TItem>([.. rows.Select(map)], nextCursor);
    }

    /// <summary>Builds <c>(key, id) &lt; (last key, last id)</c> (or <c>&gt;</c> when ascending) as a PostgreSQL row comparison.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="key">The sort key.</param>
    /// <param name="direction">The order being read.</param>
    /// <param name="boundary">The last row seen; read through a closure so EF Core sends it as parameters.</param>
    /// <returns>The filter.</returns>
    private static Expression<Func<TEntity, bool>> After<TEntity, TKey>(
        Expression<Func<TEntity, TKey>> key,
        CursorDirection direction,
        CursorBoundary<TKey> boundary)
        where TEntity : Entity<Guid>
    {
        var entity = key.Parameters[0];
        var tuple = typeof(ValueTuple<TKey, Guid>);
        var constructor = tuple.GetConstructor([typeof(TKey), typeof(Guid)])!;
        var holder = Expression.Constant(boundary);

        var row = Expression.New(constructor, key.Body, Expression.Property(entity, nameof(Entity<Guid>.Id)));
        var last = Expression.New(
            constructor,
            Expression.Property(holder, nameof(CursorBoundary<TKey>.Key)),
            Expression.Property(holder, nameof(CursorBoundary<TKey>.Id)));

        var comparison = Expression.Call(
            direction == CursorDirection.Descending ? _rowLessThan : _rowGreaterThan,
            Expression.Constant(EF.Functions),
            Expression.Convert(row, typeof(ITuple)),
            Expression.Convert(last, typeof(ITuple)));

        return Expression.Lambda<Func<TEntity, bool>>(comparison, entity);
    }

    /// <summary>Paging belongs to the request, so a specification that pages itself is a programming error.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="specification">The specification.</param>
    /// <exception cref="InvalidOperationException">The specification sets Skip or Take.</exception>
    private static void RefuseOwnPaging<TEntity>(ISpecification<TEntity> specification)
    {
        if (specification.Skip is > 0 || specification.Take is > 0)
        {
            throw new InvalidOperationException($"{specification.GetType().Name} pages itself; paging comes from the request.");
        }
    }

    /// <summary>Finds an Npgsql row-comparison function.</summary>
    /// <param name="name"><c>LessThan</c> or <c>GreaterThan</c>.</param>
    /// <returns>The method.</returns>
    private static MethodInfo RowComparison(string name) =>
        typeof(NpgsqlDbFunctionsExtensions).GetMethod(name, [typeof(DbFunctions), typeof(ITuple), typeof(ITuple)])
        ?? throw new InvalidOperationException($"Npgsql no longer offers EF.Functions.{name}(ITuple, ITuple).");

    /// <summary>Builds the query for a specification.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="specification">The specification.</param>
    /// <param name="criteriaOnly">When <see langword="true"/>, only the criteria are applied.</param>
    /// <returns>The query, not yet executed.</returns>
    private IQueryable<TEntity> Evaluate<TEntity>(ISpecification<TEntity> specification, bool criteriaOnly = false)
        where TEntity : class =>
        SpecificationEvaluator.Default.GetQuery(context.Set<TEntity>(), specification, criteriaOnly);

    /// <summary>The last row of the previous page.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="Key">Its sort key.</param>
    /// <param name="Id">Its id.</param>
    private sealed record CursorBoundary<TKey>(TKey Key, Guid Id);
}
