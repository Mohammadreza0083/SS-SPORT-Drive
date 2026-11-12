using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Abstractions.Email;

/// <summary>One email ready to send.</summary>
/// <param name="To">The address.</param>
/// <param name="Subject">The subject; never holds a code or a token.</param>
/// <param name="Text">The plain-text body.</param>
/// <param name="Html">The HTML body, with every value encoded.</param>
public sealed record EmailMessage(EmailAddress To, string Subject, string Text, string Html);

/// <summary>One way of sending an email: Resend, Brevo, Google SMTP or the development pickup folder (L-4).</summary>
public interface IEmailTransport
{
    /// <summary>Gets the provider's name, which the dispatcher routes by and the queue shows.</summary>
    string Name { get; }

    /// <summary>Gets a value indicating whether the provider has its settings.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends one message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the sending.</param>
    /// <returns>A task that completes when the provider accepted the message.</returns>
    /// <exception cref="Exception">The provider refused or could not be reached.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Chooses the providers for an email and fails over between them (decisions §8).</summary>
public interface IEmailDispatcher
{
    /// <summary>Sends a message through the providers of its kind.</summary>
    /// <param name="kind">Codes go Resend then Brevo; everything else goes through Google SMTP.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the sending.</param>
    /// <returns>The name of the provider that accepted it.</returns>
    /// <exception cref="InvalidOperationException">No provider is configured, or every one failed.</exception>
    Task<string> SendAsync(EmailKind kind, EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Builds the message of one template from a queued email.</summary>
public interface IEmailComposer
{
    /// <summary>Gets the template key this composer builds, such as <c>forward.kept</c>.</summary>
    string Template { get; }

    /// <summary>Composes the message; it may draw a code or token and store its hash first.</summary>
    /// <param name="email">The queued email.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The message, or <see langword="null"/> when there is nothing left to send.</returns>
    Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken);
}
