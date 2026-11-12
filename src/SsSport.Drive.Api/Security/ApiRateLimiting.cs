using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SsSport.Drive.Api.Endpoints;
using SsSport.Drive.Api.Telegram;

namespace SsSport.Drive.Api.Security;

/// <summary>The per-minute request budgets (section <c>RateLimiting</c>).</summary>
public sealed class ApiRateLimitOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per minute one client address may make to the API in general.</summary>
    public int PermitsPerMinute { get; set; } = 300;

    /// <summary>
    /// Requests per minute to the Telegram webhook, per address. Telegram delivers from a few addresses, in bursts,
    /// so it gets its own, larger budget.
    /// </summary>
    public int WebhookPermitsPerMinute { get; set; } = 1800;

    /// <summary>
    /// Requests per minute to the public link endpoints (<c>/links/*</c>: the gate, codes and sessions, L-2), per
    /// address. Tighter than the general budget so link codes and email codes cannot be guessed quickly.
    /// </summary>
    public int LinkPermitsPerMinute { get; set; } = 30;

    /// <summary>
    /// Gets or sets the per-address budget of a signed-in viewer's content reads (V-2b: <c>GET /links/{code}/content</c>
    /// and <c>/links/{code}/players/…</c>, photos included). A roster page alone reads about 20 photos, so these reads
    /// cannot share the gate's tight budget; every one of them still needs a live session token.
    /// </summary>
    public int ViewerPermitsPerMinute { get; set; } = 240;

    /// <summary>
    /// Gets or sets the per-address budget of the public site's reads (P-8: <c>/public/*</c>, the player search, the locked
    /// pages and photos). Home cards load a photo each, so it is larger than the gate's.
    /// </summary>
    public int PublicPermitsPerMinute { get; set; } = 120;

    /// <summary>
    /// Gets or sets the per-address budget of the two anti-spam writes of the public site (P-8: <c>POST /public/access-code</c>
    /// and <c>POST /public/access-requests</c>). Very small: a person asks for a code a few times at most.
    /// </summary>
    public int AccessPermitsPerMinute { get; set; } = 10;

    /// <summary>
    /// Gets or sets the per-address budget of the sign-in endpoints (<c>/auth/*</c>: magic links and sessions, A-1 and
    /// P-5), tight so links cannot be requested in bulk (proposed: 10).
    /// </summary>
    public int AuthPermitsPerMinute { get; set; } = 10;
}

/// <summary>
/// Rate limiting (decisions §1b): every client address gets a fixed per-minute budget. A request over it is refused
/// with 429, a <c>Retry-After</c> and a ProblemDetails body, and logged as a warning for the Loki alerts. Health
/// probes are never limited. Endpoints with tighter needs (OTP, magic links) add their own policies on top.
/// </summary>
public static partial class ApiRateLimiting
{
    /// <summary>The log category of refusals.</summary>
    private const string LogCategory = "SsSport.Drive.Api.Security.RateLimiting";

    /// <summary>The public link endpoints, which have their own budget.</summary>
    private const string LinksPrefix = "/links";

    /// <summary>The public site's endpoints (P-8).</summary>
    private const string PublicPrefix = "/public";

    /// <summary>The two public writes that get the tight anti-spam budget.</summary>
    private static readonly string[] _accessWrites = ["/public/access-code", "/public/access-requests"];

    /// <summary>The sign-in endpoints, which have their own budget.</summary>
    private const string AuthPrefix = "/auth";

