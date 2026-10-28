namespace SsSport.Drive.Application.Paging;

/// <summary>One page of an offset-paged list, with the totals the client needs for page controls.</summary>
/// <typeparam name="T">The item type, always a DTO.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>Gets the items on this page, in the list's order.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Gets the page number, starting at 1.</summary>
    public required int Page { get; init; }

    /// <summary>Gets the requested rows per page.</summary>
    public required int PageSize { get; init; }

    /// <summary>Gets how many rows the whole list has.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Gets how many pages the whole list has (0 when it is empty).</summary>
    public required int TotalPages { get; init; }

    /// <summary>Builds a page and works out the page count.</summary>
    /// <param name="items">The items on the page.</param>
    /// <param name="request">The page that was asked for.</param>
    /// <param name="totalCount">How many rows the whole list has.</param>
    /// <returns>The page.</returns>
    public static PagedResult<T> Create(IReadOnlyList<T> items, PageRequest request, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(request);
        return new PagedResult<T>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize),
        };
    }
}

/// <summary>One page of a cursor-paged list.</summary>
/// <typeparam name="T">The item type, always a DTO.</typeparam>
/// <param name="Items">The items on this page, in the list's order.</param>
/// <param name="NextCursor">Pass this back as <c>cursor</c> for the next page; <see langword="null"/> on the last page.</param>
public sealed record CursorResult<T>(IReadOnlyList<T> Items, string? NextCursor);
