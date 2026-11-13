using System.ComponentModel;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Api.Links;

/// <summary>
/// Personal share links (L-1): admins create, list, pause, resume, revoke and re-date links; anyone may ask whether a
/// <c>/r/{code}</c> link opens. Admins sign in with the Mini App's initData until the console session (A-1) joins.
/// </summary>
public static class ShareLinkEndpoints
{
    /// <summary>The OpenAPI tag of the share link endpoints.</summary>
    public const string Tag = "Share links";

    /// <summary>A realistic New share link form.</summary>
    private const string ExampleCreate =
        """
        {
          "recipient": {
            "name": "Marco Rossi",
            "organisation": "Lube Volley",
            "countryCode": "IT",
            "emails": ["marco.rossi@lube.it"]
          },
          "playerIds": ["0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e"],
          "note": "Watch his serve-receive in the second set.",
          "expiresInDays": 30,
          "verifyOnNewDevice": true,
          "allowForwarding": true,
          "forwardingScope": "AnyEmail",
          "notifyOnOpen": true
        }
        """;

    /// <summary>The link created from <see cref="ExampleCreate"/>.</summary>
    private const string ExampleLink =
        """
        {
          "id": "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c",
          "code": "Kq7mTx",
          "path": "/r/Kq7mTx",
          "type": "Personal",
          "status": "Active",
          "access": "Open",
          "recipient": {
            "id": "019a3e1c-5b29-7c44-9e21-6f7a8b9c0d1e",
            "name": "Marco Rossi",
            "organisation": "Lube Volley",
            "countryCode": "IT",
            "emails": ["marco.rossi@lube.it"]
          },
          "players": [
            { "id": "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e", "name": "Arash Tehrani", "slug": "arash-tehrani" }
          ],
          "note": "Watch his serve-receive in the second set.",
          "expiresAt": "2026-12-01T09:00:00+00:00",
          "revokedAt": null,
          "verifyOnNewDevice": true,
          "allowForwarding": true,
          "forwardingScope": "AnyEmail",
          "notifyOnOpen": true,
          "createdByUserId": "0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27",
          "createdAt": "2026-11-01T09:00:00+00:00"
        }
        """;

    /// <summary>What anyone learns about an open link.</summary>
    private const string ExampleResolved =
        """
        {
          "code": "Kq7mTx",
          "type": "Personal",
          "playerCount": 1,
          "recipientHint": "m***@lube.it",
          "player": { "name": "Arash Tehrani", "position": "OPP", "nationality": { "code": "IR", "name": "Iran" } }
        }
        """;

