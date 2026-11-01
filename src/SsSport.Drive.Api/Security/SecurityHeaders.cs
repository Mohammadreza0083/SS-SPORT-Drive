namespace SsSport.Drive.Api.Security;

/// <summary>
/// Adds the security headers to every response (decisions §1b), including errors and rate-limit refusals, because
/// they are written as the response starts. Outside development, HTTPS answers also carry HSTS. The API only serves JSON, so its CSP lets a page load nothing;
/// the Scalar reference (never served in production) is left without a CSP so its page can run.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>The CSP for every API response: nothing may load, frame it, or be posted from it.</summary>
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>Browser features the API never needs.</summary>
    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";

    /// <summary>HSTS: HTTPS only for a year, on every subdomain.</summary>
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    /// <summary>The path of the Scalar reference, which is exempt from the API CSP.</summary>
    private const string ScalarPath = "/scalar";

    /// <summary>
    /// Adds the header middleware. The headers are written when the response starts, so they survive the exception
    /// handler, which clears the headers of a failed response before writing its ProblemDetails.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        var hsts = !app.Environment.IsDevelopment();
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                Apply(context, hsts);
                return Task.CompletedTask;
            });
            return next(context);
        });
        return app;
    }

    /// <summary>Writes the headers onto the response.</summary>
    /// <param name="context">The request whose response is starting.</param>
    /// <param name="hsts">Whether HTTPS answers get HSTS (everywhere but development).</param>
    private static void Apply(HttpContext context, bool hsts)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        if (hsts && context.Request.IsHttps)
        {
            headers.StrictTransportSecurity = StrictTransportSecurity;
        }

        if (!context.Request.Path.StartsWithSegments(ScalarPath, StringComparison.OrdinalIgnoreCase))
        {
            headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
        }
    }
}
