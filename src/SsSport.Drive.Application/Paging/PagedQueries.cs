using SsSport.Drive.Application.Abstractions.Messaging;

namespace SsSport.Drive.Application.Paging;

/// <summary>A request that reads one offset page; its paging is validated by <see cref="PagedRequestValidator{TRequest}"/>.</summary>
public interface IPagedRequest
{
    /// <summary>Gets the page to read.</summary>
    PageRequest Paging { get; }
}

/// <summary>A request that reads one cursor page; its paging is validated by <see cref="CursorPagedRequestValidator{TRequest}"/>.</summary>
public interface ICursorPagedRequest
{
    /// <summary>Gets where to continue from.</summary>
    CursorRequest Paging { get; }
}

/// <summary>A query that answers with one offset page of a list.</summary>
/// <typeparam name="TItem">The item DTO.</typeparam>
public interface IPagedQuery<TItem> : IQuery<PagedResult<TItem>>, IPagedRequest;

/// <summary>A query that answers with one cursor page of a list.</summary>
/// <typeparam name="TItem">The item DTO.</typeparam>
public interface ICursorQuery<TItem> : IQuery<CursorResult<TItem>>, ICursorPagedRequest;

/// <summary>
/// Allows a query to return a whole collection (decisions §2b). Only for small fixed catalogues such as countries,
/// positions or roles; the architecture tests fail for any unmarked query that returns a collection.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UnpagedListAttribute : Attribute
{
    /// <summary>Marks the query as exempt.</summary>
    /// <param name="reason">Why the whole list is safe to send, e.g. "A fixed catalogue of 245 countries."</param>
    /// <exception cref="ArgumentException">No reason is given.</exception>
    public UnpagedListAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Gets why the whole list is safe to send.</summary>
    public string Reason { get; }
}
