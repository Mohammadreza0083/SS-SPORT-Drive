using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.Links;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Public;

namespace SsSport.Drive.Api.Public;

/// <summary>
/// The public, signed-out side of the site (P-1, P-2, P-8): searching published players and the locked player page.
/// Nothing here needs a credential, and nothing here can reach a stored file: videos and documents are a title, tag and
/// length (or page count) only.
/// </summary>
public static class PublicPlayerEndpoints
{
    /// <summary>The Scalar tag of these endpoints.</summary>
    public const string Tag = "Public players";

    /// <summary>The route prefix of everything public.</summary>
    public const string RoutePrefix = "/public";

    private static readonly PublicPlayerCardDto[] _exampleCards =
    [
        new()
        {
            Slug = "arash-tehrani",
            Name = "Arash Tehrani",
            Position = new PositionDto { Code = "OPP", Label = "Opposite" },
            Nationality = new CountryDto { Code = "IR", Name = "Iran" },
            Videos = 6,
            Documents = 2,
            HasPhoto = true,
        },
    ];

    private static readonly LockedVideoDto[] _exampleVideos =
    [
        new() { Title = "Highlights 2026", Tag = "Highlight", DurationSeconds = 84 },
        new() { Title = "Full match vs Paykan", Tag = "Full match", DurationSeconds = 6490 },
    ];

    private static readonly LockedDocumentDto[] _exampleDocuments = [new() { Title = "Season stats 2026", PageCount = 2 }];

    private const string ExamplePlayer =
        """
        {
          "slug": "arash-tehrani",
          "name": "Arash Tehrani",
          "firstName": "Arash",
          "position": { "code": "OPP", "label": "Opposite" },
          "nationality": { "code": "IR", "name": "Iran" },
          "volleyboxUrl": "https://volleybox.net/arash-tehrani-p80001",
          "hasPhoto": true,
          "stats": { "videos": 6, "minutes": 117, "documents": 2 }
        }
        """;

    /// <summary>Maps the public player endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPublicPlayers(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{RoutePrefix}/players", SearchAsync)
            .WithTags(Tag)
            .WithName("SearchPublicPlayers")
            .WithSummary("Search published players")
            .WithDescription(
                "Public, no credentials. One page of published players, sorted by name, for the home page. `search` matches any part of the name, " +
                "ignoring case (`%` and `_` match themselves); `position` filters on the **primary** position (S, OH, OPP, MB, L, DS; any case). " +
                "Each card has only public facts: slug, name, primary position, nationality, how many videos and documents exist, and whether a " +
                "photo exists at `/public/players/{slug}/photo`. Players still on the waiting list are never listed. 400 for a bad page, a search over " +
                "100 characters or an unknown position.")
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithPagedResponse(_exampleCards)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null));

        app.MapGet($"{RoutePrefix}/players/{{slug}}", GetPlayerAsync)
            .WithTags(Tag)
            .WithName("GetPublicPlayer")
            .WithSummary("Read a player for the locked page")
            .WithDescription(
                "Public, no credentials. The locked player page header: name, first name, primary position, nationality, Volleybox profile, whether a " +
                "photo exists and the counts of footage (videos, minutes, documents). 404 for an unknown slug and for a player still on the waiting list.")
            .Produces<PublicPlayerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", ExamplePlayer, null));

        app.MapGet($"{RoutePrefix}/players/{{slug}}/videos", ListVideosAsync)
            .WithTags(Tag)
            .WithName("ListPublicPlayerVideos")
            .WithSummary("List a player's locked videos")
            .WithDescription(
                "Public, no credentials. One page of the player's ready and visible videos, the hero first, then the page order. Each is its title, " +
                "tag and length in seconds, and nothing else: no id, stream address or thumbnail, so the page can show them locked. 400 for a bad page; " +
                "404 as for the player.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithPagedResponse(_exampleVideos)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null));

        app.MapGet($"{RoutePrefix}/players/{{slug}}/documents", ListDocumentsAsync)
            .WithTags(Tag)
            .WithName("ListPublicPlayerDocuments")
            .WithSummary("List a player's locked documents")
            .WithDescription(
                "Public, no credentials. One page of the player's ready and visible documents in page order. Each is its title and page count only: " +
                "no id or file address. 400 for a bad page; 404 as for the player.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithPagedResponse(_exampleDocuments)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null));

        app.MapGet($"{RoutePrefix}/players/{{slug}}/photo", GetPhotoAsync)
            .WithTags(Tag)
            .WithName("GetPublicPlayerPhoto")
            .WithSummary("Read a player's photo")
            .WithDescription(
                "Public, no credentials. Streams the cut-out photo (WebP) with `Cache-Control: public, max-age=3600`; it is not footage. Storage itself " +
                "is never exposed. 404 for an unknown or unpublished player and for a player without a photo.")
            .Produces(StatusCodes.Status200OK, typeof(byte[]), "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null));

        return app;
    }

    /// <summary>Searches published players.</summary>
    /// <param name="search">Part of the name.</param>
    /// <param name="position">A primary position code.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<PublicPlayerCardDto>>> SearchAsync(
        [FromQuery] string? search,
        [FromQuery] string? position,
        [AsParameters] PageParameters paging,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetPublicPlayersQuery(search, position, paging.ToRequest()), cancellationToken));

    /// <summary>Reads one player.</summary>
    /// <param name="slug">The slug.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the header.</returns>
    private static async Task<Ok<PublicPlayerDto>> GetPlayerAsync(string slug, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetPublicPlayerQuery(slug), cancellationToken));

    /// <summary>Lists a player's locked videos.</summary>
    /// <param name="slug">The slug.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<LockedVideoDto>>> ListVideosAsync(string slug, [AsParameters] PageParameters paging, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetPublicPlayerVideosQuery(slug, paging.ToRequest()), cancellationToken));

    /// <summary>Lists a player's locked documents.</summary>
    /// <param name="slug">The slug.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<LockedDocumentDto>>> ListDocumentsAsync(string slug, [AsParameters] PageParameters paging, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetPublicPlayerDocumentsQuery(slug, paging.ToRequest()), cancellationToken));

    /// <summary>Streams a player's photo.</summary>
    /// <param name="slug">The slug.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="response">The response, for the cache header.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the image.</returns>
    private static async Task<FileStreamHttpResult> GetPhotoAsync(string slug, ISender sender, HttpResponse response, CancellationToken cancellationToken)
    {
        var photo = await sender.Send(new GetPublicPlayerPhotoQuery(slug), cancellationToken);
        response.Headers.CacheControl = "public, max-age=3600";
        return TypedResults.Stream(photo.Content, photo.ContentType);
    }
}
