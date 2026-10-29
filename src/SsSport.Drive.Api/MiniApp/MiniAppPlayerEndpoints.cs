using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using SsSport.Drive.Application.DriveFolders;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Players;

namespace SsSport.Drive.Api.MiniApp;

/// <summary>
/// Update-player mode of the Mini App (T-5): read an existing player card, add a Drive folder to it, edit its details.
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
          "canChangePhoto": true
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

    /// <summary>Maps the update-mode endpoints under the "Mini App" tag.</summary>
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
