using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Infrastructure.Email;

/// <summary>One email provider's settings (<c>Email__Resend__*</c>, <c>Email__Brevo__*</c>).</summary>
public sealed record EmailProviderOptions
{
    /// <summary>Gets the API key; empty turns the provider off. Never logged.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Gets the sender's display name.</summary>
    public string SenderName { get; init; } = "SS-SPORT";

    /// <summary>Gets the sender's address on the verified <c>ss-sport.co</c> domain.</summary>
    public string SenderEmail { get; init; } = "info@ss-sport.co";

    /// <summary>Gets whether the provider can be used.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>The <c>Email</c> configuration section (decisions §8).</summary>
public sealed record EmailOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Email";

    /// <summary>Gets Resend, the primary provider for codes.</summary>
    public EmailProviderOptions Resend { get; init; } = new();

    /// <summary>Gets Brevo, the fallback for codes.</summary>
    public EmailProviderOptions Brevo { get; init; } = new();

    /// <summary>
    /// Gets a folder that receives every mail as an <c>.eml</c> file instead of sending it: local development without
    /// provider keys. Refused in production.
    /// </summary>
    public string PickupDirectory { get; init; } = string.Empty;
}

/// <summary>The text of the code email (English, decisions §1).</summary>
internal static class EmailCodeMessage
{
    /// <summary>The subject; the code stays out of it, so it does not show on lock screens.</summary>
    public const string Subject = "Your SS-SPORT Drive code";

    /// <summary>The plain-text body.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The text.</returns>
    public static string Text(string code) =>
        $"Your SS-SPORT Drive code is {code}.\n\nIt works for 10 minutes. If you did not ask for it, you can ignore this email.\n\nSS-SPORT · Fetch Your Dreams";

    /// <summary>The HTML body.</summary>
    /// <param name="code">The code (digits only, so nothing needs encoding).</param>
    /// <returns>The HTML.</returns>
    public static string Html(string code) =>
        "<div style=\"font-family:Arial,sans-serif;color:#111;max-width:480px\">" +
        "<p>Your SS-SPORT Drive code is</p>" +
        $"<p style=\"font-size:32px;font-weight:700;letter-spacing:8px;margin:16px 0\">{code}</p>" +
        "<p>It works for 10 minutes. If you did not ask for it, you can ignore this email.</p>" +
        "<p style=\"color:#888\">SS-SPORT · Fetch Your Dreams</p></div>";
}

/// <summary>
/// Mails gate codes (L-2): Resend first, Brevo when Resend fails or has no key (decisions §8); with a pickup directory
/// it writes an <c>.eml</c> file instead. The code is never logged.
/// </summary>
/// <param name="options">The email settings.</param>
/// <param name="http">The HTTP client for both providers.</param>
/// <param name="logger">The logger.</param>
public sealed partial class EmailCodeSender(IOptions<EmailOptions> options, HttpClient http, ILogger<EmailCodeSender> logger) : IEmailCodeSender
{
    /// <summary>Resend's send endpoint.</summary>
    private static readonly Uri _resendUrl = new("https://api.resend.com/emails");

    /// <summary>Brevo's transactional send endpoint.</summary>
    private static readonly Uri _brevoUrl = new("https://api.brevo.com/v3/smtp/email");

