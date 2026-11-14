using Ardalis.Specification;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Links;

namespace SsSport.Drive.Application.Links;

/// <summary>How long the gate keeps rows after they run out (L-2b, owner-approved 2026-11-12).</summary>
public static class GateRetention
{
    /// <summary>Email codes are kept one day after they expire; the hourly code cap only needs the last hour.</summary>
    public static readonly TimeSpan EmailCodes = TimeSpan.FromDays(1);

    /// <summary>Viewer sessions are kept seven days after they end.</summary>
    public static readonly TimeSpan Sessions = TimeSpan.FromDays(7);

    /// <summary>Trusted devices are kept seven days after their 90 days end.</summary>
    public static readonly TimeSpan Devices = TimeSpan.FromDays(7);

    /// <summary>How many rows one SQL delete removes.</summary>
    public const int BatchSize = 1_000;

    /// <summary>The most batches per table in one run (100,000 rows); the next run takes the rest.</summary>
    public const int MaxBatches = 100;
}

/// <summary>One batch of email codes asked for before a moment.</summary>
public sealed class EmailChallengesRequestedBeforeSpec : Specification<EmailChallenge>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Codes asked for before this moment (UTC) are selected.</param>
    public EmailChallengesRequestedBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(challenge => challenge.RequestedAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>One batch of viewer sessions that ended before a moment.</summary>
public sealed class ViewerSessionsEndedBeforeSpec : Specification<ViewerSession>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Sessions that ended before this moment (UTC) are selected.</param>
    public ViewerSessionsEndedBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(session => session.ExpiresAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>One batch of trusted devices whose trust ended before a moment.</summary>
public sealed class TrustedDevicesEndedBeforeSpec : Specification<TrustedDevice>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Devices whose trust ended before this moment (UTC) are selected.</param>
    public TrustedDevicesEndedBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(device => device.ExpiresAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>One batch of visitor codes (P-8) asked for before a moment.</summary>
public sealed class AccessCodesRequestedBeforeSpec : Specification<AccessCodeChallenge>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Codes asked for before this moment (UTC) are selected.</param>
    public AccessCodesRequestedBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(challenge => challenge.RequestedAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>One batch of sign-in sessions (P-5, P-8) that ended before a moment.</summary>
public sealed class AccountSessionsEndedBeforeSpec : Specification<AccountSession>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="cutoff">Sessions that ended before this moment (UTC) are selected.</param>
    public AccountSessionsEndedBeforeSpec(DateTimeOffset cutoff) =>
        Query.Where(session => session.ExpiresAt < cutoff).Take(GateRetention.BatchSize);
}

/// <summary>What one clean-up deleted.</summary>
/// <param name="EmailCodes">Email codes.</param>
/// <param name="Sessions">Viewer sessions.</param>
/// <param name="Devices">Trusted devices.</param>
/// <param name="VisitorCodes">Codes asked for by visitors requesting access (P-8).</param>
/// <param name="AccountSessions">Signed-in viewer sessions (P-8).</param>
public sealed record GatePurgeResult(int EmailCodes, int Sessions, int Devices, int VisitorCodes = 0, int AccountSessions = 0);

/// <summary>
/// Deletes the gate's rows past their retention (L-2b): email codes, viewer sessions and trusted devices, and the public
/// site's visitor codes and signed-in sessions (P-8). Forward records stay: they are the audit trail of who a link reached. Run daily by a Hangfire job.
/// </summary>
/// <param name="Now">The moment the retention is measured from (UTC).</param>
[InternalRequest("Sent only by the scheduled gate-purge job with the clock's time.")]
public sealed record PurgeGateRowsCommand(DateTimeOffset Now) : ICommand<GatePurgeResult>;

/// <summary>Handles <see cref="PurgeGateRowsCommand"/> with batched SQL deletes; nothing is loaded.</summary>
/// <param name="challenges">The email codes.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="devices">The trusted devices.</param>
/// <param name="visitorCodes">The visitor codes (P-8).</param>
/// <param name="accountSessions">The signed-in viewer sessions (P-8).</param>
public sealed class PurgeGateRowsCommandHandler(
    IRepository<EmailChallenge, Guid> challenges,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<TrustedDevice, Guid> devices,
    IRepository<AccessCodeChallenge, Guid> visitorCodes,
    IRepository<AccountSession, Guid> accountSessions) : ICommandHandler<PurgeGateRowsCommand, GatePurgeResult>
{
    /// <summary>Deletes each table's expired rows, batch by batch.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the deletes.</param>
    /// <returns>How many rows of each kind went.</returns>
    public async Task<GatePurgeResult> Handle(PurgeGateRowsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var codeCutoff = request.Now - EmailChallenge.CodeLifetime - GateRetention.EmailCodes;
        var emailCodes = await BatchDelete.InBatchesAsync(challenges, new EmailChallengesRequestedBeforeSpec(codeCutoff), cancellationToken);
        var expiredSessions = await BatchDelete.InBatchesAsync(sessions, new ViewerSessionsEndedBeforeSpec(request.Now - GateRetention.Sessions), cancellationToken);
        var expiredDevices = await BatchDelete.InBatchesAsync(devices, new TrustedDevicesEndedBeforeSpec(request.Now - GateRetention.Devices), cancellationToken);
        var expiredVisitorCodes = await BatchDelete.InBatchesAsync(visitorCodes, new AccessCodesRequestedBeforeSpec(codeCutoff), cancellationToken);
        var endedAccountSessions = await BatchDelete.InBatchesAsync(accountSessions, new AccountSessionsEndedBeforeSpec(request.Now - GateRetention.Sessions), cancellationToken);
        return new GatePurgeResult(emailCodes, expiredSessions, expiredDevices, expiredVisitorCodes, endedAccountSessions);
    }
}
