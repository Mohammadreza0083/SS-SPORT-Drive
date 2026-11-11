using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Email;

/// <summary>
/// Mails a gate code (L-2): Resend first, Brevo when Resend fails (decisions §8). It runs inside a background job, so
/// a failure makes the job retry.
/// </summary>
public interface IEmailCodeSender
{
    /// <summary>Sends the code.</summary>
    /// <param name="to">The address.</param>
    /// <param name="code">The 6-digit code; never logged.</param>
    /// <param name="cancellationToken">Cancels the sending.</param>
    /// <returns>A task that completes when a provider accepted the mail.</returns>
    /// <exception cref="InvalidOperationException">No provider accepted it, or none is configured.</exception>
    Task SendAsync(EmailAddress to, string code, CancellationToken cancellationToken);
}
