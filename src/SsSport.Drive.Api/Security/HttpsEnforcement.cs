using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using SsSport.Drive.Api.Endpoints;

namespace SsSport.Drive.Api.Security;

/// <summary>
/// HTTPS only (decisions §1b). Outside development, plain HTTP is redirected (308) to HTTPS; HTTPS answers carry
/// HSTS (written by <see cref="SecurityHeaders"/>). TLS ends at the reverse proxy, so the client's scheme and address are read
/// from <c>X-Forwarded-Proto</c>/<c>X-Forwarded-For</c>, but only when the connection comes from a proxy listed in
/// <see cref="TrustedProxiesKey"/>. Health probes stay on plain HTTP inside the network.
/// </summary>
public static class HttpsEnforcement
{
    /// <summary>
    /// Configuration key of the trusted reverse proxies: addresses or CIDR ranges, as an array or one comma-separated
    /// value (<c>Security__TrustedProxies=172.18.0.0/16</c>). Loopback is always trusted.
    /// </summary>
    public const string TrustedProxiesKey = "Security:TrustedProxies";

    /// <summary>Registers the forwarded-headers and HTTPS redirection settings.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHttpsEnforcement(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var entry in ConfigurationList.Read(configuration, TrustedProxiesKey))
            {
                if (entry.Contains('/', StringComparison.Ordinal))
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
                }
                else
                {
                    options.KnownProxies.Add(IPAddress.Parse(entry));
                }
            }
        });

        // Behind the proxy the public HTTPS port is always 443.
        services.AddHttpsRedirection(options =>
        {
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
            options.HttpsPort = 443;
        });
        return services;
    }

    /// <summary>
    /// Applies the forwarded headers and, outside development, the redirect. Call it before anything that
    /// reads the scheme or the client address.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseHttpsEnforcement(this WebApplication app)
    {
        app.UseForwardedHeaders();
        if (app.Environment.IsDevelopment())
        {
            return app;
        }

        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments(HealthEndpoints.RoutePrefix, StringComparison.OrdinalIgnoreCase),
            branch => branch.UseHttpsRedirection());
        return app;
    }
}
