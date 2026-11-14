using System.ComponentModel;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Public;

namespace SsSport.Drive.Api.Public;

/// <summary>
/// The admins' inbox of access requests (P-4): list them, approve (which makes and emails a personal link) or decline.
/// Admins sign in with the Mini App's initData or a console session (<c>X-Admin-Session</c>). The bot's Approve and
/// Decline buttons use the same rules.
/// </summary>
public static class AccessRequestInboxEndpoints
{
    /// <summary>The OpenAPI tag, shared with the public request endpoints.</summary>
    public const string Tag = PublicAccessEndpoints.Tag;

    /// <summary>An approval with the admin's changes.</summary>
    private const string ExampleApprove =
        """
        { "expiresInDays": 90, "playerIds": ["0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e"], "note": "The opposite you asked about." }
        """;

    /// <summary>An approved request with its new link.</summary>
    private const string ExampleApproved =
        """
        {
          "id": "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c",
          "player": { "id": "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e", "name": "Arash Tehrani", "slug": "arash-tehrani" },
          "email": "j.demir@aurora-volley.example",
          "about": "Aurora Volley, head scout. Looking for an opposite for 2027.",
          "source": "EmailCode",
          "status": "Approved",
          "createdAt": "2026-11-14T09:00:00+00:00",
          "decidedAt": "2026-11-14T09:30:00+00:00",
          "decidedBy": "Owner",
          "shareLink": { "id": "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3d", "code": "Kq7mTx", "path": "/r/Kq7mTx", "expiresAt": "2026-12-14T09:30:00+00:00" }
        }
        """;

    /// <summary>Rows of the list example.</summary>
    private static readonly IReadOnlyList<AccessRequestDto> _exampleRows =
    [
        new()
        {
            Id = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"),
            Player = new ShareLinkPlayerDto { Id = Guid.Parse("0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e"), Name = "Arash Tehrani", Slug = "arash-tehrani" },
            Email = "j.demir@aurora-volley.example",
            About = "Aurora Volley, head scout. Looking for an opposite for 2027.",
            Source = "EmailCode",
            Status = "Pending",
            CreatedAt = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>Maps the inbox endpoints under the "Access requests" tag.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAccessRequestInbox(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/access-requests", ListAsync)
            .WithTags(Tag)
            .WithName("ListAccessRequests")
            .RequireAdmin()
            .WithSummary("List access requests")
            .WithDescription(
                "Admins only. One page of requests, newest first, each with the player, the requester's verified email, what they wrote, how the email was " +
                "proven (`EmailCode` or `SignedIn`) and its status. `status` is `pending` (the default), `approved` or `declined`; a decided request shows who " +
                "decided it and, when approved, the personal link made for the requester. 400 for an unknown status or a bad page.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPost("/admin/access-requests/{id:guid}/approve", ApproveAsync)
            .WithTags(Tag)
            .WithName("ApproveAccessRequest")
            .RequireAdmin()
            .WithSummary("Approve an access request")
            .WithDescription(
                "Admins only. Makes a personal `/r/{code}` link for the requester's email and emails it to them (general mail). By default it shows the " +
                "requested player for 30 days; the body may change the expiry (`expiresInDays`: 7, 30 or 90), the players (one to twenty published players) and " +
                "add a note. The first decision is final: 409 `access_request.already_decided`. Audited as `access_request.approved` and `share_link.created`. " +
                "400 for another expiry or too many players; 404 for an unknown request or player; 422 `share_link.player_not_published`, `share_link.note_too_long`.")
            .Accepts<ApproveAccessRequestDto>("application/json")
            .Produces<AccessRequestDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleApproved, ExampleApprove));

        app.MapPost("/admin/access-requests/{id:guid}/decline", DeclineAsync)
            .WithTags(Tag)
            .WithName("DeclineAccessRequest")
            .RequireAdmin()
            .WithSummary("Decline an access request")
            .WithDescription(
                "Admins only. Records the decision; the requester is not emailed. The first decision is final: 409 `access_request.already_decided`. " +
                "Audited as `access_request.declined`. 404 for an unknown request.")
            .Produces<AccessRequestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleApproved));

        return app;
    }

    /// <summary>Lists requests.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="status">The status filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<AccessRequestDto>>> ListAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "status"), Description("pending (default), approved or declined (any case).")] string? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAccessRequestsQuery(paging.ToRequest(), status), cancellationToken));

    /// <summary>Approves a request.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="form">The admin's changes, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the decided request.</returns>
    private static async Task<Ok<AccessRequestDto>> ApproveAsync(Guid id, ApproveAccessRequestDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(form.ToCommand(id), cancellationToken));

    /// <summary>Declines a request.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the decided request.</returns>
    private static async Task<Ok<AccessRequestDto>> DeclineAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new DeclineAccessRequestCommand(id), cancellationToken));
}
