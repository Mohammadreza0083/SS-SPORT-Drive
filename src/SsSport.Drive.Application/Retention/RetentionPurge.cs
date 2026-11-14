using Ardalis.Specification;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Application.Retention;

/// <summary>How long the email queue, the raw player events and the sign-in links are kept (W-3, owner-approved 2026-11-14).</summary>
public static class RetentionPeriods
{
    /// <summary>Sent and skipped emails are kept 30 days after they were queued.</summary>
    public static readonly TimeSpan SentEmails = TimeSpan.FromDays(30);

    /// <summary>Failed emails are kept 90 days after they were queued, so a failure can still be looked into.</summary>
    public static readonly TimeSpan FailedEmails = TimeSpan.FromDays(90);

    /// <summary>Raw player events are kept one year; the watches they belong to, where every metric comes from, stay.</summary>
    public static readonly TimeSpan TrackingEvents = TimeSpan.FromDays(365);

    /// <summary>Sign-in links are kept one day after they expire.</summary>
    public static readonly TimeSpan SignInLinks = TimeSpan.FromDays(1);
}

/// <summary>One batch of sent or skipped emails queued before a moment. An email still queued is never selected.</summary>
public sealed class ClosedEmailsQueuedBeforeSpec : Specification<OutgoingEmail>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Emails queued before this moment (UTC) are selected.</param>
    public ClosedEmailsQueuedBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(email => (email.Status == EmailStatus.Sent || email.Status == EmailStatus.Skipped) && email.QueuedAt < cutoff)
            .Take(GateRetention.BatchSize);
}

/// <summary>One batch of failed emails queued before a moment.</summary>
public sealed class FailedEmailsQueuedBeforeSpec : Specification<OutgoingEmail>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Failed emails queued before this moment (UTC) are selected.</param>
    public FailedEmailsQueuedBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(email => email.Status == EmailStatus.Failed && email.QueuedAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>One batch of player events that happened before a moment.</summary>
public sealed class TrackingEventsBeforeSpec : Specification<TrackingEvent>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Events that happened before this moment (UTC) are selected.</param>
    public TrackingEventsBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(item => item.OccurredAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>One batch of sign-in links that expired before a moment.</summary>
public sealed class SignInLinksExpiredBeforeSpec : Specification<SignInLink>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Links that expired before this moment (UTC) are selected.</param>
    public SignInLinksExpiredBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(link => link.ExpiresAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>What one retention clean-up deleted.</summary>
/// <param name="SentEmails">Sent and skipped emails.</param>
/// <param name="FailedEmails">Failed emails.</param>
/// <param name="TrackingEvents">Raw player events.</param>
/// <param name="SignInLinks">Sign-in links.</param>
public sealed record RetentionPurgeResult(int SentEmails, int FailedEmails, int TrackingEvents, int SignInLinks);

/// <summary>
/// Deletes the email queue, the raw player events and the sign-in links past their retention (W-3). Watches are never
/// deleted. Run daily by a Hangfire job.
/// </summary>
/// <param name="Now">The moment the retention is measured from (UTC).</param>
[InternalRequest("Sent only by the scheduled retention-purge job with the clock's time.")]
public sealed record PurgeRetainedRowsCommand(DateTimeOffset Now) : ICommand<RetentionPurgeResult>;

/// <summary>Handles <see cref="PurgeRetainedRowsCommand"/> with batched SQL deletes; nothing is loaded.</summary>
/// <param name="emails">The email queue.</param>
/// <param name="events">The raw player events.</param>
/// <param name="links">The sign-in links.</param>
public sealed class PurgeRetainedRowsCommandHandler(
    IRepository<OutgoingEmail, Guid> emails,
    IRepository<TrackingEvent, Guid> events,
    IRepository<SignInLink, Guid> links) : ICommandHandler<PurgeRetainedRowsCommand, RetentionPurgeResult>
{
    /// <summary>Deletes each table's rows past their retention, batch by batch.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the deletes.</param>
    /// <returns>How many rows of each kind went.</returns>
    public async Task<RetentionPurgeResult> Handle(PurgeRetainedRowsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sent = await BatchDelete.InBatchesAsync(emails, new ClosedEmailsQueuedBeforeSpec(request.Now - RetentionPeriods.SentEmails), cancellationToken);
        var failed = await BatchDelete.InBatchesAsync(emails, new FailedEmailsQueuedBeforeSpec(request.Now - RetentionPeriods.FailedEmails), cancellationToken);
        var oldEvents = await BatchDelete.InBatchesAsync(events, new TrackingEventsBeforeSpec(request.Now - RetentionPeriods.TrackingEvents), cancellationToken);
        var expiredLinks = await BatchDelete.InBatchesAsync(links, new SignInLinksExpiredBeforeSpec(request.Now - RetentionPeriods.SignInLinks), cancellationToken);
        return new RetentionPurgeResult(sent, failed, oldEvents, expiredLinks);
    }
}
