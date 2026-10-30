using System.ComponentModel;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Application.DriveFolders;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Players;

namespace SsSport.Drive.Api.MiniApp;

/// <summary>
/// Player endpoints of the Mini App: My players and a player's files (T-7), and update-player mode (T-5): read an
/// existing player card, add a Drive folder to it, edit its details.
/// </summary>
public static class MiniAppPlayerEndpoints
{
    /// <summary>A realistic player card for the API reference.</summary>
    private const string ExampleCard =
        """
        {
          "id": "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e",
          "name": "Arash Tehrani",
          "slug": "arash-tehrani",
          "category": "Men",
          "nationalityCode": "IR",
          "nationalityName": "Iran",
          "positions": ["OH", "OPP"],
          "photoKey": "players/0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e/photo-1761750000000.webp",
          "status": "Waiting",
          "isComplete": true,
          "createdAt": "2026-10-29T13:30:00+00:00",
          "publishedAt": null,
          "volleyboxId": 48213,
          "volleyboxUrl": "https://volleybox.net/arash-tehrani-p48213",
          "missingDetails": [],
          "heroAssetId": null,
          "analystId": "0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27",
          "qrCodeKey": null,
          "analystName": "Reza Ahmadi",
          "hasPhoto": true,
          "folderCount": 2,
          "folders": [
            {
              "id": "0199f3c0-1a2b-7c3d-8e4f-5a6b7c8d9e0f",
              "playerId": "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e",
              "driveFolderId": "2ZyX-wVu_TsRqPoNmLk",
              "name": "SS-SPORT Arash Tehrani 25-26",
              "access": "Ok",
              "linkedAt": "2026-10-29T15:00:00+00:00",
              "lastSyncedAt": null,
              "accessLostAt": null
            }
          ],
          "canEditDetails": true,
          "canChangePhoto": true,
          "canEditFiles": true,
          "canHideFiles": false
        }
        """;

    /// <summary>An edit-video sheet that renames a file, retags it and makes it the hero.</summary>
    private const string ExampleFileEdit =
        """
        {
          "title": "Best spikes 25-26",
          "tags": ["Highlight", "Spike"],
          "isHero": true
        }
        """;

    /// <summary>The file after that edit.</summary>
    private const string ExampleFileEdited =
        """
        {
          "id": "0199f4a1-3b2c-7d4e-8f5a-6b7c8d9e0f1a",
          "kind": "Video",
          "title": "Best spikes 25-26",
          "tags": ["Highlight", "Spike"],
          "sortOrder": 0,
          "visibility": "Visible",
          "state": "Ready",
          "durationSeconds": 184.5,
          "pageCount": null,
          "isHero": true
        }
        """;

    /// <summary>A folder added to a player.</summary>
    private const string ExampleFolder =
        """
        {
          "id": "0199f3c0-1a2b-7c3d-8e4f-5a6b7c8d9e0f",
          "playerId": "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e",
          "driveFolderId": "2ZyX-wVu_TsRqPoNmLk",
          "name": "SS-SPORT Arash Tehrani 25-26",
          "access": "Ok",
          "linkedAt": "2026-10-29T15:00:00+00:00",
          "lastSyncedAt": null,
          "accessLostAt": null
        }
        """;

    /// <summary>An edit that changes two fields.</summary>
    private const string ExampleEdit =
        """
        {
          "nationalityCode": "IT",
          "positions": ["OPP"]
        }
        """;

    /// <summary>The player after the edit.</summary>
    private const string ExampleEdited =
        """
        {
          "id": "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e",
          "name": "Arash Tehrani",
          "slug": "arash-tehrani",
          "category": "Men",
          "nationalityCode": "IT",
          "nationalityName": "Italy",
          "positions": ["OPP"],
          "photoKey": null,
          "status": "Waiting",
          "isComplete": false,
          "createdAt": "2026-10-29T13:30:00+00:00",
          "publishedAt": null,
          "volleyboxId": 48213,
          "volleyboxUrl": "https://volleybox.net/arash-tehrani-p48213",
          "missingDetails": ["Photo"],
          "heroAssetId": null,
          "analystId": "0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27",
          "qrCodeKey": null
        }
        """;

