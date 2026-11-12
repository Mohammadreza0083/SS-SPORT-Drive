using System.Text.Json;
using System.Text.RegularExpressions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Emails;

/// <summary>Which providers an email is sent through (decisions §8).</summary>
public enum EmailKind
{
    /// <summary>A gate code or sign-in link: Resend first, Brevo when Resend fails.</summary>
    Code,

    /// <summary>Every other email: Google SMTP.</summary>
    General,
}

/// <summary>Where an email is in the queue.</summary>
public enum EmailStatus
{
    /// <summary>Waiting to be sent, or waiting for a retry.</summary>
    Queued,

    /// <summary>A provider accepted it.</summary>
    Sent,

    /// <summary>Every attempt failed.</summary>
    Failed,

    /// <summary>There was nothing left to send (for example a spent gate code).</summary>
    Skipped,
}

/// <summary>Raised when an email is queued; the background job that hears it composes and sends the email.</summary>
/// <param name="EmailId">The queued email.</param>
public sealed record EmailQueued(Guid EmailId) : IDomainEvent;

/// <summary>
/// One email on the queue (L-4): a template key and safe data (never an email address, a code or a token), sent once by
/// a provider, retried up to <see cref="MaxAttempts"/> attempts in all. Admins see the queue.
/// </summary>
public sealed partial class OutgoingEmail : AggregateRoot<Guid>
{
    /// <summary>The first try plus Hangfire's three retries.</summary>
    public const int MaxAttempts = 4;

    /// <summary>The longest data JSON.</summary>
    public const int MaxDataLength = 2000;

    /// <summary>The longest stored error.</summary>
    public const int MaxErrorLength = 300;

    /// <summary>The longest template key.</summary>
    private const int MaxTemplateLength = 64;

    /// <summary>Initializes an email; use <see cref="Queue"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="template">The template key.</param>
    /// <param name="to">The address.</param>
    /// <param name="data">The data JSON, or <see langword="null"/>.</param>
    /// <param name="queuedAt">When it was queued (UTC).</param>
    private OutgoingEmail(Guid id, EmailKind kind, string template, EmailAddress to, string? data, DateTimeOffset queuedAt)
        : base(id)
    {
        Kind = kind;
        Template = template;
        To = to;
        Data = data;
        QueuedAt = queuedAt;
    }

    /// <summary>Gets the kind, which picks the providers.</summary>
    public EmailKind Kind { get; }

    /// <summary>Gets the template key, such as <c>forward.kept</c>.</summary>
    public string Template { get; }

    /// <summary>Gets the address.</summary>
    public EmailAddress To { get; }

    /// <summary>Gets the data JSON object the template is composed from, or <see langword="null"/>.</summary>
    public string? Data { get; }

    /// <summary>Gets the status.</summary>
    public EmailStatus Status { get; private set; } = EmailStatus.Queued;

    /// <summary>Gets how many attempts were made.</summary>
    public int Attempts { get; private set; }

    /// <summary>Gets the provider that accepted the email, or <see langword="null"/>.</summary>
    public string? Provider { get; private set; }

    /// <summary>Gets the type of the last failure, or <see langword="null"/>.</summary>
    public string? LastError { get; private set; }

    /// <summary>Gets when the email was queued (UTC).</summary>
    public DateTimeOffset QueuedAt { get; }

    /// <summary>Gets when the last attempt was made (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>Gets when a provider accepted the email (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Queues an email.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="template">The template key: dotted lower-case words, at most 64 characters.</param>
    /// <param name="to">The address.</param>
    /// <param name="data">A JSON object of at most 2000 characters with no email address, or <see langword="null"/>.</param>
    /// <param name="now">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The queued email.</returns>
    /// <exception cref="DomainException">The template or the data is not acceptable.</exception>
    public static OutgoingEmail Queue(EmailKind kind, string template, EmailAddress to, string? data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (string.IsNullOrWhiteSpace(template) || template.Length > MaxTemplateLength || !TemplatePattern().IsMatch(template))
        {
            throw new DomainException("email.template_invalid", "The email template key must be dotted lower-case words, such as forward.kept.");
        }

        if (data is not null)
        {
            EnsureDataIsSafe(data);
        }

        var email = new OutgoingEmail(Guid.CreateVersion7(now), kind, template, to, data, now);
        email.RaiseDomainEvent(new EmailQueued(email.Id));
        return email;
    }

    /// <summary>Records that a provider accepted the email.</summary>
    /// <param name="provider">The provider's name.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>email.not_queued</c> when the email is already closed.</exception>
    public void RecordSent(string provider, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        EnsureQueued();
        Attempts++;
        Provider = provider;
        SentAt = now;
        LastAttemptAt = now;
        Status = EmailStatus.Sent;
    }

    /// <summary>Records a failed attempt; the email fails for good after the last allowed attempt.</summary>
    /// <param name="error">The type of the failure (never a message that could hold an address).</param>
    /// <param name="now">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>email.not_queued</c> when the email is already closed.</exception>
    public void RecordFailure(string error, DateTimeOffset now)
    {
        EnsureQueued();
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        LastAttemptAt = now;
        if (Attempts >= MaxAttempts)
        {
            Status = EmailStatus.Failed;
        }
    }

    /// <summary>Closes the email without sending it: there was nothing left to send.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>email.not_queued</c> when the email is already closed.</exception>
    public void Skip(DateTimeOffset now)
    {
        EnsureQueued();
        Status = EmailStatus.Skipped;
        LastAttemptAt = now;
    }

    /// <summary>The template key pattern: at least two dotted words of lower-case letters, digits and underscores.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplatePattern();

    /// <summary>Checks that the data is a short JSON object with no email address in it.</summary>
    /// <param name="data">The data.</param>
    /// <exception cref="DomainException">The data is not acceptable.</exception>
    private static void EnsureDataIsSafe(string data)
    {
        if (data.Length > MaxDataLength)
        {
            throw new DomainException("email.data_invalid", "The email data is too long.");
        }

        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DomainException("email.data_invalid", "The email data must be a JSON object.");
            }
        }
        catch (JsonException)
        {
            throw new DomainException("email.data_invalid", "The email data must be a JSON object.");
        }

        if (data.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException("email.data_contains_email", "The email data must not contain an email address.");
        }
    }

    /// <summary>Checks that the email still waits to be sent.</summary>
    /// <exception cref="DomainException"><c>email.not_queued</c> when it is closed.</exception>
    private void EnsureQueued()
    {
        if (Status != EmailStatus.Queued)
        {
            throw new DomainException("email.not_queued", "The email is already closed.");
        }
    }
}
