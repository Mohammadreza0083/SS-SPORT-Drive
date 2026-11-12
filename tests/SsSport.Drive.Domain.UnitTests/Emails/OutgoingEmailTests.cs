using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Emails;

/// <summary>
/// One email on the queue (L-4): queued with a template and safe data (no email address, no secret), sent once by a
/// provider, retried up to four attempts in all, then failed; or skipped when there is nothing left to send.
/// </summary>
public sealed class OutgoingEmailTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The address.</summary>
    private static readonly EmailAddress _to = EmailAddress.Create("Marco@Lube.it");

    /// <summary>A queued email waits with no attempt and raises the event that sends it.</summary>
    [Fact]
    public void Queue_waits_and_raises_the_send_event()
    {
        var email = OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, """{"shareLinkId":"019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"}""", _now);

        email.Kind.Should().Be(EmailKind.General);
        email.Template.Should().Be("forward.kept");
        email.To.Value.Should().Be("marco@lube.it");
        email.Data.Should().Be("""{"shareLinkId":"019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"}""");
        email.Status.Should().Be(EmailStatus.Queued);
        email.Attempts.Should().Be(0);
        email.Provider.Should().BeNull();
        email.LastError.Should().BeNull();
        email.QueuedAt.Should().Be(_now);
        email.LastAttemptAt.Should().BeNull();
        email.SentAt.Should().BeNull();
        email.DomainEvents.Should().ContainSingle().Which.Should().Be(new EmailQueued(email.Id));
    }

    /// <summary>Data is optional.</summary>
    [Fact]
    public void Data_is_optional()
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);

        email.Data.Should().BeNull();
    }

    /// <summary>Templates are dotted lower-case keys of at most 64 characters.</summary>
    /// <param name="template">A bad template key.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("gate")]
    [InlineData("Gate.Code")]
    [InlineData("gate code")]
    [InlineData(".gate")]
    [InlineData("gate.")]
    [InlineData("gate..code")]
    [InlineData("gate.code-x")]
    public void A_bad_template_is_refused(string template)
    {
        var act = () => OutgoingEmail.Queue(EmailKind.General, template, _to, null, _now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("email.template_invalid");
    }

    /// <summary>A template longer than 64 characters is refused.</summary>
    [Fact]
    public void A_long_template_is_refused()
    {
        var act = () => OutgoingEmail.Queue(EmailKind.General, "a." + new string('b', 63), _to, null, _now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("email.template_invalid");
    }

    /// <summary>Templates may hold underscores and digits after the first letter.</summary>
    [Fact]
    public void Underscores_and_digits_are_allowed()
    {
        OutgoingEmail.Queue(EmailKind.Code, "auth.admin_sign_in2", _to, null, _now).Template.Should().Be("auth.admin_sign_in2");
    }

    /// <summary>Data that is not a JSON object, or is longer than 2000 characters, is refused.</summary>
    /// <param name="data">The bad data.</param>
    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{not json")]
    [InlineData("")]
    public void Data_must_be_a_json_object(string data)
    {
        var act = () => OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, data, _now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("email.data_invalid");
    }

    /// <summary>Data longer than the limit is refused.</summary>
    [Fact]
    public void Long_data_is_refused()
    {
        var data = $$"""{"x":"{{new string('a', OutgoingEmail.MaxDataLength)}}"}""";

        var act = () => OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, data, _now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("email.data_invalid");
    }

    /// <summary>Data never carries an email address: the queue is shown to admins and kept for weeks.</summary>
    [Fact]
    public void Data_with_an_email_is_refused()
    {
        var act = () => OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, """{"to":"marco@lube.it"}""", _now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("email.data_contains_email");
    }

    /// <summary>A sent email records the provider, the attempt and the time.</summary>
    [Fact]
    public void Sent_records_the_provider()
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);

        email.RecordSent("Resend", _now.AddSeconds(2));

        email.Status.Should().Be(EmailStatus.Sent);
        email.Attempts.Should().Be(1);
        email.Provider.Should().Be("Resend");
        email.SentAt.Should().Be(_now.AddSeconds(2));
        email.LastAttemptAt.Should().Be(_now.AddSeconds(2));
    }

    /// <summary>A failure keeps the email queued for the next try, with the error.</summary>
    [Fact]
    public void A_failure_stays_queued_for_a_retry()
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);

        email.RecordFailure("HttpRequestException", _now.AddSeconds(1));

        email.Status.Should().Be(EmailStatus.Queued);
        email.Attempts.Should().Be(1);
        email.LastError.Should().Be("HttpRequestException");
        email.LastAttemptAt.Should().Be(_now.AddSeconds(1));
        email.SentAt.Should().BeNull();
    }

    /// <summary>The fourth failure (the first try and Hangfire's three retries) fails the email for good.</summary>
    [Fact]
    public void The_last_attempt_fails_for_good()
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);

        for (var attempt = 1; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            email.RecordFailure("HttpRequestException", _now.AddMinutes(attempt));
            email.Status.Should().Be(EmailStatus.Queued);
        }

        email.RecordFailure("InvalidOperationException", _now.AddMinutes(10));

        OutgoingEmail.MaxAttempts.Should().Be(4);
        email.Status.Should().Be(EmailStatus.Failed);
        email.Attempts.Should().Be(4);
        email.LastError.Should().Be("InvalidOperationException");
    }

    /// <summary>A success after failures still counts every attempt and keeps the last error for the admins.</summary>
    [Fact]
    public void A_retry_can_succeed()
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);
        email.RecordFailure("HttpRequestException", _now.AddSeconds(1));

        email.RecordSent("Brevo", _now.AddSeconds(30));

        email.Status.Should().Be(EmailStatus.Sent);
        email.Attempts.Should().Be(2);
        email.Provider.Should().Be("Brevo");
        email.LastError.Should().Be("HttpRequestException");
    }

    /// <summary>A long error is cut to 300 characters.</summary>
    [Fact]
    public void A_long_error_is_cut()
    {
        var email = OutgoingEmail.Queue(EmailKind.General, "forward.kept", _to, null, _now);

        email.RecordFailure(new string('x', 1000), _now);

        email.LastError.Should().HaveLength(OutgoingEmail.MaxErrorLength);
        OutgoingEmail.MaxErrorLength.Should().Be(300);
    }

    /// <summary>A skipped email (nothing left to send, for example a spent gate code) is closed without a provider.</summary>
    [Fact]
    public void Skip_closes_the_email()
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);

        email.Skip(_now.AddSeconds(3));

        email.Status.Should().Be(EmailStatus.Skipped);
        email.Attempts.Should().Be(0);
        email.Provider.Should().BeNull();
        email.LastAttemptAt.Should().Be(_now.AddSeconds(3));
    }

    /// <summary>Only a queued email can be sent, failed or skipped.</summary>
    [Fact]
    public void A_closed_email_cannot_change()
    {
        var sent = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);
        sent.RecordSent("Resend", _now);
        var skipped = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);
        skipped.Skip(_now);

        foreach (var email in new[] { sent, skipped })
        {
            ((Action)(() => email.RecordSent("Brevo", _now))).Should().Throw<DomainException>().Which.Code.Should().Be("email.not_queued");
            ((Action)(() => email.RecordFailure("x", _now))).Should().Throw<DomainException>().Which.Code.Should().Be("email.not_queued");
            ((Action)(() => email.Skip(_now))).Should().Throw<DomainException>().Which.Code.Should().Be("email.not_queued");
        }
    }

    /// <summary>The provider name is required.</summary>
    [Fact]
    public void Sent_needs_a_provider()
    {
        var email = OutgoingEmail.Queue(EmailKind.Code, "gate.code", _to, null, _now);

        ((Action)(() => email.RecordSent(" ", _now))).Should().Throw<ArgumentException>();
    }
}