    /// <summary>The length of one budget window.</summary>
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    /// <summary>Registers the limiter, built from <see cref="ApiRateLimitOptions"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<ApiRateLimitOptions>()
            .BindConfiguration(ApiRateLimitOptions.SectionName)
            .Validate(options => options.PermitsPerMinute > 0 && options.WebhookPermitsPerMinute > 0 && options.LinkPermitsPerMinute > 0 && options.ViewerPermitsPerMinute > 0 && options.AuthPermitsPerMinute > 0 && options.PublicPermitsPerMinute > 0 && options.AccessPermitsPerMinute > 0, "Rate limits must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<ApiRateLimitOptions>>((options, limits) =>
        {
            var budgets = limits.Value;
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => Partition(context, budgets));
            options.OnRejected = RejectAsync;
        });
        return services;
    }

    /// <summary>Picks the budget for a request: none for health probes, the webhook's own, the links' own, or the general one.</summary>
    /// <param name="context">The request.</param>
    /// <param name="budgets">The configured budgets.</param>
    /// <returns>The partition the request counts against.</returns>
    private static RateLimitPartition<string> Partition(HttpContext context, ApiRateLimitOptions budgets)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments(HealthEndpoints.RoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        var client = ClientAddress(context);
        if (path.StartsWithSegments(TelegramWebhookEndpoints.Route, StringComparison.OrdinalIgnoreCase))
        {
            return FixedWindow($"webhook:{client}", budgets.WebhookPermitsPerMinute);
        }

        if (path.StartsWithSegments(PublicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return HttpMethods.IsPost(context.Request.Method) && _accessWrites.Any(write => path.StartsWithSegments(write, StringComparison.OrdinalIgnoreCase))
                ? FixedWindow($"access:{client}", budgets.AccessPermitsPerMinute)
                : FixedWindow($"public:{client}", budgets.PublicPermitsPerMinute);
        }

        if (path.StartsWithSegments(AuthPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return FixedWindow($"auth:{client}", budgets.AuthPermitsPerMinute);
        }

        if (IsViewerContentRead(context.Request) || IsViewerTracking(context.Request))
        {
            return FixedWindow($"viewer:{client}", budgets.ViewerPermitsPerMinute);
        }

        return path.StartsWithSegments(LinksPrefix, StringComparison.OrdinalIgnoreCase)
            ? FixedWindow($"links:{client}", budgets.LinkPermitsPerMinute)
            : FixedWindow($"client:{client}", budgets.PermitsPerMinute);
    }

    /// <summary>Whether a request is a viewer's content read: a GET or HEAD of <c>/links/{code}/content…</c> or <c>/links/{code}/players…</c>.</summary>
    /// <param name="request">The request.</param>
    /// <returns>True for content reads; the gate's own endpoints and every write are not.</returns>
    private static bool IsViewerContentRead(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var segments = (request.Path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3
            && string.Equals(segments[0], "links", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(segments[2], "content", StringComparison.OrdinalIgnoreCase) || string.Equals(segments[2], "players", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a request is a viewer player's tracking call (L-5): a POST to <c>/links/{code}/players/{id}/videos/{id}/watch</c> or
    /// <c>/links/{code}/watch/…</c>. A player sends a heartbeat every 10 seconds, so these share the viewer budget, not the gate's.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>True for tracking calls.</returns>
    private static bool IsViewerTracking(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        var segments = (request.Path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3
            && string.Equals(segments[0], "links", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(segments[2], "watch", StringComparison.OrdinalIgnoreCase)
                || (segments.Length == 7 && string.Equals(segments[2], "players", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[^1], "watch", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>A fixed one-minute window with no queue: over the budget is refused at once.</summary>
    /// <param name="key">The partition key.</param>
    /// <param name="permits">Requests allowed per window.</param>
    /// <returns>The partition.</returns>
    private static RateLimitPartition<string> FixedWindow(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = _window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    /// <summary>Answers a refused request with 429, <c>Retry-After</c> and a ProblemDetails body, and logs it.</summary>
    /// <param name="context">The refusal context.</param>
    /// <param name="cancellationToken">Cancels writing the answer.</param>
    /// <returns>A task that completes when the answer is written.</returns>
    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : _window;
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
        LogRefused(logger, http.Request.Path.Value ?? string.Empty, ClientAddress(http), seconds);

        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = $"Try again in {seconds} seconds.",
            },
        });
    }

    /// <summary>The client's address (after the trusted-proxy headers are applied), or <c>unknown</c>.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The address as text.</returns>
    private static string ClientAddress(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>Logs a refused request: a possible flood or brute-force attempt.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Path">The requested path.</param>
    /// <param name="ClientIp">The client address.</param>
    /// <param name="RetryAfterSeconds">When the client may try again.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded on {Path} for client {ClientIp}; retry after {RetryAfterSeconds}s")]
    private static partial void LogRefused(ILogger logger, string Path, string ClientIp, int RetryAfterSeconds);
}
