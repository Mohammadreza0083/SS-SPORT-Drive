using System.Text.Json.Nodes;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SsSport.Drive.Api.Authentication;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Players;

namespace SsSport.Drive.Api.MiniApp;

/// <summary>Endpoints the Telegram Mini App calls, all authenticated with Telegram-signed initData (T-3).</summary>
public static class MiniAppEndpoints
{
    /// <summary>The OpenAPI tag of the Mini App endpoints.</summary>
    public const string Tag = "Mini App";

    /// <summary>A realistic profile for the API reference.</summary>
    private const string ExampleProfile =
        """
        {
          "id": "0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27",
          "telegramId": 123456789,
          "displayName": "Reza Ahmadi",
          "username": "reza_analyst",
          "role": "Analyst"
        }
        """;

    /// <summary>A shortened country list for the API reference.</summary>
    private const string ExampleCountries =
        """
        [
          { "code": "IR", "name": "Iran" },
          { "code": "IT", "name": "Italy" },
          { "code": "PL", "name": "Poland" }
        ]
        """;

    /// <summary>A Volleybox check for a new player.</summary>
    private const string ExampleVolleybox =
        """
        {
          "volleyboxId": 48213,
          "url": "https://volleybox.net/arash-tehrani-p48213",
          "existingPlayerId": null,
          "existingPlayerName": null
        }
        """;

    /// <summary>A filled new-player form.</summary>
    private const string ExampleNewPlayer =
        """
        {
          "category": "Men",
          "name": "Arash Tehrani",
          "nationalityCode": "IR",
          "positions": ["OH", "OPP"],
          "volleyboxUrl": "https://volleybox.net/arash-tehrani-p48213",
          "driveFolderUrl": "https://drive.google.com/drive/folders/1AbC-dEf_GhIjKlMnOp"
        }
        """;

