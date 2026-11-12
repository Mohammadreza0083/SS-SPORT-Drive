using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Security;

namespace SsSport.Drive.Infrastructure.Security;

/// <summary>The Turnstile settings (section <c>Turnstile</c>).</summary>
public sealed class TurnstileOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Turnstile";

    /// <summary>Gets or sets the secret key (<c>Turnstile__SecretKey</c>); never logged and never sent to the browser.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Gets or sets Cloudflare's verification address.</summary>
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

/// <summary>
/// Checks Turnstile tokens at Cloudflare's <c>siteverify</c> (P-8): the token, the secret and the visitor's address are
/// posted as a form; only <c>"success": true</c> passes. A missing or oversized token, a Cloudflare error and an
/// unreachable Cloudflare all fail closed. Neither the secret nor the token is logged.
/// </summary>
/// <param name="http">The HTTP client.</param>
/// <param name="options">The settings.</param>
/// <param name="logger">The logger.</param>
public sealed partial class CloudflareTurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<CloudflareTurnstileVerifier> logger) : ITurnstileVerifier
{
    /// <summary>The longest token Cloudflare accepts.</summary>
    private const int MaxTokenLength = 2048;

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var settings = options.Value;
        var form = new Dictionary<string, string> { ["secret"] = settings.SecretKey, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            form["remoteip"] = clientIp;
        }

        try
        {
            using var response = await http.PostAsync(new Uri(settings.VerifyUrl), new FormUrlEncodedContent(form), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnavailable(logger, (int)response.StatusCode);
                return false;
            }

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return body.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            LogUnavailable(logger, 0);
            return false;
        }
    }

    /// <summary>Logs that Cloudflare could not be asked or gave an answer that cannot be read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="StatusCode">The HTTP status, or 0 when there was none.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile verification failed closed: Cloudflare answered {StatusCode} or could not be reached")]
    private static partial void LogUnavailable(ILogger logger, int StatusCode);
}
