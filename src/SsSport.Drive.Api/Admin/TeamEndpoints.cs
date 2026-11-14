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

/// <summary>Users &amp; roles, the audit log (A-8) and the jobs and email queue pages (A-9).</summary>
public static class TeamEndpoints
{
    /// <summary>The OpenAPI tag of the team endpoints.</summary>
    public const string Tag = "Admin users";

    /// <summary>The OpenAPI tag of the jobs endpoints.</summary>
    public const string JobsTag = "Admin jobs";

    /// <summary>A realistic member.</summary>
    private static readonly AdminUserDto[] _exampleUsers =
    [
        new()
        {
            Id = Guid.Parse("019a3e1b-4a29-7e0f-93b2-7c8d9e0f1a2b"),
            DisplayName = "Sara N.",
            Username = "sara_n",
            SignIn = "Telegram",
            Role = "Analyst",
            Status = "Active",
            PlayerCount = 64,
            LastActiveAt = new DateTimeOffset(2026, 11, 14, 8, 40, 0, TimeSpan.Zero),
            RequestedAt = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>A realistic audit entry.</summary>
    private static readonly AdminAuditEntryDto[] _exampleAudit =
    [
        new()
        {
            Id = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"),
            At = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
            ActorId = Guid.Parse("019a3e1b-4a29-7e0f-93b2-7c8d9e0f1a2b"),
            ActorName = "Mohammadreza",
            Action = "user.approved",
            TargetType = "User",
            TargetId = "019a3e1d-6c3b-7a21-b5d4-9e0f1a2b3c4d",
            Data = """{"role":"Analyst"}""",
        },
    ];

    /// <summary>A realistic job.</summary>
    private static readonly JobRowDto[] _exampleJobs =
    [
        new()
        {
            AssetId = Guid.Parse("0199f6a3-1111-7b3d-9a52-3f1c8e7d2b10"),
            PlayerId = Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10"),
            PlayerName = "Arash Tehrani",
            Title = "Full Match 02",
            FileName = "Full_Match_02.mov",
            Kind = "Video",
            State = "Failed",
            FailureReason = "ffmpeg exited with code 1",
            CreatedAt = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>Maps the team, audit and jobs endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAdminTeam(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/users", ListUsersAsync)
            .WithTags(Tag)
            .WithName("ListAdminUsers")
            .RequireAdmin()
            .WithSummary("List the team")
            .WithDescription(
                "Admins only. One page of team members by name: role, status, how they sign in (`Email` for admins, `Telegram` for analysts), the masked email, how many " +
                "players they are responsible for and when they were last active. Filter with `status` (active, pending, declined, removed). 400 for an unknown status.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleUsers)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPost("/admin/users/{id:guid}/approve", ApproveAsync)
            .WithTags(Tag)
            .WithName("ApproveAdminUser")
            .RequireAdmin()
            .WithSummary("Approve an access request")
            .WithDescription(
                "Admins only. Approves a pending bot request as `Analyst` or `Admin`. Audited as `user.approved`. 400 for an unknown role; 404 for an unknown user; " +
                "422 `user.admin_needs_email` when approving as Admin without an email, or `user.invalid_state` when the user is not pending.")
            .Accepts<ApproveUserDto>("application/json")
            .Produces<AdminUserDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null, """{ "role": "Analyst" }"""));

        app.MapPost("/admin/users/{id:guid}/decline", DeclineAsync)
            .WithTags(Tag)
            .WithName("DeclineAdminUser")
            .RequireAdmin()
            .WithSummary("Decline an access request")
            .WithDescription("Admins only. Declines a pending request; the user may ask again after 24 hours. Audited as `user.declined`. 404 for an unknown user; 422 when the user is not pending.")
            .Accepts<object>("application/json")
            .Produces<AdminUserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPatch("/admin/users/{id:guid}", UpdateUserAsync)
            .WithTags(Tag)
            .WithName("UpdateAdminUser")
            .RequireAdmin()
            .WithSummary("Change a member's email or role")
            .WithDescription(
                "Admins only. The email is applied first, then the role, so an analyst can be promoted in one call. Audited as `user.email_changed` and " +
                "`user.role_changed` (no email in the audit data). 400 for an unknown role or a bad email; 404 for an unknown user; 422 `user.admin_needs_email`, " +
                "`user.last_admin` for the last active admin.")
            .Accepts<UpdateUserDto>("application/json")
            .Produces<AdminUserDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null, """{ "email": "ali.r@ss-sport.co", "role": "Admin" }"""));

        app.MapPost("/admin/users/{id:guid}/remove", RemoveAsync)
            .WithTags(Tag)
            .WithName("RemoveAdminUser")
            .RequireAdmin()
            .WithSummary("Remove a member")
            .WithDescription(
                "Admins only. Removes an active member; the record stays. A member with players needs `reassignTo`, an active admin who takes the players over. Audited as " +
                "`user.removed`. 400 `reassignTo` when it is missing or not an active admin; 404 for an unknown user; 422 `user.last_admin`.")
            .Accepts<RemoveUserDto>("application/json")
            .Produces<AdminUserDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null, """{ "reassignTo": "019a3e1b-4a29-7e0f-93b2-7c8d9e0f1a2b" }"""));

