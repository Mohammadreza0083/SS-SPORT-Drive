using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Api.Links;

/// <summary>
/// What a signed-in viewer streams and reads behind <c>/r/{code}</c> (V-5b): a signed edge URL for a video's HLS stream
/// and signed edge URLs for a document's page images. Like the other viewer content routes they are public, the proof
/// is the viewer session token in <c>X-Viewer-Session</c>, and the checks run in a fixed order: a link that does not open
/// is 404 <c>share_link.unavailable</c>; no live session for this link is 401 <c>viewer.verification_required</c>; a
/// player not on the link, or an item that is not a ready, visible file of this player, is 404. Every answer is
/// <c>no-store</c>: the token is bound to the session and lives 30 minutes.
/// </summary>
public static class ViewerMediaEndpoints
{
    /// <summary>A realistic stream answer.</summary>
    private const string ExampleStream =
        """
        {
          "playlistUrl": "/hls/eyJ2IjoxLCJwIjoicGxheWVycy8wMTk5ZjZhMi4uLiJ9.q8Tt3c1ZkXhX2hQp0m5nGdY8F6o/players/0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10/videos/0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f61/v1/hls/master.m3u8",
          "expiresAt": "2026-11-12T10:30:00+00:00"
        }
        """;

    /// <summary>A realistic pages answer.</summary>
    private const string ExamplePages =
        """
        {
          "pages": [
            "/pages/eyJ2IjoxLCJwIjoicGxheWVycy8wMTk5ZjZhMi4uLiJ9.q8Tt3c1ZkXhX2hQp0m5nGdY8F6o/players/0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10/documents/0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f70/v1/pages/1.webp",
            "/pages/eyJ2IjoxLCJwIjoicGxheWVycy8wMTk5ZjZhMi4uLiJ9.q8Tt3c1ZkXhX2hQp0m5nGdY8F6o/players/0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10/documents/0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f70/v1/pages/2.webp"
          ],
          "expiresAt": "2026-11-12T10:30:00+00:00"
        }
        """;

    /// <summary>A realistic scout report: Club Aurora 3-1 Volley Nord, Arash Tehrani's serve and attack.</summary>
    private const string ExampleReport =
        """
        {
          "id": "0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f80",
          "title": "Match vs Volley Nord",
          "match": {
            "date": "2026-03-12",
            "homeTeam": "Club Aurora",
            "awayTeam": "Volley Nord",
            "homeSets": 3,
            "awaySets": 1,
            "sets": [ { "home": 25, "away": 21 }, { "home": 22, "away": 25 }, { "home": 25, "away": 19 }, { "home": 26, "away": 24 } ]
          },
          "player": { "number": 7, "team": "Club Aurora" },
          "skills": [
            { "skill": "Serve", "total": 5, "perfect": 2, "positive": 1, "ok": 0, "negative": 1, "poor": 0, "error": 1, "successRate": 40, "efficiency": 20 },
            { "skill": "Attack", "total": 7, "perfect": 3, "positive": 1, "ok": 0, "negative": 1, "poor": 1, "error": 1, "successRate": 43, "efficiency": 14 }
          ]
        }
        """;

    /// <summary>Example scout files.</summary>
    private static readonly LinkScoutDto[] _exampleScouts =
    [
        new() { Id = Guid.Parse("0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f80"), Title = "Match vs Volley Nord" },
    ];

