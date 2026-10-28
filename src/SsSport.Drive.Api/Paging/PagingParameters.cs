using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Api.Paging;

/// <summary>
/// The <c>page</c> and <c>pageSize</c> query parameters of an offset-paged list, bound with <c>[AsParameters]</c>.
/// The attributes document the defaults and limits in OpenAPI; the Application validators enforce them (400).
/// </summary>
/// <param name="Page">The page number, starting at 1.</param>
/// <param name="PageSize">Rows per page, 1 to 100.</param>
public sealed record PageParameters(
    [property: FromQuery(Name = "page")]
    [property: Range(1, int.MaxValue)]
    [property: DefaultValue(1)]
    [property: Description("The page number, starting at 1. A page after the last one is empty.")]
    int Page = 1,
    [property: FromQuery(Name = "pageSize")]
    [property: Range(1, PageRequest.MaxPageSize)]
    [property: DefaultValue(PageRequest.DefaultPageSize)]
    [property: Description("Rows per page, 1 to 100 (default 20).")]
    int PageSize = PageRequest.DefaultPageSize)
{
    /// <summary>Converts the parameters to the Application's page request.</summary>
    /// <returns>The page request.</returns>
    public PageRequest ToRequest() => new(Page, PageSize);
}

/// <summary>
/// The <c>cursor</c> and <c>pageSize</c> query parameters of a cursor-paged list, bound with <c>[AsParameters]</c>.
/// </summary>
/// <param name="Cursor">The <c>nextCursor</c> of the previous page; leave it out for the first page.</param>
/// <param name="PageSize">Rows per page, 1 to 100.</param>
public sealed record CursorParameters(
    [property: FromQuery(Name = "cursor")]
    [property: MaxLength(CursorToken.MaxLength)]
    [property: Description("The nextCursor of the previous page. Leave it out for the first page.")]
    string? Cursor = null,
    [property: FromQuery(Name = "pageSize")]
    [property: Range(1, PageRequest.MaxPageSize)]
    [property: DefaultValue(PageRequest.DefaultPageSize)]
    [property: Description("Rows per page, 1 to 100 (default 20).")]
    int PageSize = PageRequest.DefaultPageSize)
{
    /// <summary>Converts the parameters to the Application's cursor request.</summary>
    /// <returns>The cursor request.</returns>
    public CursorRequest ToRequest() => new(Cursor, PageSize);
}
