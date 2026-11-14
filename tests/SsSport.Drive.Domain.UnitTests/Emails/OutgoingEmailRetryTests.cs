using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Emails;

/// <summary>
/// An admin retries a failed email from the Jobs &amp; email queue page (A-9): only a failed email can be retried; it goes
/// back on the queue with a fresh set of attempts and raises the event that sends it again.
/// </summary>
public sealed class OutgoingEmailRetryTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The address.</summary>
    private static readonly EmailAddress _to = EmailAddress.Create("scout@other.com");

    /// <summary>A failed email goes back on the queue with no attempts and raises <see cref="EmailQueued"/> again.</summary>
    [Fact]
    public void Retry_requeues_a_failed_email()
    {
        var email = FailedEmail();

        email.Retry(_now.AddHours(1));

        email.Status.Should().Be(EmailStatus.Queued);
        email.Attempts.Should().Be(0);
        email.LastError.Should().Be("SmtpException", "the last error stays visible until the next attempt");
        email.DomainEvents.Should().ContainSingle().Which.Should().Be(new EmailQueued(email.Id));
    }

    /// <summary>A sent, skipped or still-queued email cannot be retried.</summary>
    [Fact]
    public void Only_a_failed_email_can_be_retried()
    {
        var queued = OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, null, _now);
        var sent = OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, null, _now);
        sent.RecordSent("GoogleSmtp", _now);
        var skipped = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);
        skipped.Skip(_now);

        foreach (var email in new[] { queued, sent, skipped })
        {
            var retry = () => email.Retry(_now);
            retry.Should().Throw<DomainException>().Which.Code.Should().Be("email.not_failed");
        }
    }

    /// <summary>A retried email can fail again only after a full new set of attempts.</summary>
    [Fact]
    public void A_retried_email_gets_a_full_new_set_of_attempts()
    {
        var email = FailedEmail();
        email.Retry(_now);

        for (var attempt = 1; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            email.RecordFailure("SmtpException", _now);
            email.Status.Should().Be(EmailStatus.Queued);
        }

        email.RecordFailure("SmtpException", _now);
        email.Status.Should().Be(EmailStatus.Failed);
    }

    /// <summary>An email that used up every attempt.</summary>
    /// <returns>The failed email, with its events cleared.</returns>
    private static OutgoingEmail FailedEmail()
    {
        var email = OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, null, _now);
        for (var attempt = 0; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            email.RecordFailure("SmtpException", _now);
        }

        email.Status.Should().Be(EmailStatus.Failed);
        email.ClearDomainEvents();
        return email;
    }
}
