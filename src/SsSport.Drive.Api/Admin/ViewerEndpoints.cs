using System.ComponentModel;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Api.Admin;

/// <summary>
/// Viewers (A-7 profile and heatmap, A-11 access and watch log): who can watch, what they can open and how they watched.
/// The console addresses a viewer by id, never by email, so an address never travels in a URL.
/// </summary>
public static class ViewerEndpoints
{
    /// <summary>The OpenAPI tag.</summary>
    public const string Tag = "Admin viewers";

    /// <summary>A realistic list row.</summary>
    private static readonly AdminViewerRowDto[] _exampleRows =
    [
        new()
        {
            Id = Guid.Parse("019a3e1b-4a29-7e0f-93b2-7c8d9e0f1a2b"),
            Email = "j.demir@aurora-volley.example",
            Organisation = "Club Aurora",
            Players = ["Arash Tehrani", "Reza Moradi"],
            PlayerCount = 2,
            Plays = 6,
            WatchSeconds = 2470,
            LastSeenAt = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
            AccessUntil = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero),
            Account = "SignedIn",
        },
    ];

    /// <summary>A realistic access row.</summary>
    private static readonly ViewerAccessRowDto[] _exampleAccess =
    [
        new()
        {
            PlayerId = Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10"),
            PlayerName = "Arash Tehrani",
            LinkId = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"),
            Code = "K7f2Qa",
            Status = "Active",
            GrantedAt = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero),
            Plays = 4,
            WatchSeconds = 1980,
        },
    ];

    /// <summary>A realistic watch-log row.</summary>
    private static readonly ViewerVideoRowDto[] _exampleVideos =
    [
        new()
        {
            PlayerId = Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10"),
            PlayerName = "Arash Tehrani",
            AssetId = Guid.Parse("0199f6a3-1111-7b3d-9a52-3f1c8e7d2b10"),
            Title = "Highlights 2026",
            Plays = 2,
            WatchSeconds = 168,
            UniqueSeconds = 84,
            Completion = 1,
            LastWatchedAt = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>A realistic session row.</summary>
    private static readonly ViewerSessionRowDto[] _exampleSessions =
    [
        new()
        {
            WatchId = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"),
            PlayerName = "Arash Tehrani",
            VideoTitle = "Highlights 2026",
            StartedAt = new DateTimeOffset(2026, 11, 14, 8, 55, 0, TimeSpan.Zero),
            LastSeenAt = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
            WatchSeconds = 84,
            Completion = 1,
        },
    ];

    /// <summary>Maps the viewer endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAdminViewers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/viewers", ListAsync)
            .WithTags(Tag)
            .WithName("ListAdminViewers")
            .RequireAdmin()
            .WithSummary("List viewers")
            .WithDescription(
                "Admins only. One page of viewers, most recently seen first: email and organisation, up to three players they can open, plays, seconds watched, " +
                "last seen, when their latest open link expires and how they sign in (`SignedIn`, `LinkOnly`, `NotYet`). Filter with `q` (part of an email or " +
                "organisation), `activity` (`all`, `active` = watched something, `never`) and `playerId` (viewers of links to that player). 400 for an unknown filter.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/viewers/{id:guid}", GetAsync)
            .WithTags(Tag)
            .WithName("GetAdminViewer")
            .RequireAdmin()
            .WithSummary("Read a viewer's profile")
            .WithDescription("Admins only. The viewer's identity, first seen, last seen, sign-in, plays, seconds watched, how many players they can open and their average completion. 404 for an unknown viewer.")
            .Produces<AdminViewerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/viewers/{id:guid}/access", AccessAsync)
            .WithTags(Tag)
            .WithName("ListAdminViewerAccess")
            .RequireAdmin()
            .WithSummary("List the players a viewer can open")
            .WithDescription("Admins only. One page with a row per link and player the viewer's email is a recipient of: the link's access now, when it was made and expires, and the plays and seconds on it. 404 for an unknown viewer.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithPagedResponse(_exampleAccess)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/viewers/{id:guid}/videos", VideosAsync)
            .WithTags(Tag)
            .WithName("ListAdminViewerVideos")
            .RequireAdmin()
            .WithSummary("Read a viewer's watch log")
            .WithDescription(
                "Admins only. One page with a row per video the viewer played, across every link: plays, seconds played, unique seconds (every part counted once), " +
                "completion and when it was last watched. Filter with `playerId`. 404 for an unknown viewer.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithPagedResponse(_exampleVideos)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/viewers/{id:guid}/videos/{assetId:guid}/heatmap", HeatmapAsync)
            .WithTags(Tag)
            .WithName("GetAdminViewerHeatmap")
            .RequireAdmin()
            .WithSummary("Read a viewer's replay heatmap of a video")
            .WithDescription(
                "Admins only. For each bucket of the video, how many of the viewer's plays covered it. Buckets are 5 seconds wide, or wider so there are never more than 200. " +
                "404 for an unknown viewer or a video the viewer never played.")
            .Produces<ViewerHeatmapDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", """{ "assetId": "0199f6a3-1111-7b3d-9a52-3f1c8e7d2b10", "title": "Highlights 2026", "durationSeconds": 84, "bucketSeconds": 5, "buckets": [1, 2, 2, 1, 0] }"""));

        app.MapGet("/admin/viewers/{id:guid}/sessions", SessionsAsync)
            .WithTags(Tag)
            .WithName("ListAdminViewerSessions")
            .RequireAdmin()
            .WithSummary("List a viewer's plays")
            .WithDescription("Admins only. One cursor page of the viewer's plays, newest first. Device and location are not collected. 404 for an unknown viewer.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithCursorResponse(_exampleSessions)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));
        return app;
    }

    /// <summary>Lists viewers.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="q">The search.</param>
    /// <param name="activity">The activity filter.</param>
    /// <param name="playerId">The player filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<AdminViewerRowDto>>> ListAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "q"), Description("Part of an email or an organisation.")] string? q,
        [FromQuery(Name = "activity"), Description("all (default), active or never.")] string? activity,
        [FromQuery(Name = "playerId"), Description("Only viewers of links to this player.")] Guid? playerId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAdminViewersQuery(paging.ToRequest(), q, activity, playerId), cancellationToken));

    /// <summary>Reads a profile.</summary>
    /// <param name="id">The viewer id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile.</returns>
    private static async Task<Ok<AdminViewerDto>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAdminViewerQuery(id), cancellationToken));

    /// <summary>Lists a viewer's access.</summary>
    /// <param name="id">The viewer id.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<ViewerAccessRowDto>>> AccessAsync(Guid id, [AsParameters] PageParameters paging, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetViewerAccessQuery(id, paging.ToRequest()), cancellationToken));

    /// <summary>Reads a viewer's watch log.</summary>
    /// <param name="id">The viewer id.</param>
    /// <param name="playerId">The player filter.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<ViewerVideoRowDto>>> VideosAsync(
        Guid id,
        [FromQuery(Name = "playerId"), Description("Only this player's videos.")] Guid? playerId,
        [AsParameters] PageParameters paging,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetViewerVideosQuery(id, playerId, paging.ToRequest()), cancellationToken));

    /// <summary>Reads a heatmap.</summary>
    /// <param name="id">The viewer id.</param>
    /// <param name="assetId">The video id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the heatmap.</returns>
    private static async Task<Ok<ViewerHeatmapDto>> HeatmapAsync(Guid id, Guid assetId, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetViewerHeatmapQuery(id, assetId), cancellationToken));

    /// <summary>Lists a viewer's plays.</summary>
    /// <param name="id">The viewer id.</param>
    /// <param name="paging">The cursor page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<CursorResult<ViewerSessionRowDto>>> SessionsAsync(Guid id, [AsParameters] CursorParameters paging, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetViewerSessionsQuery(id, paging.ToRequest()), cancellationToken));
}
