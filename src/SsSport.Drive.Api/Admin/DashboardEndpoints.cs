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

/// <summary>The console dashboard (A-2): KPIs, views per day, what needs attention, top players and the live activity feed.</summary>
public static class DashboardEndpoints
{
    /// <summary>The OpenAPI tag.</summary>
    public const string Tag = "Admin dashboard";

    /// <summary>A realistic dashboard.</summary>
    private const string ExampleDashboard =
        """
        {
          "period": "30d",
          "kpis": { "livePlayers": 142, "views": 1204, "previousViews": 1100, "uniqueViewers": 131, "averageCompletion": 0.61, "pendingAccessRequests": 3, "waitingPlayers": 5 },
          "viewsByDay": [ { "date": "2026-10-16", "views": 31 }, { "date": "2026-10-17", "views": 42 } ],
          "needsAttention": { "accessRequests": 3, "waitingPlayers": 5, "failedFiles": 1, "failedEmails": 0, "retryingEmails": 1, "lostFolders": 0 },
          "topPlayers": [ { "playerId": "0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10", "name": "Arash Tehrani", "views": 41, "averageCompletion": 0.72 } ]
        }
        """;

    /// <summary>Two realistic feed rows.</summary>
    private static readonly ActivityRowDto[] _exampleRows =
    [
        new()
        {
            WatchId = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"),
            ViewerId = Guid.Parse("019a3e1b-4a29-7e0f-93b2-7c8d9e0f1a2b"),
            Email = "j.demir@aurora-volley.example",
            Organisation = "Club Aurora",
            PlayerId = Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10"),
            PlayerName = "Arash Tehrani",
            VideoTitle = "Highlights 2026",
            Completion = 0.62,
            LastSeenAt = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
            IsLive = true,
        },
    ];

    /// <summary>Maps the dashboard endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/dashboard", GetAsync)
            .WithTags(Tag)
            .WithName("GetDashboard")
            .RequireAdmin()
            .WithSummary("Read the dashboard")
            .WithDescription(
                "Admins only. KPIs for the period (`7d`, `30d` default, `90d`): live players, views (watches started), the previous period's views, unique viewers, " +
                "average completion, pending access requests and waiting players. Views per Tehran day (oldest first, empty days as zero), what needs an admin " +
                "(failed files and emails, lost Drive folders, the two queues) and the five most watched players. 400 for an unknown period.")
            .Produces<DashboardDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleDashboard));

        app.MapGet("/admin/activity", ActivityAsync)
            .WithTags(Tag)
            .WithName("GetActivity")
            .RequireAdmin()
            .WithSummary("Read the live activity feed")
            .WithDescription(
                "Admins only. One cursor page of watches, newest activity first: the viewer and their organisation, the player, the video, how much of it was " +
                "watched and whether the viewer was heard from in the last minute (`isLive`).")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithCursorResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));
        return app;
    }

    /// <summary>Reads the dashboard.</summary>
    /// <param name="period">The period.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the dashboard.</returns>
    private static async Task<Ok<DashboardDto>> GetAsync(
        [FromQuery(Name = "period"), Description("7d, 30d (default) or 90d.")] string? period,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetDashboardQuery(period), cancellationToken));

    /// <summary>Reads one page of the feed.</summary>
    /// <param name="paging">The cursor page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<CursorResult<ActivityRowDto>>> ActivityAsync([AsParameters] CursorParameters paging, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetActivityQuery(paging.ToRequest()), cancellationToken));
}