    /// <summary>Maps the viewer media endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapViewerMedia(this IEndpointRouteBuilder app)
    {
        app.MapGet("/links/{code}/players/{playerId:guid}/videos/{videoId:guid}/stream", GetStreamAsync)
            .WithTags(ViewerContentEndpoints.Tag)
            .WithName("GetVideoStream")
            .WithSummary("Sign a video's stream")
            .WithDescription(
                "For a signed-in viewer: the site-relative URL of the video's master playlist on the edge, `/hls/{token}/{folder}/master.m3u8`, and when the " +
                "token expires (30 minutes). The token is bound to this viewer session, so a copied URL plays nowhere else; the player asks again every " +
                "20 minutes while it plays. Only a ready, visible video of a player on the link. The answer is never cached. 401 `viewer.verification_required` " +
                "without a live session for this link; 404 for a closed link (`share_link.unavailable`), a player not on the link or a video that cannot be watched; " +
                "503 `media.streaming_off` when no edge signing key is configured.")
            .Produces<VideoStreamDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", ExampleStream, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players/{playerId:guid}/documents/{documentId:guid}/pages", GetPagesAsync)
            .WithTags(ViewerContentEndpoints.Tag)
            .WithName("GetDocumentPages")
            .WithSummary("Sign a document's pages")
            .WithDescription(
                "For a signed-in viewer: one site-relative edge URL per page image, in order, `/pages/{token}/{folder}/{n}.webp`, all with one token that expires " +
                "in 30 minutes and is bound to this viewer session. The PDF file itself is never offered. A document whose page count is not known yet has no pages. " +
                "Only a ready, visible document of a player on the link. The answer is never cached. Errors as for the stream.")
            .Produces<DocumentPagesDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", ExamplePages, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players/{playerId:guid}/scouts", ListScoutsAsync)
            .WithTags(ViewerContentEndpoints.Tag)
            .WithName("ListScoutFiles")
            .WithSummary("List a player's DataVolley files")
            .WithDescription(
                "One page of the player's ready and visible DataVolley scout files, in the page order: id and title only. The file itself is never sent " +
                "(V-8). 400 for a bad page; 401 and 404 as for the player.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithPagedResponse(_exampleScouts)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", null, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/links/{code}/players/{playerId:guid}/scouts/{scoutId:guid}", GetScoutReportAsync)
            .WithTags(ViewerContentEndpoints.Tag)
            .WithName("GetScoutReport")
            .WithSummary("Read a DataVolley file as a report")
            .WithDescription(
                "The scout file parsed on the server for this player's page: the match header (date, teams, sets won, each set's score) and the player's stats " +
                "per skill (Serve, Reception, Attack, Block, Dig, Set; total, perfect, errors, success rate, efficiency), found in the file by name. When the " +
                "file does not hold the player, `player` is null and `skills` is empty. The answer is never cached. 422 `scout.unreadable` for a file that is " +
                "not a DataVolley file (its text is never echoed); 401 and 404 as for the player; 404 also for a hidden file or one of another player.")
            .Produces<ScoutReportDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", ExampleReport, null, ViewerGateEndpoints.SessionSchemeName));

        return app;
    }

    /// <summary>Signs a video's stream.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="videoId">The video id.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="response">The response, for the cache header.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the signed URL.</returns>
    private static async Task<Ok<VideoStreamDto>> GetStreamAsync(
        string code,
        Guid playerId,
        Guid videoId,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var stream = await sender.Send(new GetVideoStreamQuery(code, sessionToken, playerId, videoId), cancellationToken);
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(stream);
    }

    /// <summary>Signs a document's pages.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="documentId">The document id.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="response">The response, for the cache header.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the signed page URLs.</returns>
    private static async Task<Ok<DocumentPagesDto>> GetPagesAsync(
        string code,
        Guid playerId,
        Guid documentId,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var pages = await sender.Send(new GetDocumentPagesQuery(code, sessionToken, playerId, documentId), cancellationToken);
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(pages);
    }

    /// <summary>Lists a player's scout files.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<LinkScoutDto>>> ListScoutsAsync(
        string code,
        Guid playerId,
        [AsParameters] PageParameters paging,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetLinkPlayerScoutsQuery(code, sessionToken, playerId, paging.ToRequest()), cancellationToken));

    /// <summary>Reads a scout file as a report.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player id.</param>
    /// <param name="scoutId">The scout file id.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="response">The response, for the cache header.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the report.</returns>
    private static async Task<Ok<ScoutReportDto>> GetScoutReportAsync(
        string code,
        Guid playerId,
        Guid scoutId,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var report = await sender.Send(new GetScoutReportQuery(code, sessionToken, playerId, scoutId), cancellationToken);
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(report);
    }
}