    /// <inheritdoc />
    public async Task SendAsync(EmailAddress to, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var settings = options.Value;
        if (!string.IsNullOrWhiteSpace(settings.PickupDirectory))
        {
            await WritePickupAsync(settings.PickupDirectory, to, code, cancellationToken);
            return;
        }

        var providers = new List<(string Name, Func<Task<HttpResponseMessage>> Send)>();
        if (settings.Resend.IsConfigured)
        {
            providers.Add(("Resend", () => SendResendAsync(settings.Resend, to, code, cancellationToken)));
        }

        if (settings.Brevo.IsConfigured)
        {
            providers.Add(("Brevo", () => SendBrevoAsync(settings.Brevo, to, code, cancellationToken)));
        }

        if (providers.Count == 0)
        {
            throw new InvalidOperationException("No email provider is configured: set Email__Resend__ApiKey, Email__Brevo__ApiKey or Email__PickupDirectory.");
        }

        foreach (var (name, send) in providers)
        {
            try
            {
                using var response = await send();
                if (response.IsSuccessStatusCode)
                {
                    LogSent(logger, name, to.ToString());
                    return;
                }

                LogProviderFailed(logger, name, (int)response.StatusCode);
            }
            catch (HttpRequestException exception)
            {
                LogProviderUnreachable(logger, exception, name);
            }
        }

        throw new InvalidOperationException("Every email provider failed to send the code.");
    }

    /// <summary>Sends through Resend.</summary>
    /// <param name="provider">Resend's settings.</param>
    /// <param name="to">The address.</param>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Resend's answer.</returns>
    private Task<HttpResponseMessage> SendResendAsync(EmailProviderOptions provider, EmailAddress to, string code, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _resendUrl)
        {
            Content = JsonContent.Create(new
            {
                from = $"{provider.SenderName} <{provider.SenderEmail}>",
                to = new[] { to.Value },
                subject = EmailCodeMessage.Subject,
                text = EmailCodeMessage.Text(code),
                html = EmailCodeMessage.Html(code),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        return http.SendAsync(request, cancellationToken);
    }

    /// <summary>Sends through Brevo.</summary>
    /// <param name="provider">Brevo's settings.</param>
    /// <param name="to">The address.</param>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Brevo's answer.</returns>
    private Task<HttpResponseMessage> SendBrevoAsync(EmailProviderOptions provider, EmailAddress to, string code, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _brevoUrl)
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = provider.SenderName, email = provider.SenderEmail },
                to = new[] { new { email = to.Value } },
                subject = EmailCodeMessage.Subject,
                textContent = EmailCodeMessage.Text(code),
                htmlContent = EmailCodeMessage.Html(code),
            }),
        };
        request.Headers.Add("api-key", provider.ApiKey);
        return http.SendAsync(request, cancellationToken);
    }

    /// <summary>Writes the mail as an <c>.eml</c> file (local development).</summary>
    /// <param name="directory">The pickup directory.</param>
    /// <param name="to">The address.</param>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private async Task WritePickupAsync(string directory, EmailAddress to, string code, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var mail = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"From: {options.Value.Resend.SenderName} <{options.Value.Resend.SenderEmail}>\r\n")
            .Append(CultureInfo.InvariantCulture, $"To: {to.Value}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Subject: {EmailCodeMessage.Subject}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Date: {DateTimeOffset.UtcNow:R}\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n\r\n")
            .Append(EmailCodeMessage.Text(code).Replace("\n", "\r\n", StringComparison.Ordinal))
            .ToString();
        var path = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
        await File.WriteAllTextAsync(path, mail, cancellationToken);
        LogSent(logger, "PickupDirectory", to.ToString());
    }

    /// <summary>Logs a mail handed to a provider; the address is masked.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Provider">The provider.</param>
    /// <param name="Recipient">The masked address.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Gate code email accepted by {Provider} for {Recipient}")]
    private static partial void LogSent(ILogger logger, string Provider, string Recipient);

    /// <summary>Logs a provider that refused the mail; the next one is tried.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Provider">The provider.</param>
    /// <param name="StatusCode">Its HTTP status.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Email provider {Provider} refused the gate code email with {StatusCode}; trying the next one")]
    private static partial void LogProviderFailed(ILogger logger, string Provider, int StatusCode);

    /// <summary>Logs a provider that could not be reached; the next one is tried.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="Provider">The provider.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Email provider {Provider} could not be reached; trying the next one")]
    private static partial void LogProviderUnreachable(ILogger logger, Exception exception, string Provider);
}
