using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Api.Authentication;

/// <summary>
/// Signs in Mini App callers (T-3) from <c>Authorization: tma &lt;initData&gt;</c>. The initData must carry Telegram's
/// signature under our bot token and be at most 24 hours old (<see cref="MiniAppInitData"/>); nothing is stored, every
/// request proves itself. A verified caller always gets the <see cref="TelegramIdClaim"/>; only an active team member
/// also gets their user id and role, so everyone else is refused with 403 by the role checks.
/// </summary>
/// <param name="options">The scheme options.</param>
/// <param name="logger">The logger factory.</param>
/// <param name="encoder">The URL encoder.</param>
/// <param name="telegram">The Telegram settings (bot token).</param>
/// <param name="sender">The MediatR sender, to find the team member.</param>
public sealed partial class TelegramMiniAppAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<TelegramOptions> telegram,
    ISender sender)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The authentication scheme name, also the OpenAPI security scheme.</summary>
    public const string SchemeName = "TelegramMiniApp";

    /// <summary>The Authorization header scheme the Mini App sends initData with.</summary>
    public const string HeaderScheme = "tma";

    /// <summary>The claim holding the verified Telegram user id.</summary>
    public const string TelegramIdClaim = "telegram_id";

    /// <summary>Verifies the initData and builds the caller.</summary>
    /// <returns>No result without a <c>tma</c> header, a failure for untrusted data, otherwise the signed-in caller.</returns>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadInitData(Request.Headers.Authorization.ToString(), out var initData))
        {
            return AuthenticateResult.NoResult();
        }

        var result = MiniAppInitData.Validate(initData, telegram.Value.BotToken, TimeProvider.GetUtcNow());
        if (!result.IsValid)
        {
            LogRejected(Logger, result.Error!.Value);
            return AuthenticateResult.Fail("Invalid Telegram Mini App initData.");
        }

        var telegramId = result.User!.Id;
        var claims = new List<Claim> { new(TelegramIdClaim, telegramId.ToString(CultureInfo.InvariantCulture)) };
        var member = await sender.Send(new GetTeamMemberByTelegramIdQuery(telegramId), Context.RequestAborted);
        if (member is { IsActive: true })
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, member.Id.ToString()));
            claims.Add(new Claim(ClaimTypes.Role, member.Role));
        }
        else
        {
            LogNotActive(Logger, telegramId);
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    /// <summary>Reads the initData from an Authorization header of the form <c>tma &lt;initData&gt;</c>.</summary>
    /// <param name="header">The header value.</param>
    /// <param name="initData">The initData.</param>
    /// <returns><see langword="true"/> when the header uses the <c>tma</c> scheme.</returns>
    private static bool TryReadInitData(string header, out string initData)
    {
        initData = string.Empty;
        if (header.Length <= HeaderScheme.Length || !header.StartsWith(HeaderScheme + " ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        initData = header[(HeaderScheme.Length + 1)..].Trim();
        return true;
    }

    /// <summary>Logs rejected initData (the reason only; the data and its hash are never logged).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Reason">Why it was rejected.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected Telegram Mini App initData: {Reason}")]
    private static partial void LogRejected(ILogger logger, MiniAppInitDataError Reason);

    /// <summary>Logs a verified Telegram user who is not an active team member.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="TelegramId">The Telegram user id.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram user {TelegramId} opened the Mini App without active team access")]
    private static partial void LogNotActive(ILogger logger, long TelegramId);
}
