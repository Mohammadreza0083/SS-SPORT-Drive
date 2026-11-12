using Ardalis.Specification;
using FluentValidation;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Links;

/// <summary>The newest code asked for an email on a link.</summary>
public sealed class LatestEmailChallengeSpec : Specification<EmailChallenge>, ISingleResultSpecification<EmailChallenge>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The email.</param>
    public LatestEmailChallengeSpec(Guid shareLinkId, EmailAddress email) =>
        Query.Where(challenge => challenge.ShareLinkId == shareLinkId && challenge.Email == email)
            .OrderByDescending(challenge => challenge.RequestedAt)
            .Take(1);
}

/// <summary>The codes asked for an email on a link since a moment.</summary>
public sealed class EmailChallengesSinceSpec : Specification<EmailChallenge>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The email.</param>
    /// <param name="since">The start of the window (UTC).</param>
    public EmailChallengesSinceSpec(Guid shareLinkId, EmailAddress email, DateTimeOffset since) =>
        Query.Where(challenge => challenge.ShareLinkId == shareLinkId && challenge.Email == email && challenge.RequestedAt >= since);
}

/// <summary>The trusted device row of one device token for one link.</summary>
public sealed class TrustedDeviceSpec : Specification<TrustedDevice>, ISingleResultSpecification<TrustedDevice>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="tokenHash">The keyed hash of the device token.</param>
    /// <param name="shareLinkId">The link.</param>
    public TrustedDeviceSpec(string tokenHash, Guid shareLinkId) =>
        Query.Where(device => device.TokenHash == tokenHash && device.ShareLinkId == shareLinkId);
}

/// <summary>The session behind a session token.</summary>
public sealed class ViewerSessionByTokenSpec : Specification<ViewerSession>, ISingleResultSpecification<ViewerSession>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="tokenHash">The keyed hash of the session token.</param>
    public ViewerSessionByTokenSpec(string tokenHash) => Query.Where(session => session.TokenHash == tokenHash);
}

/// <summary>The gate's limits and shared steps (L-2, decisions §7).</summary>
public static partial class ViewerGateRules
{
    /// <summary>The most codes one email may ask for on one link in <see cref="CodeWindow"/>.</summary>
    public const int MaxCodesPerWindow = 5;

    /// <summary>The window <see cref="MaxCodesPerWindow"/> counts in.</summary>
    public static readonly TimeSpan CodeWindow = TimeSpan.FromHours(1);

    /// <summary>The code clients see when asking too often.</summary>
    public const string TooManyCode = "email_code.too_many";

    /// <summary>The code clients see for an email the link does not admit, or a blocked forward (the same, on purpose).</summary>
    public const string EmailNotAllowedCode = "share_link.email_not_allowed";

    /// <summary>The message shown with <see cref="EmailNotAllowedCode"/>.</summary>
    public const string EmailNotAllowedMessage = "This link was shared with someone else. Use the email it was sent to.";

    /// <summary>
    /// Starts a session on a link for an email, and returns its token. The token goes to the browser; only its hash is
    /// stored.
    /// </summary>
    /// <param name="sessions">The sessions.</param>
    /// <param name="secrets">The gate's secrets.</param>
    /// <param name="link">The link.</param>
    /// <param name="email">The viewer's email.</param>
    /// <param name="isForward">Whether the email is a forward.</param>
    /// <param name="deviceId">The trusted device, or <see langword="null"/>.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>The session token and when it ends.</returns>
    public static (string Token, DateTimeOffset ExpiresAt) StartSession(
        IRepository<ViewerSession, Guid> sessions,
        IViewerSecrets secrets,
        ShareLink link,
        EmailAddress email,
        bool isForward,
        Guid? deviceId,
        DateTimeOffset now)
    {
        var token = secrets.NewToken();
        var session = ViewerSession.Start(secrets.HashToken(token), link.Id, email, isForward, deviceId, now);
        sessions.Add(session);
        return (token, session.ExpiresAt);
    }

    /// <summary>Logs an email a link refused at the gate.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="ShareLinkId">The link.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gate refused an email that link {ShareLinkId} does not admit")]
    public static partial void LogEmailRefused(ILogger logger, Guid ShareLinkId);