        app.MapGet("/admin/audit", AuditAsync)
            .WithTags(Tag)
            .WithName("ListAuditLog")
            .RequireAdmin()
            .WithSummary("Read the audit log")
            .WithDescription("Admins only. One cursor page of the audit log, newest first, with the actor's name. Filter with `action` (for example `user.declined`). Entries are append-only and never hold an email.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithCursorResponse(_exampleAudit)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/jobs", JobsAsync)
            .WithTags(JobsTag)
            .WithName("ListAdminJobs")
            .RequireAdmin()
            .WithSummary("List the files being ingested")
            .WithDescription("Admins only. One page of the files that are not ready, newest first: the player, the file, its state and why it failed. Filter with `state` (queued, copying, processing, failed). 400 for any other state.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleJobs)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/jobs/summary", JobsSummaryAsync)
            .WithTags(JobsTag)
            .WithName("GetAdminJobsSummary")
            .RequireAdmin()
            .WithSummary("Count the jobs and email tiles")
            .WithDescription("Admins only. The files per state, the Drive folders (and those that lost access), the last Drive sync, and the emails sent in the last 24 hours, failed and queued.")
            .Produces<JobsSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPost("/admin/emails/{id:guid}/retry", RetryEmailAsync)
            .WithTags(JobsTag)
            .WithName("RetryAdminEmail")
            .RequireAdmin()
            .WithSummary("Retry a failed email")
            .WithDescription("Admins only. Puts a failed email back on the queue with a fresh set of attempts. Audited as `email.retried`. 404 for an unknown email; 422 `email.not_failed` for an email that did not fail.")
            .Accepts<object>("application/json")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("202", null));
        return app;
    }

    /// <summary>Lists the team.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="status">The status filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<AdminUserDto>>> ListUsersAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "status"), Description("active, pending, declined or removed; all when left out.")] string? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAdminUsersQuery(paging.ToRequest(), status), cancellationToken));

    /// <summary>Approves a request.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="form">The role.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the member.</returns>
    private static async Task<Ok<AdminUserDto>> ApproveAsync(Guid id, ApproveUserDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ApproveUserCommand(id, form.Role), cancellationToken));

    /// <summary>Declines a request.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the member.</returns>
    private static async Task<Ok<AdminUserDto>> DeclineAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new DeclineUserCommand(id), cancellationToken));

    /// <summary>Changes a member.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="form">The new email and role.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the member.</returns>
    private static async Task<Ok<AdminUserDto>> UpdateUserAsync(Guid id, UpdateUserDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new UpdateUserCommand(id, form.Email, form.Role), cancellationToken));

    /// <summary>Removes a member.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="form">Who takes over the players.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the member.</returns>
    private static async Task<Ok<AdminUserDto>> RemoveAsync(Guid id, RemoveUserDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new RemoveUserCommand(id, form.ReassignTo), cancellationToken));

    /// <summary>Reads the audit log.</summary>
    /// <param name="paging">The cursor page.</param>
    /// <param name="action">The action filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<CursorResult<AdminAuditEntryDto>>> AuditAsync(
        [AsParameters] CursorParameters paging,
        [FromQuery(Name = "action"), Description("An action code such as user.declined; all when left out.")] string? action,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAuditLogQuery(action, paging.ToRequest()), cancellationToken));

    /// <summary>Lists the files being ingested.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="state">The state filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<JobRowDto>>> JobsAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "state"), Description("queued, copying, processing or failed; all unfinished when left out.")] string? state,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetJobsQuery(paging.ToRequest(), state), cancellationToken));

    /// <summary>Counts the tiles.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the tiles.</returns>
    private static async Task<Ok<JobsSummaryDto>> JobsSummaryAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetJobsSummaryQuery(), cancellationToken));

    /// <summary>Retries a failed email.</summary>
    /// <param name="id">The email id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202.</returns>
    private static async Task<Accepted> RetryEmailAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new RetryEmailCommand(id), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }
}
