using System.Text.Json;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Api.Paging;

/// <summary>
/// Documents a list endpoint's responses in one line: the page shape with a realistic example, and invalid paging as a
/// 400 ValidationProblemDetails keyed by parameter name.
/// </summary>
public static class PagingEndpointExtensions
{
    /// <summary>JSON settings matching the API's responses (camelCase).</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Documents an offset-paged list response.</summary>
    /// <typeparam name="TItem">The item DTO.</typeparam>
    /// <param name="builder">The endpoint.</param>
    /// <param name="exampleItems">Items for the example page; empty when not given.</param>
    /// <returns>The same endpoint, for chaining.</returns>
    public static RouteHandlerBuilder WithPagedResponse<TItem>(this RouteHandlerBuilder builder, params IReadOnlyList<TItem> exampleItems)
    {
        var example = PagedResult<TItem>.Create(exampleItems, new PageRequest(2, PageRequest.DefaultPageSize), 45);
        return builder.Document<PagedResult<TItem>>(example);
    }

    /// <summary>Documents a cursor-paged list response.</summary>
    /// <typeparam name="TItem">The item DTO.</typeparam>
    /// <param name="builder">The endpoint.</param>
    /// <param name="exampleItems">Items for the example page; empty when not given.</param>
    /// <returns>The same endpoint, for chaining.</returns>
    public static RouteHandlerBuilder WithCursorResponse<TItem>(this RouteHandlerBuilder builder, params IReadOnlyList<TItem> exampleItems)
    {
        var example = new CursorResult<TItem>(exampleItems, CursorToken.Encode(new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero), Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10")));
        return builder.Document<CursorResult<TItem>>(example);
    }

    /// <summary>Adds the 200 and 400 responses and the example.</summary>
    /// <typeparam name="TResult">The page type.</typeparam>
    /// <param name="builder">The endpoint.</param>
    /// <param name="example">The example page.</param>
    /// <returns>The same endpoint, for chaining.</returns>
    private static RouteHandlerBuilder Document<TResult>(this RouteHandlerBuilder builder, TResult example) =>
        builder
            .Produces<TResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                if (operation.Responses?.TryGetValue("200", out var ok) == true
                    && ok.Content?.TryGetValue("application/json", out var media) == true)
                {
                    media.Example = JsonSerializer.SerializeToNode(example, _json);
                }

                return Task.CompletedTask;
            });
}