    /// <summary>Logs a refused request for a code: asked too often.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="ShareLinkId">The link.</param>
    /// <param name="RetryAfterSeconds">When the viewer may ask again.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gate refused an email code for link {ShareLinkId}: too many requests; retry after {RetryAfterSeconds}s")]
    public static partial void LogTooMany(ILogger logger, Guid ShareLinkId, int RetryAfterSeconds);

    /// <summary>Logs a code that did not open the link.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="ShareLinkId">The link.</param>
    /// <param name="Outcome">Why: wrong, exhausted, expired or used.</param>
    /// <param name="AttemptsLeft">How many tries are left.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gate code failed for link {ShareLinkId}: {Outcome}; {AttemptsLeft} attempts left")]
    public static partial void LogCodeFailed(ILogger logger, Guid ShareLinkId, EmailCodeCheck Outcome, int AttemptsLeft);
}

/// <summary>The gate's email form.</summary>
public sealed record EmailCodeFormDto
{
    /// <summary>Gets the email the code should go to.</summary>
    public string? Email { get; init; }
}

/// <summary>The gate's code form.</summary>
public sealed record VerifyEmailCodeFormDto
{
    /// <summary>Gets the email the code went to.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the 6-digit code.</summary>
    public string? Code { get; init; }
}

/// <summary>What the gate shows after a code was asked for.</summary>
public sealed record EmailCodeSentDto
{
    /// <summary>Gets the email the code goes to, masked (<c>m***@lube.it</c>).</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets how many seconds until another code may be asked for.</summary>
    public int ResendAfterSeconds { get; init; }

    /// <summary>Gets how many seconds the code is valid once it arrives.</summary>
    public int ExpiresInSeconds { get; init; }
}

/// <summary>
/// The tokens a viewer gets at the gate, for the web app to put into cookies: never shown to scripts. A forward that no
/// admin has kept yet (L-3) gets the device token only, with <see cref="AwaitingApproval"/> set.
/// </summary>
public sealed record ViewerAccessDto
{
    /// <summary>Gets the device token (<c>__Host-ssd_dev</c>), or <see langword="null"/> when the device cookie stays as it is.</summary>
    public string? DeviceToken { get; init; }

    /// <summary>Gets when the device is forgotten, or <see langword="null"/> with no device token.</summary>
    public DateTimeOffset? DeviceExpiresAt { get; init; }

    /// <summary>Gets the session token (<c>__Host-ssd_vs</c>), or <see langword="null"/> while awaiting approval.</summary>
    public string? SessionToken { get; init; }

    /// <summary>Gets when the session ends, or <see langword="null"/> while awaiting approval.</summary>
    public DateTimeOffset? SessionExpiresAt { get; init; }

    /// <summary>Gets whether the email is a forward an admin has not kept yet: the device is trusted, the link stays closed.</summary>
    public bool AwaitingApproval { get; init; }
}

/// <summary>Who a session belongs to.</summary>
public sealed record ViewerSessionDto
{
    /// <summary>Gets the viewer's email, laid over the videos (V-4).</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets whether the email is a forward.</summary>
    public bool IsForward { get; init; }

