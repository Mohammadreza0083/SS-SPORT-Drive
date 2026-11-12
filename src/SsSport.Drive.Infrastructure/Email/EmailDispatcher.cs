using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Domain.Emails;

namespace SsSport.Drive.Infrastructure.Email;

/// <summary>
/// Chooses the providers for an email (decisions §8, L-4): codes and sign-in links go Resend first, then Brevo; every
/// other email goes through Google SMTP. In development the pickup folder takes everything. A provider that is off is
/// skipped; when every provider fails the dispatcher throws, so the queue retries.
/// </summary>
/// <param name="transports">Every registered provider, in any order.</param>
/// <param name="logger">The logger.</param>
public sealed partial class EmailDispatcher(IEnumerable<IEmailTransport> transports, ILogger<EmailDispatcher> logger) : IEmailDispatcher
{
    /// <summary>The development pickup folder's provider name.</summary>
    private const string PickupName = "PickupDirectory";

    /// <summary>The providers a code goes through, in order.</summary>
    private static readonly string[] _codeProviders = ["Resend", "Brevo"];

    /// <summary>The providers every other email goes through, in order.</summary>
    private static readonly string[] _generalProviders = ["GoogleSmtp"];

    /// <inheritdoc />
    public async Task<string> SendAsync(EmailKind kind, EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var all = transports.ToList();
        var route = all.Where(item => item.Name == PickupName && item.IsConfigured).ToList();
        if (route.Count == 0)
        {
            route = [.. (kind == EmailKind.Code ? _codeProviders : _generalProviders)
                .Select(name => all.FirstOrDefault(item => item.Name == name))
                .OfType<IEmailTransport>()
                .Where(item => item.IsConfigured)];
        }

        if (route.Count == 0)
        {
            throw new InvalidOperationException(
                "No email provider is configured: set Email__Resend__ApiKey, Email__Brevo__ApiKey, Email__GoogleSmtp__AppPassword or Email__PickupDirectory.");
        }

        foreach (var transport in route)
        {
            try
            {
                await transport.SendAsync(message, cancellationToken);
                LogSent(logger, transport.Name, message.To.ToString());
                return transport.Name;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogProviderFailed(logger, transport.Name, exception.GetType().Name);
            }
        }

        throw new InvalidOperationException("Every email provider failed to send the email.");
    }

    /// <summary>Logs a mail a provider accepted; the address is masked.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Provider">The provider.</param>
    /// <param name="Recipient">The masked address.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Email accepted by {Provider} for {Recipient}")]
    private static partial void LogSent(ILogger logger, string Provider, string Recipient);

    /// <summary>Logs a provider that failed; the next one is tried.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Provider">The provider.</param>
    /// <param name="ErrorType">The failure's type.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Email provider {Provider} failed with {ErrorType}; trying the next one")]
    private static partial void LogProviderFailed(ILogger logger, string Provider, string ErrorType);
}