    /// <summary>Two realistic rows of My players for the API reference.</summary>
    private static readonly PlayerListItemDto[] _exampleRows =
    [
        new()
        {
            Id = Guid.Parse("0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e"),
            Name = "Arash Tehrani",
            Category = "Men",
            NationalityCode = "IR",
            NationalityName = "Iran",
            Positions = ["OH", "OPP"],
            HasPhoto = true,
            Status = "Published",
            IsComplete = true,
            FileCount = 14,
            ProcessingCount = 2,
            CreatedAt = new DateTimeOffset(2026, 10, 29, 13, 30, 0, TimeSpan.Zero),
        },
        new()
        {
            Id = Guid.Parse("0199f1d0-7a3b-7e21-9c4d-2b8a6f1e0c35"),
            Name = "Kian Moradi",
            Category = "Men",
            NationalityCode = "IR",
            NationalityName = "Iran",
            Positions = ["S"],
            Status = "Waiting",
            FileCount = 3,
            FailedCount = 1,
            CreatedAt = new DateTimeOffset(2026, 10, 28, 9, 15, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>Two realistic files of a player for the API reference.</summary>
    private static readonly PlayerFileDto[] _exampleFiles =
    [
        new()
        {
            Id = Guid.Parse("0199f4a1-3b2c-7d4e-8f5a-6b7c8d9e0f1a"),
            Kind = "Video",
            Title = "Highlights 2025-26",
            Tags = ["Highlight"],
            SortOrder = 0,
            Visibility = "Visible",
            State = "Ready",
            DurationSeconds = 184.5,
            IsHero = true,
        },
        new()
        {
            Id = Guid.Parse("0199f4a1-3b2c-7d4e-8f5a-6b7c8d9e0f1b"),
            Kind = "Document",
            Title = "Season stats",
            Tags = ["Stats"],
            SortOrder = 1,
            Visibility = "Visible",
            State = "Processing",
        },
    ];

    /// <summary>Maps the update-mode and My players endpoints under the "Mini App" tag.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/miniapp/players/{id:guid}", GetAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("GetMiniAppPlayer")
            .WithSummary("Read a player card for update mode")
            .WithDescription(
                "Every detail of the player, its owning analyst, whether it has a photo, the number of linked Drive folders and the latest 10 of them, " +
                "and what the caller may change: `canEditDetails` (an admin, or the owning analyst while the player is Waiting) and `canChangePhoto` " +
                "(the owning analyst or an admin). Any active analyst or admin may read it. 404 for an unknown player.")
            .Produces<MiniAppPlayerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleCard));

        app.MapGet("/miniapp/players", ListAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("ListMyPlayers")
            .WithSummary("List My players")
            .WithDescription(
                "One page of players, newest first. An analyst sees only the players they own; an admin sees every player. `search` matches part of the " +
                "name (any case) or the exact Volleybox id. `filter` is a chip: `all` (default), `live` (published), `processing` (a file is queued, " +
                "copying or processing) or `needsAttention` (details missing, a file failed, or a Drive folder lost access). Each row carries the counts " +
                "behind the chips. 400 for an unknown filter, a search over 100 characters or a bad page.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/miniapp/players/{id:guid}/files", ListFilesAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("ListMiniAppPlayerFiles")
            .WithSummary("List a player's files in page order")
            .WithDescription(
                "One page of the player's files for the player detail, in page order: hidden files included, each with its kind, tags, processing state " +
                "and whether it is the hero video. Any active analyst or admin may read them, like the player card. 404 for an unknown player.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithPagedResponse(_exampleFiles)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPatch("/miniapp/players/{id:guid}/files/{fileId:guid}", EditFileAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("EditMiniAppPlayerFile")
            .WithSummary("Edit a player's file (edit-video sheet)")
            .WithDescription(
                "Changes the fields that are sent; fields left out stay as they are. `title` (1 to 200 characters; a hand-edited title no longer follows " +
                "Drive renames), `tags` (every tag the file should have: Highlight, Spike, Serve, Block, Reception, Defence, FullMatch, Stats; empty clears them) " +
                "and `isHero` are for the owning analyst or an admin, also after Publish. `hidden` is for admins only; hiding and showing are audited " +
                "(`asset.hidden`, `asset.shown`), and hiding the hero clears it. Files are never deleted here. 400 for a bad title or an unknown tag; 403 for " +
                "another analyst, or a non-admin sending `hidden`; 404 for an unknown player or a file of another player; 409 `concurrency.conflict` when " +
                "someone saved the same player or file at the same moment; 422 `asset.hero_not_video`, `asset.hero_hidden`.")
            .Accepts<EditPlayerFileDto>("application/json")
            .Produces<PlayerFileDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleFileEdited, ExampleFileEdit));

        app.MapPost("/miniapp/players/{id:guid}/folders", AddFolderAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("AddMiniAppPlayerFolder")
            .WithSummary("Add a Drive folder to a player")
            .WithDescription(
                "Adds a Drive folder to an existing player; earlier folders stay (a folder is never replaced). Any active analyst or admin may add one; " +
                "when it is not the owning analyst, the owner is told by the bot. The folder is checked like a new player folder: 422 `drive_folder.link_invalid`, " +
                "`drive_folder.taken`, `drive_folder.no_access`, `drive_folder.outside_shared_drive`, `drive_folder.not_a_folder`; 503 `drive.unavailable`. " +
                "404 for an unknown player.")
            .Accepts<AddFolderDto>("application/json")
            .Produces<DriveFolderDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("201", ExampleFolder, """{ "driveFolderUrl": "https://drive.google.com/drive/folders/2ZyX-wVu_TsRqPoNmLk" }"""));

        app.MapPatch("/miniapp/players/{id:guid}", UpdateAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("UpdateMiniAppPlayer")
            .WithSummary("Edit a player details")
            .WithDescription(
                "Changes the fields that are sent; fields left out stay as they are. Allowed for an admin, and for the owning analyst while the player is " +
                "Waiting; anyone else gets 403 (after Publish only an admin may edit, because a new name gives a new slug). The rules match the new-player form: " +
                "400 for unreadable category or positions, 422 `player.name_invalid_characters`, `player.name_too_long`, `player.nationality_invalid`, " +
                "`player.duplicate_position`, `player.volleybox_url_invalid`, `player.volleybox_not_a_player`, `player.volleybox_taken`. 404 for an unknown player.")
            .Accepts<UpdatePlayerDto>("application/json")
            .Produces<PlayerDetailsDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleEdited, ExampleEdit));

        return app;
    }

