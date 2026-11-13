namespace SsSport.Drive.Application.Abstractions.Security;

/// <summary>Checks a Cloudflare Turnstile token on the server (P-8, decisions §6 "Anti-spam").</summary>
public interface ITurnstileVerifier
{
    /// <summary>Asks Cloudflare whether a token is genuine. Fails closed: any doubt is <see langword="false"/>.</summary>
    /// <param name="token">The token the widget gave the browser, or <see langword="null"/>.</param>
    /// <param name="clientIp">The visitor's address, if known.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> only when Cloudflare says the token passed.</returns>
    Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken);
}
