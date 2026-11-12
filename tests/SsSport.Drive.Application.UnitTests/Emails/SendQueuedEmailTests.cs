using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Emails;

/// <summary>
/// The email job (L-4): it composes the queued email from its template, hands it to the dispatcher and records the
/// outcome. A failure is recorded and thrown so Hangfire retries; the last failure is recorded and not thrown. A
/// composer that has nothing to send (a spent gate code) skips the email.
/// </summary>
public sealed class SendQueuedEmailTests
{
    /// <summary>The fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The address.</summary>
    private static readonly EmailAddress _to = EmailAddress.Create("marco@lube.it");

    /// <summary>The message the composer builds.</summary>
    private static readonly EmailMessage _message = new(_to, "Subject", "Text", "<p>Html</p>");

    /// <summary>The queue.</summary>
    private readonly IRepository<OutgoingEmail, Guid> _emails = Substitute.For<IRepository<OutgoingEmail, Guid>>();

    /// <summary>The unit of work.</summary>
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    /// <summary>The dispatcher.</summary>
    private readonly IEmailDispatcher _dispatcher = Substitute.For<IEmailDispatcher>();

    /// <summary>The composer for <c>forward.kept</c>.</summary>
    private readonly IEmailComposer _composer = Substitute.For<IEmailComposer>();

    /// <summary>Sets up the composer.</summary>
    public SendQueuedEmailTests()
    {
        _composer.Template.Returns("forward.kept");
        _composer.ComposeAsync(Arg.Any<OutgoingEmail>(), Arg.Any<CancellationToken>()).Returns(_message);
    }

    /// <summary>A queued email is composed, sent through its kind's providers, and recorded as sent.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Sends_and_records_the_provider()
    {
        var email = Queued(EmailKind.General);
        _dispatcher.SendAsync(EmailKind.General, _message, Arg.Any<CancellationToken>()).Returns("GoogleSmtp");

        await Handler().HandleAsync(new EmailQueued(email.Id), new EventContext(null), TestContext.Current.CancellationToken);

        email.Status.Should().Be(EmailStatus.Sent);
        email.Provider.Should().Be("GoogleSmtp");
        email.SentAt.Should().Be(_now);
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A failed send is recorded and thrown, so the job is retried.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_failure_is_recorded_and_retried()
    {
        var email = Queued(EmailKind.General);
        _dispatcher.SendAsync(Arg.Any<EmailKind>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Every email provider failed."));

        var act = () => Handler().HandleAsync(new EmailQueued(email.Id), new EventContext(null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        email.Status.Should().Be(EmailStatus.Queued);
        email.Attempts.Should().Be(1);
        email.LastError.Should().Be("InvalidOperationException");
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The last allowed failure marks the email failed and is not thrown again (no endless retries).</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_last_failure_is_final()
    {
        var email = Queued(EmailKind.General);
        for (var attempt = 1; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            email.RecordFailure("HttpRequestException", _now);
        }

        _dispatcher.SendAsync(Arg.Any<EmailKind>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));

        await Handler().HandleAsync(new EmailQueued(email.Id), new EventContext(null), TestContext.Current.CancellationToken);

        email.Status.Should().Be(EmailStatus.Failed);
        email.LastError.Should().Be("HttpRequestException");
    }

    /// <summary>A composer with nothing to send skips the email without calling a provider.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Nothing_to_send_skips_the_email()
    {
        var email = Queued(EmailKind.General);
        _composer.ComposeAsync(email, Arg.Any<CancellationToken>()).Returns((EmailMessage?)null);

        await Handler().HandleAsync(new EmailQueued(email.Id), new EventContext(null), TestContext.Current.CancellationToken);

        email.Status.Should().Be(EmailStatus.Skipped);
        await _dispatcher.DidNotReceiveWithAnyArgs().SendAsync(default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>An email already sent, failed or skipped (a duplicate job) is left alone.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_closed_email_is_left_alone()
    {
        var email = Queued(EmailKind.General);
        email.RecordSent("GoogleSmtp", _now);

        await Handler().HandleAsync(new EmailQueued(email.Id), new EventContext(null), TestContext.Current.CancellationToken);

        email.Attempts.Should().Be(1);
        await _dispatcher.DidNotReceiveWithAnyArgs().SendAsync(default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>An unknown email id is ignored.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_email_is_ignored()
    {
        await Handler().HandleAsync(new EmailQueued(Guid.CreateVersion7()), new EventContext(null), TestContext.Current.CancellationToken);

        await _dispatcher.DidNotReceiveWithAnyArgs().SendAsync(default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>A template with no composer is skipped (a bug, logged as an error), not retried for ever.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_template_without_composer_is_skipped()
    {
        var email = OutgoingEmail.Queue(EmailKind.General, "unknown.template", _to, null, _now);
        _emails.GetByIdAsync(email.Id, Arg.Any<CancellationToken>()).Returns(email);

        await Handler().HandleAsync(new EmailQueued(email.Id), new EventContext(null), TestContext.Current.CancellationToken);

        email.Status.Should().Be(EmailStatus.Skipped);
        await _dispatcher.DidNotReceiveWithAnyArgs().SendAsync(default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>Queues an email the repository will find.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The email.</returns>
    private OutgoingEmail Queued(EmailKind kind)
    {
        var email = OutgoingEmail.Queue(kind, "forward.kept", _to, null, _now.AddMinutes(-1));
        _emails.GetByIdAsync(email.Id, Arg.Any<CancellationToken>()).Returns(email);
        return email;
    }

    /// <summary>Builds the job.</summary>
    /// <returns>The handler.</returns>
    private SendQueuedEmail Handler() =>
        new(_emails, _unitOfWork, [_composer], _dispatcher, new FixedClock(_now), NullLogger<SendQueuedEmail>.Instance);

    /// <summary>A clock stuck at one moment.</summary>
    /// <param name="now">The moment.</param>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;
    }
}