    /// <summary>Two realistic rows of the links list.</summary>
    private static readonly ShareLinkListItemDto[] _exampleRows =
    [
        new()
        {
            Id = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"),
            Code = "Kq7mTx",
            Type = "Personal",
            Status = "Active",
            Access = "Open",
            RecipientId = Guid.Parse("019a3e1c-5b29-7c44-9e21-6f7a8b9c0d1e"),
            RecipientName = "Marco Rossi",
            RecipientOrganisation = "Lube Volley",
            PlayerNames = ["Arash Tehrani"],
            ExpiresAt = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
        },
        new()
        {
            Id = Guid.Parse("019a2b7d-0c4e-7a91-8b5f-1e2d3c4b5a69"),
            Code = "pW4zRe",
            Type = "Roster",
            Status = "Paused",
            Access = "Paused",
            RecipientId = Guid.Parse("019a2b7d-0c4d-73e2-b6a8-9f0e1d2c3b4a"),
            RecipientName = "Jakub Nowak",
            RecipientOrganisation = "Asseco Resovia",
            PlayerNames = ["Kian Moradi", "Arash Tehrani"],
            ExpiresAt = null,
            CreatedAt = new DateTimeOffset(2026, 10, 28, 14, 30, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>Maps the share link endpoints under the "Share links" tag.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapShareLinks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/links", CreateAsync)
            .WithTags(Tag)
            .WithName("CreateShareLink")
            .RequireAdmin()
            .WithSummary("Create a personal share link")
            .WithDescription(
                "Admins only. Creates `/r/{code}` for a recipient and one to twenty **published** players, in page order: one player makes a " +
                "`Personal` link, several a `Roster`. Send either `recipientId` (an existing recipient) or a new `recipient` (name, optional " +
                "organisation and country, one to five emails). `expiresInDays` is 7, 30 or 90 and is required: every link expires. " +
                "Switches default to verify new devices, allow forwarding to any email, no open notifications. The code is six random letters and " +
                "digits without look-alikes. Audited as `share_link.created`. 400 for no or both recipients, no players, a missing or other expiry or an unknown " +
                "scope; 404 for an unknown recipient or player; 422 `share_link.player_not_published`, `share_link.too_many_players`, " +
                "`share_link.note_too_long`, `recipient.name_required`, `recipient.name_too_long`, `recipient.organisation_too_long`, " +
                "`recipient.email_required`, `recipient.too_many_emails`, `email.invalid`, `player.nationality_invalid`.")
            .Accepts<CreateShareLinkDto>("application/json")
            .Produces<ShareLinkDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("201", ExampleLink, ExampleCreate));

        app.MapGet("/admin/links", ListAsync)
            .WithTags(Tag)
            .WithName("ListShareLinks")
            .RequireAdmin()
            .WithSummary("List share links")
            .WithDescription(
                "Admins only. One page of links, newest first, each with its recipient's name and organisation, its players' names in page order, " +
                "its status and whether it opens right now (`access`: `Open`, `Expired`, `Paused`, `Revoked`). Filter by `status` (active, paused, " +
                "revoked), `playerId` or `recipientId`. 400 for an unknown status or a bad page.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/links/{id:guid}", GetAsync)
            .WithTags(Tag)
            .WithName("GetShareLink")
            .RequireAdmin()
            .WithSummary("Read a share link")
            .WithDescription("Admins only. The link with its recipient (full emails) and players. 404 for an unknown link.")
            .Produces<ShareLinkDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleLink));

        MapStatusChange(app, "pause", ShareLinkStatusChange.Pause, "PauseShareLink", "Pause a share link",
            "Admins only. Stops the link for now; it answers like an expired link until it is resumed. Pausing a paused link changes nothing. " +
            "Audited as `share_link.paused`. 404 for an unknown link; 422 `share_link.revoked`.");
        MapStatusChange(app, "resume", ShareLinkStatusChange.Resume, "ResumeShareLink", "Resume a paused share link",
            "Admins only. Lets a paused link open again; its expiry still applies. Resuming an active link changes nothing. " +
            "Audited as `share_link.resumed`. 404 for an unknown link; 422 `share_link.revoked`.");
        MapStatusChange(app, "revoke", ShareLinkStatusChange.Revoke, "RevokeShareLink", "Revoke a share link",
            "Admins only. Stops the link for good: it can no longer be resumed or re-dated. Revoking twice changes nothing. " +
            "Audited as `share_link.revoked`. 404 for an unknown link.");

