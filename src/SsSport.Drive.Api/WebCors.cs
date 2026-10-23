using Microsoft.AspNetCore.Cors.Infrastructure;

namespace SsSport.Drive.Api;

/// <summary>
/// CORS lockdown: browsers may call the API cross-origin only from the SS-SPORT web front-end origins
/// listed in <c>Cors:AllowedOrigins</c>. With no origins configured, every cross-origin request is refused.
/// </summary>
public static class WebCors
{
    /// <summary>Name of the CORS policy applied to the whole API.</summary>
    public const string PolicyName = "WebApp";

    /// <summary>
    /// Configuration key of the allowed origins. Accepts an array (<c>Cors__AllowedOrigins__0</c>, ...)
    /// or a single comma-separated value (<c>Cors__AllowedOrigins=http://localhost:3000,https://drive.ss-sport.co</c>).
    /// </summary>
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    /// <summary>Adds the CORS services and the <see cref="PolicyName"/> policy built from configuration.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddWebCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IConfiguration>((options, configuration) => options.AddPolicy(PolicyName, policy => policy
                .WithOrigins(ReadAllowedOrigins(configuration))
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .AllowAnyHeader()
                .AllowCredentials()));
        return services;
    }

    /// <summary>Reads the allowed origins in either supported format.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The trimmed, non-empty origins.</returns>
    private static string[] ReadAllowedOrigins(IConfiguration configuration)
    {
        var section = configuration.GetSection(AllowedOriginsKey);
        var values = section.Value is { } single
            ? single.Split(',')
            : section.GetChildren().Select(child => child.Value ?? string.Empty);

        return values.Select(origin => origin.Trim()).Where(origin => origin.Length > 0).ToArray();
    }
}
