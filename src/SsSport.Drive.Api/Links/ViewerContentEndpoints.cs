using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Api.Links;

/// <summary>
/// What a signed-in viewer reads behind <c>/r/{code}</c> (V-2b): the link overview, its players, one player's header,
/// their videos and documents, and their photo. All are public routes whose proof is the viewer session token in
/// <c>X-Viewer-Session</c>; the web app sends it on the browser's behalf. The checks run in a fixed order: a link that
/// does not open is 404 <c>share_link.unavailable</c>; a missing, unknown or ended session, or one for another link,
/// is 401 <c>viewer.verification_required</c>; a player who is not on the link is 404. No storage key, checksum or
/// Drive id is ever sent.
/// </summary>
public static class ViewerContentEndpoints
{
    /// <summary>The OpenAPI tag.</summary>
    public const string Tag = "Viewer content";

    /// <summary>A realistic link overview.</summary>
    private const string ExampleContent =
        """
        {
          "type": "Personal",
          "playerCount": 1,
          "recipient": { "name": "Marco Rossi", "organisation": "Lube Volley" },
          "note": "Hi Marco, as discussed: Arash's 2026 season. Full match vs Paykan included.",
          "viewerEmail": "marco@lube.it"
        }
        """;

    /// <summary>A realistic player header.</summary>
    private const string ExamplePlayer =
        """
        {
          "id": "0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10",
          "name": "Arash Tehrani",
          "firstName": "Arash",
          "position": { "code": "OPP", "label": "Opposite" },
          "nationality": { "code": "IR", "name": "Iran" },
          "volleyboxUrl": "https://volleybox.net/arash-tehrani-p80001",
          "hasPhoto": true,
          "stats": { "videos": 6, "minutes": 117, "documents": 2 }
        }
        """;

    /// <summary>Example roster cards.</summary>
    private static readonly LinkPlayerCardDto[] _exampleCards =
    [
        new()
        {
            Id = Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10"),
            Name = "Arash Tehrani",
            FirstName = "Arash",
            Position = new PositionDto { Code = "OPP", Label = "Opposite" },
            Nationality = new CountryDto { Code = "IR", Name = "Iran" },
            HasPhoto = true,
            VideoCount = 6,
        },
    ];

    /// <summary>Example videos.</summary>
    private static readonly LinkVideoDto[] _exampleVideos =
    [
        new() { Id = Guid.Parse("0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f60"), Title = "Full match vs Paykan", Tag = "Full match", DurationSeconds = 6490 },
        new() { Id = Guid.Parse("0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f61"), Title = "Highlights 2026", Tag = "Highlight", DurationSeconds = 84 },
    ];

    /// <summary>Example documents.</summary>
    private static readonly LinkDocumentDto[] _exampleDocuments =
    [
        new() { Id = Guid.Parse("0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f70"), Title = "Season stats 2026", PageCount = 2 },
    ];