    /// <summary>Lists My players.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="search">Part of the name or the exact Volleybox id.</param>
    /// <param name="filter">The filter chip.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<PlayerListItemDto>>> ListAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "search"), Description("Part of the player's name (any case) or the exact Volleybox id.")] string? search,
        [FromQuery(Name = "filter"), Description("all (default), live, processing or needsAttention.")] string? filter,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMyPlayersQuery(paging.ToRequest(), search, filter), cancellationToken));

    /// <summary>Lists a player's files.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<PlayerFileDto>>> ListFilesAsync(Guid id, [AsParameters] PageParameters paging, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetPlayerFilesQuery(id, paging.ToRequest()), cancellationToken));

    /// <summary>Edits one file from the edit-video sheet.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="fileId">The file id.</param>
    /// <param name="form">The fields to change.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the file.</returns>
    private static async Task<Ok<PlayerFileDto>> EditFileAsync(Guid id, Guid fileId, EditPlayerFileDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(form.ToCommand(id, fileId), cancellationToken));

    /// <summary>Reads the card.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the card.</returns>
    private static async Task<Ok<MiniAppPlayerDto>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMiniAppPlayerQuery(id), cancellationToken));

    /// <summary>Adds a folder.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="form">The folder link.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the linked folder.</returns>
    private static async Task<Created<DriveFolderDto>> AddFolderAsync(Guid id, AddFolderDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await sender.Send(new AddPlayerFolderCommand(id, form.DriveFolderUrl), cancellationToken));

    /// <summary>Edits the details.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="form">The fields to change.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the player.</returns>
    private static async Task<Ok<PlayerDetailsDto>> UpdateAsync(Guid id, UpdatePlayerDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(form.ToCommand(id), cancellationToken));
}
