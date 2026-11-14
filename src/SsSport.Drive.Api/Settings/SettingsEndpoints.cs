using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Application.Settings;

namespace SsSport.Drive.Api.Settings;

/// <summary>The console's global settings (A-10): contact email, digest time, quiet hours and the default link expiry.</summary>
public static class SettingsEndpoints
{
    /// <summary>The OpenAPI tag.</summary>
    public const string Tag = "Admin settings";

    /// <summary>A realistic settings answer.</summary>
    private const string ExampleSettings =
        """
        {
          "contactEmail": "info@ss-sport.co",
          "digestAt": "09:00",
          "quietHoursStart": "23:00",
          "quietHoursEnd": "08:00",
          "defaultLinkExpiryDays": 30,
          "linkExpiryChoices": [7, 30, 90],
          "timeZone": "Asia/Tehran",
          "updatedAt": "2026-11-14T09:00:00+00:00"
        }
        """;

    /// <summary>A realistic settings form.</summary>
    private const string ExampleForm =
        """
        {
          "contactEmail": "info@ss-sport.co",
          "digestAt": "09:00",
          "quietHoursStart": "23:00",
          "quietHoursEnd": "08:00",
          "defaultLinkExpiryDays": 30
        }
        """;

    /// <summary>Maps the settings endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/settings", GetAsync)
            .WithTags(Tag)
            .WithName("GetSettings")
            .RequireAdmin()
            .WithSummary("Read the settings")
            .WithDescription(
                "Admins only. The contact email shown on every player page, the daily digest time and the quiet hours (Tehran wall clock), and the " +
                "default expiry the New share link modal preselects (7, 30 or 90 days). On a fresh database these are the configured first values.")
            .Produces<SettingsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleSettings));

        app.MapPut("/admin/settings", UpdateAsync)
            .WithTags(Tag)
            .WithName("UpdateSettings")
            .RequireAdmin()
            .WithSummary("Change the settings")
            .WithDescription(
                "Admins only. Replaces all five values. A change takes effect without a restart: the bot's quiet hours, the digest's schedule and the " +
                "contact email on the player page. Audited as `settings.changed` with the names of the changed fields only. 400 per field: an email, " +
                "times as `HH:mm`, and an expiry of 7, 30 or 90 days.")
            .Accepts<UpdateSettingsDto>("application/json")
            .Produces<SettingsDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleSettings, ExampleForm));
        return app;
    }

    /// <summary>Reads the settings.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the settings.</returns>
    private static async Task<Ok<SettingsDto>> GetAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetSettingsQuery(), cancellationToken));

    /// <summary>Changes the settings.</summary>
    /// <param name="form">The new values.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the settings.</returns>
    private static async Task<Ok<SettingsDto>> UpdateAsync(UpdateSettingsDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(form.ToCommand(), cancellationToken));
}