    /// <summary>Gets when the session ends.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>The outcome of typing a code: the tokens, or why not.</summary>
/// <param name="Access">The tokens, or <see langword="null"/> when the code did not open the link.</param>
/// <param name="FailureCode">
/// Why not: <c>email_code.invalid</c> (tries left), <c>email_code.exhausted</c>, <c>email_code.expired</c> or
/// <c>email_code.used</c>; <see langword="null"/> on success.
/// </param>
/// <param name="FailureMessage">A message for the viewer, or <see langword="null"/>.</param>
/// <param name="AttemptsLeft">How many tries are left.</param>
public sealed record EmailCodeVerification(ViewerAccessDto? Access, string? FailureCode, string? FailureMessage, int AttemptsLeft)
{
    /// <summary>Describes a code that did not open the link.</summary>
    /// <param name="check">What the check found.</param>
    /// <param name="attemptsLeft">How many tries are left.</param>
    /// <returns>The outcome.</returns>
    public static EmailCodeVerification Failed(EmailCodeCheck check, int attemptsLeft) => check switch
    {
        EmailCodeCheck.Wrong => new(null, "email_code.invalid", attemptsLeft == 1 ? "That code is not right. 1 try left." : $"That code is not right. {attemptsLeft} tries left.", attemptsLeft),
        EmailCodeCheck.Exhausted => new(null, "email_code.exhausted", "Too many wrong codes. Ask for a new code.", 0),
        EmailCodeCheck.Used => new(null, "email_code.used", "This code was already used. Ask for a new code.", 0),
        _ => new(null, "email_code.expired", "This code has expired. Ask for a new code.", 0),
    };
}

/// <summary>Asks for a code for an email at the gate of <c>/r/{code}</c> (<c>POST /links/{code}/email-code</c>). Public.</summary>
/// <param name="Code">The link code.</param>
/// <param name="Email">The email.</param>
public sealed record RequestEmailCodeCommand(string Code, string? Email) : ICommand<EmailCodeSentDto>;

/// <summary>The email is filled in; its format is the email rule's (422 <c>email.invalid</c>).</summary>
public sealed class RequestEmailCodeCommandValidator : AbstractValidator<RequestEmailCodeCommand>
{
    /// <summary>Builds the rules.</summary>
    public RequestEmailCodeCommandValidator() =>
        RuleFor(command => command.Email).NotEmpty().WithMessage("Enter your email.").OverridePropertyName("email");
}

/// <summary>Handles <see cref="RequestEmailCodeCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="challenges">The codes.</param>
/// <param name="forwards">The forwards, to refuse blocked ones (L-3).</param>
/// <param name="emails">The email queue; the code is drawn and mailed by the job that sends the queued email (L-4).</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class RequestEmailCodeCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<EmailChallenge, Guid> challenges,
    IRepository<LinkForward, Guid> forwards,
    IRepository<OutgoingEmail, Guid> emails,
    TimeProvider timeProvider,
    ILogger<RequestEmailCodeCommandHandler> logger) : ICommandHandler<RequestEmailCodeCommand, EmailCodeSentDto>
{
    /// <summary>
    /// Checks that the link opens and admits the email, applies the 60-second wait and the hourly cap, then records the
    /// request; the code itself is drawn and mailed by the background job.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the gate shows next.</returns>
    /// <exception cref="ShareLinkUnavailableException">The link does not open.</exception>
    /// <exception cref="DomainException"><c>email.invalid</c> or <c>share_link.email_not_allowed</c>.</exception>
    /// <exception cref="TooManyRequestsException">Asked too soon, or too often.</exception>
    public async Task<EmailCodeSentDto> Handle(RequestEmailCodeCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        var email = EmailAddress.Create(request.Email);
        var recipient = await recipients.GetByIdAsync(link.RecipientId, cancellationToken) ?? throw new ShareLinkUnavailableException();
        var admission = link.Admit(recipient, email);
        if (admission == EmailAdmission.Refused || (admission == EmailAdmission.Forward && await IsBlockedAsync(link.Id, email, cancellationToken)))
        {
            ViewerGateRules.LogEmailRefused(logger, link.Id);
            throw new DomainException(ViewerGateRules.EmailNotAllowedCode, ViewerGateRules.EmailNotAllowedMessage);
        }

        await EnsureNotTooOftenAsync(link.Id, email, now, cancellationToken);
        var challenge = EmailChallenge.Request(link.Id, email, admission == EmailAdmission.Forward, now);
        challenges.Add(challenge);
        var emailData = $$"""{"challengeId":"{{challenge.Id:D}}"}""";
        emails.Add(OutgoingEmail.Queue(EmailKind.Code, EmailTemplates.GateCode, email, emailData, now));
        return new EmailCodeSentDto
        {
            Email = email.ToString(),
            ResendAfterSeconds = (int)EmailChallenge.ResendAfter.TotalSeconds,
            ExpiresInSeconds = (int)EmailChallenge.CodeLifetime.TotalSeconds,
        };
    }

    /// <summary>Tells whether an admin blocked this forward (L-3).</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The forwarded email.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="true"/> when blocked.</returns>
    private async Task<bool> IsBlockedAsync(Guid shareLinkId, EmailAddress email, CancellationToken cancellationToken) =>
        await forwards.FirstOrDefaultAsync(new LinkForwardSpec(shareLinkId, email), cancellationToken) is { Status: ForwardStatus.Blocked };

    /// <summary>Refuses a code asked for within 60 seconds of the last one, or over the hourly cap.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="email">The email.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>A task that completes when the request may go ahead.</returns>
    /// <exception cref="TooManyRequestsException">Asked too soon, or too often.</exception>
    private async Task EnsureNotTooOftenAsync(Guid shareLinkId, EmailAddress email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await challenges.FirstOrDefaultAsync(new LatestEmailChallengeSpec(shareLinkId, email), cancellationToken);
        if (latest is not null && latest.ResendAt > now)
        {
            Refuse(shareLinkId, latest.ResendAt - now, "Wait a moment before asking for a new code.");
        }

        var windowStart = now - ViewerGateRules.CodeWindow;
        var recent = await challenges.ListAsync(new EmailChallengesSinceSpec(shareLinkId, email, windowStart), cancellationToken);
        if (recent.Count >= ViewerGateRules.MaxCodesPerWindow)
        {
            var oldest = recent.Min(challenge => challenge.RequestedAt);
            Refuse(shareLinkId, oldest + ViewerGateRules.CodeWindow - now, "Too many codes for this email. Try again later.");
        }
    }

    /// <summary>Logs and throws the refusal.</summary>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="wait">How long to wait.</param>
    /// <param name="message">The message.</param>
    /// <exception cref="TooManyRequestsException">Always.</exception>
    private void Refuse(Guid shareLinkId, TimeSpan wait, string message)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        ViewerGateRules.LogTooMany(logger, shareLinkId, seconds);
        throw new TooManyRequestsException(ViewerGateRules.TooManyCode, message, TimeSpan.FromSeconds(seconds));
    }
}