        app.MapPatch("/admin/links/{id:guid}/expiry", ChangeExpiryAsync)
            .WithTags(Tag)
            .WithName("ChangeShareLinkExpiry")
            .RequireAdmin()
            .WithSummary("Change a share link's expiry")
            .WithDescription(
                "Admins only. Sets the expiry to 7, 30 or 90 days from now; it cannot be removed. A new expiry opens an expired link again. " +
                "Audited as `share_link.expiry_changed` when it changes. 400 for a missing or other number of days; 404 for an unknown link; " +
                "422 `share_link.revoked`.")
            .Accepts<ChangeShareLinkExpiryDto>("application/json")
            .Produces<ShareLinkDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleLink, """{ "expiresInDays": 90 }"""));

        app.MapGet("/links/{code}", ResolveAsync)
            .WithTags(Tag)
            .WithName("ResolveShareLink")
            .WithSummary("Check whether a personal link opens")
            .WithDescription(
                "Public, no sign-in. The viewer site calls it for `/r/{code}`. An open link answers its type, how many players it shows, a masked " +
                "hint of the recipient's first email and, for a Personal link, the player's name, primary position and nationality for the gate card " +
                "(L-2; `player` is null for a Roster). Photos, videos and files come only after the gate. An unknown, malformed, " +
                "paused, revoked or expired code all answer the same 404 `share_link.unavailable` with the same text, so codes cannot be probed.")
            .Produces<ResolvedShareLinkDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleResolved, signedIn: false));

        return app;
    }

    /// <summary>Maps one of the pause, resume and revoke endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <param name="verb">The last path segment.</param>
    /// <param name="change">The change it applies.</param>
    /// <param name="name">The operation name.</param>
    /// <param name="summary">The summary.</param>
    /// <param name="description">The description.</param>
    private static void MapStatusChange(IEndpointRouteBuilder app, string verb, ShareLinkStatusChange change, string name, string summary, string description) =>
        app.MapPost($"/admin/links/{{id:guid}}/{verb}", (Guid id, ISender sender, CancellationToken cancellationToken) => ChangeStatusAsync(id, change, sender, cancellationToken))
            .WithTags(Tag)
            .RequireAdmin()
            .WithName(name)
            .WithSummary(summary)
            .WithDescription(description)
            .Produces<ShareLinkDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleLink));

    /// <summary>Creates a link.</summary>
    /// <param name="form">The New share link form.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the link.</returns>
    private static async Task<Created<ShareLinkDto>> CreateAsync(CreateShareLinkDto form, ISender sender, CancellationToken cancellationToken)
    {
        var link = await sender.Send(form.ToCommand(), cancellationToken);
        return TypedResults.Created($"/admin/links/{link.Id}", link);
    }

    /// <summary>Lists links.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="status">The status filter.</param>
    /// <param name="playerId">The player filter.</param>
    /// <param name="recipientId">The recipient filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<ShareLinkListItemDto>>> ListAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "status"), Description("active, paused or revoked (any case); all when left out.")] string? status,
        [FromQuery(Name = "playerId"), Description("Only links that show this player.")] Guid? playerId,
        [FromQuery(Name = "recipientId"), Description("Only links for this recipient.")] Guid? recipientId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetShareLinksQuery(paging.ToRequest(), status, playerId, recipientId), cancellationToken));

    /// <summary>Reads a link.</summary>
    /// <param name="id">The link id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the link.</returns>
    private static async Task<Ok<ShareLinkDto>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetShareLinkQuery(id), cancellationToken));

    /// <summary>Pauses, resumes or revokes a link.</summary>
    /// <param name="id">The link id.</param>
    /// <param name="change">The change.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the link.</returns>
    private static async Task<Ok<ShareLinkDto>> ChangeStatusAsync(Guid id, ShareLinkStatusChange change, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ChangeShareLinkStatusCommand(id, change), cancellationToken));

    /// <summary>Changes a link's expiry.</summary>
    /// <param name="id">The link id.</param>
    /// <param name="form">The new expiry.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the link.</returns>
    private static async Task<Ok<ShareLinkDto>> ChangeExpiryAsync(Guid id, ChangeShareLinkExpiryDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ChangeShareLinkExpiryCommand(id, form.ExpiresInDays), cancellationToken));

    /// <summary>Checks whether a code opens.</summary>
    /// <param name="code">The code from <c>/r/{code}</c>.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with what the email gate needs.</returns>
    private static async Task<Ok<ResolvedShareLinkDto>> ResolveAsync(string code, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ResolveShareLinkQuery(code), cancellationToken));
}
