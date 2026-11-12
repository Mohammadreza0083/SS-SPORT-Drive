using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Accounts;

namespace SsSport.Drive.Api.Authentication;

/// <summary>
/// Signs a request in from an admin console session (A-1): the web app sends the session token it keeps in its
/// <c>__Host-</c> cookie in the <c>X-Admin-Session</c> header (the API never reads cookies). A live session of a user who
/// is still an active admin gets the same claims the Mini App signature gives, so every <c>/admin/*</c> endpoint works with
/// either. A missing, unknown, ended or expired session, or a viewer's session, leaves the request anonymous.
/// </summary>
/// <param name="options">The scheme options.</param>
/// <param name="logger">The logger factory.</param>
/// <param name="encoder">The URL encoder.</param>
/// <param name="sender">The MediatR sender.</param>
public sealed class AdminSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISender sender)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme name.</summary>
    public const string SchemeName = "AdminSession";

    /// <summary>The header that carries the admin session token.</summary>
    public const string HeaderName = "X-Admin-Session";

    /// <summary>Reads the session header and resolves the admin.</summary>
    /// <returns>Success with the admin's claims, or a failure.</returns>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers[HeaderName].ToString();
        var admin = await sender.Send(new ResolveAdminSessionQuery(token), Context.RequestAborted);
        if (admin is null)
        {
            return AuthenticateResult.Fail("No live admin session.");
        }

        var claims = new List<Claim>
        {
            new(TelegramMiniAppAuthenticationHandler.TelegramIdClaim, admin.TelegramId.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.NameIdentifier, admin.UserId.ToString()),
            new(ClaimTypes.Role, admin.Role),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
