using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.Links;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Tracking;

namespace SsSport.Drive.Api.Tracking;

/// <summary>
/// Watch tracking behind a personal link (L-5) and its metrics for admins (L-6). The viewer's player starts a watch on
/// the first play, sends a heartbeat with the range played every 10 seconds and reports play, pause, seek and ended. Every
/// call needs the viewer's live session for the link (<c>X-Viewer-Session</c>), like the content reads.
/// </summary>
public static class TrackingEndpoints
{
    /// <summary>The OpenAPI tag of the viewer's calls.</summary>
    public const string Tag = "Viewer tracking";

    /// <summary>An example of a started watch.</summary>
    private const string ExampleStarted =
        """
        {
          "watchId": "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c",
          "heartbeatSeconds": 10
        }
        """;

    /// <summary>Two example rows of the viewing.</summary>
    private static readonly ViewingRowDto[] _exampleRows =
    [
        new()
        {
            Email = "marco.rossi@lube.it",
            PlayerId = Guid.Parse("0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e"),
            AssetId = Guid.Parse("0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f61"),
            Plays = 2,
            WatchSeconds = 130.5,
            UniqueSeconds = 84,
            Completion = 1,
            IsComplete = true,
            LastSeenAt = new DateTimeOffset(2026, 11, 12, 9, 30, 0, TimeSpan.Zero),
        },
        new()
        {
            Email = "marco.rossi@lube.it",
            PlayerId = Guid.Parse("0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e"),
            AssetId = Guid.Parse("0199f6a3-1d2e-7c4f-8a01-6b2c3d4e5f60"),
            Plays = 1,
            WatchSeconds = 610,
            UniqueSeconds = 610,
            Completion = 0.094,
            IsComplete = false,
            LastSeenAt = new DateTimeOffset(2026, 11, 12, 9, 10, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>Maps the tracking endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapTracking(this IEndpointRouteBuilder app)
    {
        app.MapPost("/links/{code}/players/{playerId:guid}/videos/{assetId:guid}/watch", StartAsync)
            .WithTags(Tag)
            .WithName("StartWatch")
            .WithSummary("Start watching a video")
            .WithDescription(
                "For a signed-in viewer: call on the first play. Counts one play and answers the watch id to send with every heartbeat and event, and how often to " +
                "send a heartbeat. 404 `share_link.unavailable` when the link does not open; 401 `viewer.verification_required` without a live session for this " +
                "link; 404 `not_found` for a player who is not on the link; 404 `tracking.unknown_video` for a video that is hidden, still processing or not the player's.")
            .Produces<WatchStartedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("201", ExampleStarted, null, ViewerGateEndpoints.SessionSchemeName));

        app.MapPost("/links/{code}/watch/{watchId:guid}/heartbeat", HeartbeatAsync)
            .WithTags(Tag)
            .WithName("RecordHeartbeat")
            .WithSummary("Send a heartbeat")
            .WithDescription(
                "For a signed-in viewer: the range played since the last heartbeat, in seconds. Contiguous ranges join; a seek back starts a new one. 204 when taken. " +
                "422 `tracking.invalid_range` for a range that cannot be real (backwards, longer than 30 seconds, past the end of the video); 404 " +
                "`tracking.unknown_watch` for a watch that is not this session's; 401 and 404 as for starting a watch.")
            .Accepts<HeartbeatFormDto>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("204", null, """{ "from": 20, "to": 30 }""", ViewerGateEndpoints.SessionSchemeName));

        app.MapPost("/links/{code}/watch/{watchId:guid}/events", EventAsync)
            .WithTags(Tag)
            .WithName("RecordPlaybackEvent")
            .WithSummary("Report a player event")
            .WithDescription(
                "For a signed-in viewer: `Play`, `Pause`, `Seek` or `Ended`, with the position in seconds. Playing again after `Ended` counts a new play. 204 when taken. " +
                "400 for an unknown event or a bad position; 401 and 404 as for a heartbeat.")
            .Accepts<PlaybackEventFormDto>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("204", null, """{ "type": "Pause", "position": 42.5 }""", ViewerGateEndpoints.SessionSchemeName));

        app.MapGet("/admin/links/{id:guid}/viewing", ViewingAsync)
            .WithTags(ShareLinkEndpoints.Tag)
            .WithName("GetLinkViewing")
            .WithSummary("Read how a link was watched")
            .WithDescription(
                "Admins only. One page of rows, one per viewer email and video, the most recently watched first: plays, watch time (every second played), unique " +
                "seconds watched (ranges merged), completion (0 to 1, complete from 0.9) and when the viewer was last seen. 404 for an unknown link.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithPagedResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));
        return app;
    }

    /// <summary>Starts a watch.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="assetId">The video.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the watch id.</returns>
    private static async Task<Created<WatchStartedDto>> StartAsync(
        string code,
        Guid playerId,
        Guid assetId,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var started = await sender.Send(new StartWatchCommand(code, sessionToken, playerId, assetId), cancellationToken);
        return TypedResults.Created((string?)null, started);
    }

    /// <summary>Takes a heartbeat.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="watchId">The watch.</param>
    /// <param name="form">The range.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204.</returns>
    private static async Task<NoContent> HeartbeatAsync(
        string code,
        Guid watchId,
        HeartbeatFormDto form,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RecordHeartbeatCommand(code, sessionToken, watchId, form.From, form.To), cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Takes a player event.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="watchId">The watch.</param>
    /// <param name="form">The event.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204.</returns>
    private static async Task<NoContent> EventAsync(
        string code,
        Guid watchId,
        PlaybackEventFormDto form,
        [FromHeader(Name = ViewerGateEndpoints.SessionHeader)] string? sessionToken,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RecordPlaybackEventCommand(code, sessionToken, watchId, form.Type, form.Position), cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Reads a link's viewing.</summary>
    /// <param name="id">The link.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<ViewingRowDto>>> ViewingAsync(
        Guid id,
        [AsParameters] PageParameters paging,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetLinkViewingQuery(id, paging.ToRequest()), cancellationToken));
}
