using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Email;

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

/// <summary>Google SMTP settings (<c>Email__GoogleSmtp__*</c>), used for every email that is not a code (decisions §8).</summary>
public sealed record SmtpOptions
{
    /// <summary>Gets the SMTP host.</summary>
    public string Host { get; init; } = "smtp.gmail.com";

    /// <summary>Gets the SMTP port (STARTTLS).</summary>
    public int Port { get; init; } = 587;

    /// <summary>Gets the account that signs in.</summary>
    public string UserName { get; init; } = "info@ss-sport.co";

    /// <summary>Gets the Google app password; empty turns the provider off. Never logged.</summary>
    public string AppPassword { get; init; } = string.Empty;

    /// <summary>Gets the sender's display name.</summary>
    public string SenderName { get; init; } = "SS-SPORT";

    /// <summary>Gets the sender's address.</summary>
    public string SenderEmail { get; init; } = "info@ss-sport.co";

    /// <summary>Gets whether the provider can be used.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppPassword);
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

    /// <summary>Gets Google SMTP, which sends everything that is not a code.</summary>
    public SmtpOptions GoogleSmtp { get; init; } = new();

    /// <summary>
    /// Gets a folder that receives every mail as an <c>.eml</c> file instead of sending it: local development without
    /// provider keys. Refused in production.
    /// </summary>
    public string PickupDirectory { get; init; } = string.Empty;
}

/// <summary>A provider refused an email. The message holds the provider and the HTTP status, never the address.</summary>
public sealed class EmailTransportException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">What happened, without addresses.</param>
    public EmailTransportException(string message)
        : base(message)
    {
    }
}

/// <summary>Sends through Resend's HTTP API.</summary>
/// <param name="options">The email settings.</param>
/// <param name="http">The HTTP client.</param>
public sealed class ResendTransport(IOptions<EmailOptions> options, HttpClient http) : IEmailTransport
{
    /// <summary>Resend's send endpoint.</summary>
    private static readonly Uri _url = new("https://api.resend.com/emails");

    /// <inheritdoc />
    public string Name => "Resend";

    /// <inheritdoc />
    public bool IsConfigured => options.Value.Resend.IsConfigured;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var provider = options.Value.Resend;
        using var request = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = JsonContent.Create(new
            {
                from = $"{provider.SenderName} <{provider.SenderEmail}>",
                to = new[] { message.To.Value },
                subject = message.Subject,
                text = message.Text,
                html = message.Html,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new EmailTransportException($"{Name} refused the email with {(int)response.StatusCode}.");
        }
    }
}

/// <summary>Sends through Brevo's transactional HTTP API.</summary>
/// <param name="options">The email settings.</param>
/// <param name="http">The HTTP client.</param>
public sealed class BrevoTransport(IOptions<EmailOptions> options, HttpClient http) : IEmailTransport
{
    /// <summary>Brevo's transactional send endpoint.</summary>
    private static readonly Uri _url = new("https://api.brevo.com/v3/smtp/email");

    /// <inheritdoc />
    public string Name => "Brevo";

    /// <inheritdoc />
    public bool IsConfigured => options.Value.Brevo.IsConfigured;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var provider = options.Value.Brevo;
        using var request = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = provider.SenderName, email = provider.SenderEmail },
                to = new[] { new { email = message.To.Value } },
                subject = message.Subject,
                textContent = message.Text,
                htmlContent = message.Html,
            }),
        };
        request.Headers.Add("api-key", provider.ApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new EmailTransportException($"{Name} refused the email with {(int)response.StatusCode}.");
        }
    }
}

/// <summary>Sends through Google SMTP (STARTTLS) as <c>info@ss-sport.co</c> with an app password.</summary>
/// <param name="options">The email settings.</param>
public sealed class GoogleSmtpTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    /// <inheritdoc />
    public string Name => "GoogleSmtp";

    /// <inheritdoc />
    public bool IsConfigured => options.Value.GoogleSmtp.IsConfigured;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value.GoogleSmtp;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.SenderEmail, settings.SenderName),
            Subject = message.Subject,
            Body = message.Text,
            IsBodyHtml = false,
        };
        mail.To.Add(new MailAddress(message.To.Value));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Html, Encoding.UTF8, "text/html"));
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(settings.UserName, settings.AppPassword),
            Timeout = 15_000,
        };
        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (SmtpException exception)
        {
            throw new EmailTransportException($"{Name} refused the email with {exception.StatusCode}.");
        }
    }
}

/// <summary>Writes every mail as an <c>.eml</c> file instead of sending it: local development without provider keys.</summary>
/// <param name="options">The email settings.</param>
public sealed class PickupDirectoryTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    /// <inheritdoc />
    public string Name => "PickupDirectory";

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.PickupDirectory);

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        Directory.CreateDirectory(settings.PickupDirectory);
        var mail = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"From: {settings.Resend.SenderName} <{settings.Resend.SenderEmail}>\r\n")
            .Append(CultureInfo.InvariantCulture, $"To: {message.To.Value}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Subject: {message.Subject}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Date: {DateTimeOffset.UtcNow:R}\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n\r\n")
            .Append(message.Text.Replace("\n", "\r\n", StringComparison.Ordinal))
            .ToString();
        var path = Path.Combine(settings.PickupDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
        await File.WriteAllTextAsync(path, mail, cancellationToken);
    }
}