/// <summary>
/// Types the code at the gate (<c>POST /links/{code}/verify</c>). Public. The right code trusts the device for 90 days
/// and starts a one-day session; a wrong one uses a try, which is saved either way. A forward (L-3) is recorded and
/// alerted the first time; until an admin keeps it, the device is trusted but no session starts. A blocked forward is
/// refused like an email the link does not admit.
/// </summary>
/// <param name="Code">The link code.</param>
/// <param name="Email">The email the code went to.</param>
/// <param name="EmailCode">The typed code.</param>
/// <param name="DeviceToken">The browser's device token, or <see langword="null"/> for a new device.</param>
public sealed record VerifyEmailCodeCommand(string Code, string? Email, string? EmailCode, string? DeviceToken) : ICommand<EmailCodeVerification>;

/// <summary>The email is filled in, the code is six digits and a device token is no longer than a real one.</summary>
public sealed class VerifyEmailCodeCommandValidator : AbstractValidator<VerifyEmailCodeCommand>
{
    /// <summary>The longest device token accepted; real ones are 43 characters.</summary>
    public const int MaxDeviceTokenLength = 128;

    /// <summary>Builds the rules.</summary>
    public VerifyEmailCodeCommandValidator()
    {
        RuleFor(command => command.DeviceToken)
            .MaximumLength(MaxDeviceTokenLength)
            .WithMessage("The device token is not valid.")
            .OverridePropertyName("deviceToken");
        RuleFor(command => command.Email).NotEmpty().WithMessage("Enter your email.").OverridePropertyName("email");
        RuleFor(command => command.EmailCode)
            .NotEmpty()
            .Matches($"^[0-9]{{{EmailChallenge.CodeLength}}}$")
            .WithMessage("Enter all 6 digits.")
            .OverridePropertyName("code");
    }
}