    /// <summary>The player created from <see cref="ExampleNewPlayer"/>.</summary>
    private const string ExampleCreatedPlayer =
        """
        {
          "id": "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e",
          "name": "Arash Tehrani",
          "slug": "arash-tehrani",
          "category": "Men",
          "nationalityCode": "IR",
          "nationalityName": "Iran",
          "positions": ["OH", "OPP"],
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

    /// <summary>Maps the Mini App endpoints under the "Mini App" tag.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapMiniApp(this IEndpointRouteBuilder app)
    {
        app.MapGet("/miniapp/me", GetProfileAsync)
            .WithTags(Tag)
            .WithName("GetMiniAppProfile")
            .WithSummary("Get the signed-in team member")
            .WithDescription(
                "Who is using the Mini App: id, Telegram id, display name, username and role (`Analyst` or `Admin`). " +
                $"Send the Mini App's `Telegram.WebApp.initData` as `Authorization: {TelegramMiniAppAuthenticationHandler.HeaderScheme} <initData>`. " +
                "It must carry Telegram's signature for our bot and be at most 24 hours old, otherwise the answer is 401. " +
                "A real Telegram user who is not an active analyst or admin (unknown, pending, declined or removed) gets 403. " +
                "The profile is read from our database, not from the initData.")
            .Produces<MiniAppProfileDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(Document("200", ExampleProfile));

        app.MapGet("/miniapp/countries", ListCountriesAsync)
            .WithTags(Tag)
            .WithName("ListMiniAppCountries")
            .WithSummary("List the countries for the nationality picker")
            .WithDescription(
                "The fixed catalogue of 245 countries (ISO 3166-1 alpha-2 plus XK, AC and TA) with English names, in catalogue order. " +
                "It is small and never grows, so it is returned whole and the picker searches it on the device. " +
                "Needs Mini App sign-in as an active analyst or admin.")
            .Produces<IReadOnlyList<CountryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(Document("200", ExampleCountries));

        app.MapGet("/miniapp/volleybox", CheckVolleyboxAsync)
            .WithTags(Tag)
            .WithName("CheckMiniAppVolleybox")
            .WithSummary("Check a pasted Volleybox link")
            .WithDescription(
                "Parses the Volleybox player id from `url` (never scraping the page) and returns the normalised URL. " +
                "When a player already has that id, `existingPlayerId` and `existingPlayerName` name them, so the form can switch to updating that player. " +
                "A team page is refused with 422 `player.volleybox_not_a_player`, anything else that is not a Volleybox player page with 422 `player.volleybox_url_invalid`.")
            .Produces<VolleyboxCheckDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(Document("200", ExampleVolleybox));

        app.MapPost("/miniapp/players", CreatePlayerAsync)
            .WithTags(Tag)
            .WithName("CreateMiniAppPlayer")
            .WithSummary("Create a player from the new-player form")
            .WithDescription(
                "Creates a player on the admin waiting list, owned by the signed-in analyst, and links the Drive folder. An admin publishes it later. " +
                "Missing or unreadable fields are a 400 with errors per field. Broken rules are a 422 with a stable `code`: " +
                "`player.name_invalid_characters`, `player.name_too_long`, `player.nationality_invalid`, `player.duplicate_position`, " +
                "`player.volleybox_url_invalid`, `player.volleybox_not_a_player`, `player.volleybox_taken`, `drive_folder.link_invalid`, `drive_folder.taken`. " +
                "Nothing is saved when any check fails. Until the photo upload exists (T-4b), `missingDetails` still lists `Photo`.")
            .Accepts<NewPlayerDto>("application/json")
            .Produces<PlayerDetailsDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(Document("201", ExampleCreatedPlayer, ExampleNewPlayer));

        return app;
    }

    /// <summary>
    /// Builds the OpenAPI transformer every Mini App endpoint shares: the initData security requirement, a success
    /// example and, for a body, a request example.
    /// </summary>
    /// <param name="status">The success status code.</param>
    /// <param name="responseExample">The success example.</param>
    /// <param name="requestExample">The request body example, or <see langword="null"/>.</param>
    /// <returns>The transformer.</returns>
    private static Func<OpenApiOperation, OpenApiOperationTransformerContext, CancellationToken, Task> Document(
        string status, string responseExample, string? requestExample = null) =>
        (operation, context, _) =>
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(TelegramMiniAppAuthenticationHandler.SchemeName, context.Document)] = [] },
            ];
            if (operation.Responses?.TryGetValue(status, out var success) == true && success.Content?.TryGetValue("application/json", out var media) == true)
            {
                media.Example = JsonNode.Parse(responseExample);
            }

            if (requestExample is not null && operation.RequestBody?.Content?.TryGetValue("application/json", out var body) == true)
            {
                body.Example = JsonNode.Parse(requestExample);
            }

            return Task.CompletedTask;
        };

    /// <summary>Reads the caller's profile.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile; 401/403 come from the pipeline as ProblemDetails.</returns>
    private static async Task<Ok<MiniAppProfileDto>> GetProfileAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMiniAppProfileQuery(), cancellationToken));

    /// <summary>Lists the countries.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the catalogue.</returns>
    private static async Task<Ok<IReadOnlyList<CountryDto>>> ListCountriesAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ListCountriesQuery(), cancellationToken));

    /// <summary>Checks a Volleybox link.</summary>
    /// <param name="url">The pasted link.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the parsed id; a broken link becomes a 422 ProblemDetails.</returns>
    private static async Task<Ok<VolleyboxCheckDto>> CheckVolleyboxAsync(string? url, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new CheckVolleyboxQuery(url), cancellationToken));

    /// <summary>Creates a player from the form.</summary>
    /// <param name="form">The form.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the new player.</returns>
    private static async Task<Created<PlayerDetailsDto>> CreatePlayerAsync(NewPlayerDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await sender.Send(form.ToCommand(), cancellationToken));
}
