using System.Text.Json.Nodes;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.OpenApi;
using SsSport.Drive.Api.Authentication;
using SsSport.Drive.Application.MiniApp;

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
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                operation.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(TelegramMiniAppAuthenticationHandler.SchemeName, context.Document)] = [] },
                ];
                if (operation.Responses?.TryGetValue("200", out var ok) == true && ok.Content?.TryGetValue("application/json", out var media) == true)
                {
                    media.Example = JsonNode.Parse(ExampleProfile);
                }

                return Task.CompletedTask;
            });

        return app;
    }

    /// <summary>Reads the caller's profile.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile; 401/403 come from the pipeline as ProblemDetails.</returns>
    private static async Task<Ok<MiniAppProfileDto>> GetProfileAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMiniAppProfileQuery(), cancellationToken));
}