/// <summary>Handles <see cref="VerifyEmailCodeCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="challenges">The codes.</param>
/// <param name="devices">The trusted devices.</param>
/// <param name="sessions">The sessions.</param>
/// <param name="forwards">The forwards (L-3).</param>
/// <param name="secrets">The gate's secrets.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class VerifyEmailCodeCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<EmailChallenge, Guid> challenges,
    IRepository<TrustedDevice, Guid> devices,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<LinkForward, Guid> forwards,
    IViewerSecrets secrets,
    TimeProvider timeProvider,
    ILogger<VerifyEmailCodeCommandHandler> logger) : ICommandHandler<VerifyEmailCodeCommand, EmailCodeVerification>
{
    /// <summary>Checks the newest code for the email; when it opens, trusts the device and starts a session.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The tokens, or why not.</returns>
    /// <exception cref="ShareLinkUnavailableException">The link does not open.</exception>
    /// <exception cref="DomainException"><c>email.invalid</c>.</exception>
    public async Task<EmailCodeVerification> Handle(VerifyEmailCodeCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        var email = EmailAddress.Create(request.Email);
        var challenge = await challenges.FirstOrDefaultAsync(new LatestEmailChallengeSpec(link.Id, email), cancellationToken);
        if (challenge is null)
        {
            ViewerGateRules.LogCodeFailed(logger, link.Id, EmailCodeCheck.Expired, 0);
            return EmailCodeVerification.Failed(EmailCodeCheck.Expired, 0);
        }

        var forward = challenge.IsForward ? await forwards.FirstOrDefaultAsync(new LinkForwardSpec(link.Id, email), cancellationToken) : null;
        if (forward is { Status: ForwardStatus.Blocked })
        {
            ViewerGateRules.LogEmailRefused(logger, link.Id);
            return new EmailCodeVerification(null, ViewerGateRules.EmailNotAllowedCode, ViewerGateRules.EmailNotAllowedMessage, 0);
        }

        var check = challenge.Check(secrets.CodeMatches(request.EmailCode!, challenge.CodeHash), now);
        if (check != EmailCodeCheck.Verified)
        {
            ViewerGateRules.LogCodeFailed(logger, link.Id, check, challenge.AttemptsLeft);
            return EmailCodeVerification.Failed(check, challenge.AttemptsLeft);
        }

        var device = await TrustDeviceAsync(request.DeviceToken, link.Id, challenge, now, cancellationToken);
        if (challenge.IsForward && forward is null)
        {
            forward = LinkForward.Record(link.Id, email, now);
            forwards.Add(forward);
        }

        if (forward is { OpensLink: false })
        {
            var waiting = new ViewerAccessDto { DeviceToken = device.Token, DeviceExpiresAt = device.Row.ExpiresAt, AwaitingApproval = true };
            return new EmailCodeVerification(waiting, null, null, challenge.AttemptsLeft);
        }

        var (sessionToken, sessionExpiresAt) = ViewerGateRules.StartSession(sessions, secrets, link, email, challenge.IsForward, device.Row.Id, now);
        var access = new ViewerAccessDto
        {
            DeviceToken = device.Token,
            DeviceExpiresAt = device.Row.ExpiresAt,
            SessionToken = sessionToken,
            SessionExpiresAt = sessionExpiresAt,
        };
        return new EmailCodeVerification(access, null, null, challenge.AttemptsLeft);
    }

    /// <summary>
    /// Trusts the browser for the link: it keeps the device token it already has (so one token serves all its links),
    /// or gets a new one.
    /// </summary>
    /// <param name="deviceToken">The browser's device token, or <see langword="null"/>.</param>
    /// <param name="shareLinkId">The link.</param>
    /// <param name="challenge">The verified code.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The device token and its row.</returns>
    private async Task<(string Token, TrustedDevice Row)> TrustDeviceAsync(
        string? deviceToken,
        Guid shareLinkId,
        EmailChallenge challenge,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = string.IsNullOrWhiteSpace(deviceToken) ? secrets.NewToken() : deviceToken;
        var hash = secrets.HashToken(token);
        var existing = await devices.FirstOrDefaultAsync(new TrustedDeviceSpec(hash, shareLinkId), cancellationToken);
        if (existing is not null)
        {
            existing.Retrust(challenge.Email, challenge.IsForward, now);
            return (token, existing);
        }

        var device = TrustedDevice.Trust(hash, shareLinkId, challenge.Email, challenge.IsForward, now);
        devices.Add(device);
        return (token, device);
    }
}

