using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using SsSport.Drive.Application.Links;

namespace SsSport.Drive.Api.Security;

/// <summary>
/// The admin area is deny-by-default at the HTTP layer (decisions §1b, 2026-11-13): every <c>/admin/*</c> endpoint carries
/// the <see cref="PolicyName"/> policy (an authenticated caller with the Admin role), so a request is refused before any
/// handler or database work. The MediatR authorization behaviour stays as the second check. Refusals are 401/403
/// ProblemDetails and are logged as warnings (suspicious activity, no secrets).
/// </summary>
public static class AdminAuthorization
{
    /// <summary>The policy name.</summary>
    public const string PolicyName = "Admin";

    /// <summary>Registers the Admin policy and the ProblemDetails refusal handler.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAdminAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy => policy.RequireAuthenticatedUser().RequireRole(ShareLinkRules.AdminRoles));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        return services;
    }

    /// <summary>Requires the Admin policy on an endpoint.</summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) => builder.RequireAuthorization(PolicyName);
}

/// <summary>
/// Answers authorization refusals with ProblemDetails (401 not signed in, 403 not allowed) instead of an empty body, and
/// logs each refusal as a warning with the path and the caller id only.
/// </summary>
/// <param name="problemDetails">Writes ProblemDetails responses.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ProblemAuthorizationResultHandler(IProblemDetailsService problemDetails, ILogger<ProblemAuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The default handler, used when the request is allowed.</summary>
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <summary>Writes a 401/403 ProblemDetails for a refusal, or lets an allowed request through.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="policy">The evaluated policy.</param>
    /// <param name="authorizeResult">The authorization result.</param>
    /// <returns>A task that completes when the response is handled.</returns>
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Challenged && !authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var status = authorizeResult.Challenged ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
        LogRefused(logger, context.Request.Method, context.Request.Path, status, context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        context.Response.StatusCode = status;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = status,
                Title = status == StatusCodes.Status401Unauthorized ? "Sign in first." : "You are not allowed to do this.",
            },
        });
    }

    /// <summary>Logs a refused call to a protected endpoint.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The request path.</param>
    /// <param name="statusCode">The answered status.</param>
    /// <param name="userId">The caller's user id, or <see langword="null"/> when anonymous.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused {Method} {Path} with {StatusCode} for user {UserId}")]
    private static partial void LogRefused(ILogger logger, string method, string path, int statusCode, string? userId);
}