    /// <summary>Maps the viewer content endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapViewerContent(this IEndpointRouteBuilder app)
    {
        app.MapGet("/links/{code}/content", GetContentAsync)
            .WithTags(Tag)
            .WithName("GetLinkContent")
            .WithSummary("Read the link overview")
            .WithDescription(
                "For a signed-in viewer: the link type (`Personal` or `Roster`), how many players it shows, who it was prepared for (name and " +
                "organisation, never their emails), the admin's note, and the viewer's own email for the overlay. " +
                "404 `share_link.unavailable` when the link does not open; 401 `viewer.verification_required` without a live session for this link.")
            .Produces<LinkContentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", ExampleContent, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players", ListPlayersAsync)
            .WithTags(Tag)
            .WithName("ListLinkPlayers")
            .WithSummary("List the link's players")
            .WithDescription(
                "One page of the link's players in the link's page order, as Roster cards: name, primary position, nationality, whether a photo " +
                "exists, and how many videos can be watched. A link holds at most 20 players. 400 for a bad page; 401 and 404 as for the overview.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithPagedResponse(_exampleCards)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players/{playerId:guid}", GetPlayerAsync)
            .WithTags(Tag)
            .WithName("GetLinkPlayer")
            .WithSummary("Read a player's page header")
            .WithDescription(
                "The player page header for a signed-in viewer: name, first name, primary position with its label, nationality, Volleybox profile, " +
                "whether a photo exists, and the stats row (ready and visible videos, their minutes, ready and visible documents). " +
                "404 `not_found` for a player who is not on this link; 401 and 404 as for the overview.")
            .Produces<LinkPlayerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", ExamplePlayer, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players/{playerId:guid}/videos", ListVideosAsync)
            .WithTags(Tag)
            .WithName("ListLinkPlayerVideos")
            .WithSummary("List a player's videos")
            .WithDescription(
                "One page of the player's ready and visible videos: the hero first, then the page order. Each has its title, the tag the chips show " +
                "(Highlight, Spike, Serve, Block, Reception, Defence, Full match, Stats, or Video when untagged) and its length. The stream URL is " +
                "signed per viewer by the edge (V-5b) and is not part of this answer. 400 for a bad page; 401 and 404 as for the player.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithPagedResponse(_exampleVideos)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players/{playerId:guid}/documents", ListDocumentsAsync)
            .WithTags(Tag)
            .WithName("ListLinkPlayerDocuments")
            .WithSummary("List a player's documents")
            .WithDescription(
                "One page of the player's ready and visible documents (PDFs and images) in page order, with their page count. The file itself is " +
                "never offered; page images come from ingest (I-4) through the edge (V-5b). DataVolley files are left out until V-8. " +
                "400 for a bad page; 401 and 404 as for the player.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithPagedResponse(_exampleDocuments)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players/{playerId:guid}/photo", GetPhotoAsync)
            .WithTags(Tag)
            .WithName("GetLinkPlayerPhoto")
            .WithSummary("Read a player's photo")
            .WithDescription(
                "Streams the player's photo (WebP) with `Cache-Control: private`; storage itself is never exposed. 404 when the player has no photo " +
                "or is not on this link; 401 and 404 as for the overview.")
            .Produces(StatusCodes.Status200OK, typeof(byte[]), "image/webp")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null, ViewerGateEndpoints.SessionSchemeName));

        return app;
    }

    /// <summary>Reads the link overview.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the overview.</returns>
    private static async Task<Ok<LinkContentDto>> GetContentAsync(
        string code,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetLinkContentQuery(code, sessionToken), cancellationToken));

    /// <summary>Lists the link's players.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<LinkPlayerCardDto>>> ListPlayersAsync(
        string code,
        [AsParameters] PageParameters paging,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetLinkPlayersQuery(code, sessionToken, paging.ToRequest()), cancellationToken));

    /// <summary>Reads a player's header.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the header.</returns>
    private static async Task<Ok<LinkPlayerDto>> GetPlayerAsync(
        string code,
        Guid playerId,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetLinkPlayerQuery(code, sessionToken, playerId), cancellationToken));

    /// <summary>Lists a player's videos.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<LinkVideoDto>>> ListVideosAsync(
        string code,
        Guid playerId,
        [AsParameters] PageParameters paging,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetLinkPlayerVideosQuery(code, sessionToken, playerId, paging.ToRequest()), cancellationToken));

    /// <summary>Lists a player's documents.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<LinkDocumentDto>>> ListDocumentsAsync(
        string code,
        Guid playerId,
        [AsParameters] PageParameters paging,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetLinkPlayerDocumentsQuery(code, sessionToken, playerId, paging.ToRequest()), cancellationToken));

    /// <summary>Streams a player's photo.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="response">The response, for the cache header.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the image.</returns>
    private static async Task<FileStreamHttpResult> GetPhotoAsync(
        string code,
        Guid playerId,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var photo = await sender.Send(new GetLinkPlayerPhotoQuery(code, sessionToken, playerId), cancellationToken);
        response.Headers.CacheControl = "private, max-age=300";
        return TypedResults.Stream(photo.Content, photo.ContentType);
    }
}
