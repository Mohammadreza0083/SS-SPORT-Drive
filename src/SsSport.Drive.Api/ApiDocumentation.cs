using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace SsSport.Drive.Api;

/// <summary>
/// OpenAPI document (<c>/openapi/v1.json</c>) and the Scalar API reference (<c>/scalar</c>).
/// Public in local and staging; not exposed in production until admin sign-in exists to protect it.
/// </summary>
public static class ApiDocumentation
{
    /// <summary>The API title shown in the document and in Scalar.</summary>
    public const string Title = "SS-SPORT Drive API";

    /// <summary>Registers the OpenAPI document with the API's name, description and contact.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddOperationTransformer(DocumentRateLimitAsync).AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = Title,
                Version = "v1",
                Description = "Backend of SS-SPORT Drive: branded, trackable volleyball player pages built from Google Drive folders. " +
                    "Errors are returned as RFC 9457 ProblemDetails; broken business rules use status 422 with a stable `code`.",
                Contact = new OpenApiContact { Name = "SS-SPORT", Email = "info@ss-sport.co" },
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[Telegram.TelegramWebhookEndpoints.SecuritySchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = Telegram.TelegramWebhookEndpoints.SecretHeader,
                Description = "The secret token registered with Telegram's setWebhook (Telegram__WebhookSecret). Used only by Telegram.",
            };
            document.Components.SecuritySchemes[Authentication.TelegramMiniAppAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Telegram Mini App sign-in: `tma <initData>`, where initData is `Telegram.WebApp.initData` exactly as Telegram " +
                    "gave it (URL-encoded, with its hash). Valid for 24 hours after Telegram issued it.",
            };
            return Task.CompletedTask;
        }));

    /// <summary>
    /// Adds the 429 answer of the rate limiter (decisions §1b) to every operation except the health probes, which are
    /// never limited.
    /// </summary>
    /// <param name="operation">The operation being documented.</param>
    /// <param name="context">The transformer context.</param>
    /// <param name="cancellationToken">Cancels the transformation.</param>
    /// <returns>A task that completes when the response is added.</returns>
    private static async Task DocumentRateLimitAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var path = "/" + (context.Description.RelativePath ?? string.Empty);
        if (path.StartsWith(Endpoints.HealthEndpoints.RoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var problem = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
        operation.Responses ??= new OpenApiResponses();
        operation.Responses["429"] = new OpenApiResponse
        {
            Description = "Too many requests from this client address. Wait for the number of seconds in `Retry-After`, then try again.",
            Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["Retry-After"] = new OpenApiHeader
                {
                    Description = "Seconds until the client's budget is refilled.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
                    Example = System.Text.Json.Nodes.JsonValue.Create(42),
                },
            },
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType
                {
                    Schema = problem,
                    Example = System.Text.Json.Nodes.JsonNode.Parse("""{ "type": "https://tools.ietf.org/html/rfc6585#section-4", "title": "Too many requests.", "status": 429, "detail": "Try again in 42 seconds." }"""),
                },
            },
        };
    }

    /// <summary>Maps the OpenAPI document and the Scalar UI, except in production.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        if (app.Environment.IsProduction())
        {
            return app;
        }

        app.MapOpenApi();
        app.MapScalarApiReference(options => options.WithTitle(Title));
        return app;
    }
}
