namespace SsSport.Drive.Application.Paging;

/// <summary>
/// Which page of an offset-paged list to read (decisions §2b): admin tables and search, where the total and page numbers are shown.
/// </summary>
/// <param name="Page">The page number, starting at 1.</param>
/// <param name="PageSize">Rows per page, 1 to <see cref="MaxPageSize"/>.</param>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize)
{
    /// <summary>Rows per page when the client does not ask for a size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The most rows one page may hold.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Gets how many rows come before this page.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>
/// Where to continue a cursor-paged list (decisions §2b): large, append-only feeds such as the audit log, tracking events and infinite scroll.
/// </summary>
/// <param name="Cursor">The <c>nextCursor</c> of the previous page, or <see langword="null"/> for the first page.</param>
/// <param name="PageSize">Rows per page, 1 to <see cref="PageRequest.MaxPageSize"/>.</param>
public sealed record CursorRequest(string? Cursor = null, int PageSize = PageRequest.DefaultPageSize);

/// <summary>The order a cursor-paged list is read in.</summary>
public enum CursorDirection
{
    /// <summary>Largest key first, e.g. newest first.</summary>
    Descending,

    /// <summary>Smallest key first.</summary>
    Ascending,
}
