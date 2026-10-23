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
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = Title,
                Version = "v1",
                Description = "Backend of SS-SPORT Drive: branded, trackable volleyball player pages built from Google Drive folders. " +
                    "Errors are returned as RFC 9457 ProblemDetails; broken business rules use status 422 with a stable `code`.",
                Contact = new OpenApiContact { Name = "SS-SPORT", Email = "info@ss-sport.co" },
            };
            return Task.CompletedTask;
        }));

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