/// <summary>
/// Opens a link on a trusted device without a code (<c>POST /links/{code}/session</c>): a new one-day session. Links
/// that do not verify new devices open for anyone, as the recipient's first email. Public.
/// </summary>
/// <param name="Code">The link code.</param>
/// <param name="DeviceToken">The browser's device token, or <see langword="null"/>.</param>
public sealed record OpenWithDeviceCommand(string Code, string? DeviceToken) : ICommand<ViewerAccessDto>;

/// <summary>Handles <see cref="OpenWithDeviceCommand"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="recipients">The recipients.</param>
/// <param name="devices">The trusted devices.</param>
/// <param name="sessions">The sessions.</param>
/// <param name="forwards">The forwards (L-3).</param>
/// <param name="secrets">The gate's secrets.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class OpenWithDeviceCommandHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<Recipient, Guid> recipients,
    IRepository<TrustedDevice, Guid> devices,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<LinkForward, Guid> forwards,
    IViewerSecrets secrets,
    TimeProvider timeProvider) : ICommandHandler<OpenWithDeviceCommand, ViewerAccessDto>
{
    /// <summary>Starts a session from the trusted device, or for anyone when the link skips the gate.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The session token (no new device token).</returns>
    /// <exception cref="ShareLinkUnavailableException">The link does not open.</exception>
    /// <exception cref="ViewerVerificationRequiredException">The device is not trusted for the link, or its forward was blocked.</exception>
    /// <exception cref="ForwardPendingException">The device's email is a forward no admin has kept yet.</exception>
    public async Task<ViewerAccessDto> Handle(OpenWithDeviceCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        if (!link.VerifyOnNewDevice)
        {
            var recipient = await recipients.GetByIdAsync(link.RecipientId, cancellationToken) ?? throw new ShareLinkUnavailableException();
            return Access(ViewerGateRules.StartSession(sessions, secrets, link, recipient.Emails[0], isForward: false, deviceId: null, now));
        }

        if (string.IsNullOrWhiteSpace(request.DeviceToken))
        {
            throw new ViewerVerificationRequiredException();
        }

        var device = await devices.FirstOrDefaultAsync(new TrustedDeviceSpec(secrets.HashToken(request.DeviceToken), link.Id), cancellationToken);
        if (device is null || !device.IsTrustedAt(now))
        {
            throw new ViewerVerificationRequiredException();
        }

        if (device.IsForward)
        {
            var forward = await forwards.FirstOrDefaultAsync(new LinkForwardSpec(link.Id, device.Email), cancellationToken);
            if (forward is { Status: ForwardStatus.Blocked })
            {
                throw new ViewerVerificationRequiredException();
            }

            if (forward is not { OpensLink: true })
            {
                throw new ForwardPendingException();
            }
        }

        device.Seen(now);
        return Access(ViewerGateRules.StartSession(sessions, secrets, link, device.Email, device.IsForward, device.Id, now));
    }

    /// <summary>Wraps a new session's token.</summary>
    /// <param name="session">The token and when it ends.</param>
    /// <returns>The DTO.</returns>
    private static ViewerAccessDto Access((string Token, DateTimeOffset ExpiresAt) session) =>
        new() { SessionToken = session.Token, SessionExpiresAt = session.ExpiresAt };
}

/// <summary>Reads who a session belongs to (<c>GET /links/{code}/session</c>). Public; the token is the proof.</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, or <see langword="null"/>.</param>
public sealed record GetViewerSessionQuery(string Code, string? SessionToken) : IQuery<ViewerSessionDto>;

/// <summary>Handles <see cref="GetViewerSessionQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The sessions.</param>
/// <param name="secrets">The gate's secrets.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetViewerSessionQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IViewerSecrets secrets,
    TimeProvider timeProvider) : IQueryHandler<GetViewerSessionQuery, ViewerSessionDto>
{
    /// <summary>Finds the session, checks it is on and belongs to the link.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Who it belongs to.</returns>
    /// <exception cref="ShareLinkUnavailableException">The link does not open.</exception>
    /// <exception cref="ViewerVerificationRequiredException">No such session for the link, or it ended.</exception>
    public async Task<ViewerSessionDto> Handle(GetViewerSessionQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        var session = await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        return new ViewerSessionDto { Email = session.Email.Value, IsForward = session.IsForward, ExpiresAt = session.ExpiresAt };
    }
}
