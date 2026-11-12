using Ardalis.Specification;
using FluentValidation;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Accounts;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Public;

/// <summary>The newest code asked for an email.</summary>
public sealed class LatestAccessCodeSpec : Specification<AccessCodeChallenge>, ISingleResultSpecification<AccessCodeChallenge>
{
    /// <summary>Initializes a new instance of the <see cref="LatestAccessCodeSpec"/> class.</summary>
    /// <param name="email">The email.</param>
    public LatestAccessCodeSpec(EmailAddress email) =>
        Query.Where(challenge => challenge.Email == email).OrderByDescending(challenge => challenge.RequestedAt).Take(1);
}

/// <summary>The codes asked for an email since a moment.</summary>
public sealed class AccessCodesSinceSpec : Specification<AccessCodeChallenge>
{
    /// <summary>Initializes a new instance of the <see cref="AccessCodesSinceSpec"/> class.</summary>
    /// <param name="email">The email.</param>
    /// <param name="since">The start of the window (UTC).</param>
    public AccessCodesSinceSpec(EmailAddress email, DateTimeOffset since) =>
        Query.Where(challenge => challenge.Email == email && challenge.RequestedAt >= since);
}

/// <summary>The requests an email made since a moment.</summary>
public sealed class AccessRequestsByEmailSinceSpec : Specification<AccessRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AccessRequestsByEmailSinceSpec"/> class.</summary>
    /// <param name="email">The email.</param>
    /// <param name="since">The start of the window (UTC).</param>
    public AccessRequestsByEmailSinceSpec(EmailAddress email, DateTimeOffset since) =>
        Query.Where(request => request.Email == email && request.CreatedAt >= since).AsNoTracking();
}

/// <summary>The requests made for a player since a moment, from every email.</summary>
public sealed class AccessRequestsForPlayerSinceSpec : Specification<AccessRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AccessRequestsForPlayerSinceSpec"/> class.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="since">The start of the window (UTC).</param>
    public AccessRequestsForPlayerSinceSpec(Guid playerId, DateTimeOffset since) =>
        Query.Where(request => request.PlayerId == playerId && request.CreatedAt >= since).AsNoTracking();
}

/// <summary>An email's request for a player that no admin has decided yet.</summary>
public sealed class PendingAccessRequestSpec : Specification<AccessRequest>
{
    /// <summary>Initializes a new instance of the <see cref="PendingAccessRequestSpec"/> class.</summary>
    /// <param name="email">The email.</param>
    /// <param name="playerId">The player.</param>
    public PendingAccessRequestSpec(EmailAddress email, Guid playerId) =>
        Query.Where(request => request.Email == email && request.PlayerId == playerId && request.Status == AccessRequestStatus.Pending).AsNoTracking();
}

/// <summary>The limits of the request-access flow (P-8, decisions §6 "Anti-spam") and what they log.</summary>
public static partial class PublicAccessRules
{
    /// <summary>The most requests one email may make in <see cref="EmailWindow"/>.</summary>
    public const int MaxRequestsPerEmail = 5;

    /// <summary>The window <see cref="MaxRequestsPerEmail"/> counts in.</summary>
    public static readonly TimeSpan EmailWindow = TimeSpan.FromHours(24);

    /// <summary>The most requests one player may get from all emails in <see cref="PlayerWindow"/>.</summary>
    public const int MaxRequestsPerPlayer = 20;

    /// <summary>The window <see cref="MaxRequestsPerPlayer"/> counts in.</summary>
    public static readonly TimeSpan PlayerWindow = TimeSpan.FromHours(1);

    /// <summary>The code clients see when a limit is reached.</summary>
    public const string TooManyRequestsCode = "access_request.too_many";

    /// <summary>The code clients see when the Turnstile check fails.</summary>
    public const string TurnstileFailedCode = "turnstile.failed";

    /// <summary>Logs a code that was refused: asked too often.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="RetryAfterSeconds">When the visitor may ask again.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Access code refused: too many requests; retry after {RetryAfterSeconds}s")]
    public static partial void LogCodeTooMany(ILogger logger, int RetryAfterSeconds);

    /// <summary>Logs a Turnstile check that failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="PlayerId">The player asked for.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Access request refused: Turnstile check failed for player {PlayerId}")]
    public static partial void LogTurnstileFailed(ILogger logger, Guid PlayerId);

    /// <summary>Logs a code that did not verify.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="PlayerId">The player asked for.</param>
    /// <param name="Outcome">Why: wrong, exhausted, expired or used.</param>
    /// <param name="AttemptsLeft">How many tries are left.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Access request code failed for player {PlayerId}: {Outcome}; {AttemptsLeft} attempts left")]
    public static partial void LogCodeFailed(ILogger logger, Guid PlayerId, EmailCodeCheck Outcome, int AttemptsLeft);

    /// <summary>Logs a request refused by a limit.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="PlayerId">The player asked for.</param>
    /// <param name="Limit">Which limit: email or player.</param>
    /// <param name="RetryAfterSeconds">When the visitor may ask again.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Access request refused for player {PlayerId}: {Limit} limit reached; retry after {RetryAfterSeconds}s")]
    public static partial void LogRequestLimit(ILogger logger, Guid PlayerId, string Limit, int RetryAfterSeconds);

    /// <summary>Logs a request that was stored.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="AccessRequestId">The request.</param>
    /// <param name="PlayerId">The player asked for.</param>
    /// <param name="Source">How the email was proven.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Access request {AccessRequestId} made for player {PlayerId} ({Source})")]
    public static partial void LogRequestMade(ILogger logger, Guid AccessRequestId, Guid PlayerId, AccessRequestSource Source);
}

/// <summary>Asks for a code for an email (<c>POST /public/access-code</c>). Public.</summary>
/// <param name="Email">The email.</param>
public sealed record RequestAccessCodeCommand(string? Email) : ICommand<EmailCodeSentDto>;

/// <summary>The email is filled in; its format is the email rule's (422 <c>email.invalid</c>).</summary>
public sealed class RequestAccessCodeCommandValidator : AbstractValidator<RequestAccessCodeCommand>
{
    /// <summary>Builds the rules.</summary>
    public RequestAccessCodeCommandValidator() =>
        RuleFor(command => command.Email).NotEmpty().WithMessage("Enter your email.").OverridePropertyName("email");
}

/// <summary>Handles <see cref="RequestAccessCodeCommand"/>.</summary>
/// <param name="challenges">The codes.</param>
/// <param name="emails">The email queue (L-4).</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class RequestAccessCodeCommandHandler(
    IRepository<AccessCodeChallenge, Guid> challenges,
    IRepository<OutgoingEmail, Guid> emails,
    TimeProvider timeProvider,
    ILogger<RequestAccessCodeCommandHandler> logger) : ICommandHandler<RequestAccessCodeCommand, EmailCodeSentDto>
{
    /// <summary>Applies the 60-second wait and the hourly cap, then records the request; the queue mails the code.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the dialog shows next.</returns>
    /// <exception cref="DomainException"><c>email.invalid</c>.</exception>
    /// <exception cref="TooManyRequestsException">Asked too soon, or too often.</exception>
    public async Task<EmailCodeSentDto> Handle(RequestAccessCodeCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var email = EmailAddress.Create(request.Email);
        await EnsureNotTooOftenAsync(email, now, cancellationToken);
        var challenge = AccessCodeChallenge.Request(email, now);
        challenges.Add(challenge);
        var emailData = $$"""{"challengeId":"{{challenge.Id:D}}"}""";
        emails.Add(OutgoingEmail.Queue(EmailKind.Code, EmailTemplates.AccessCode, email, emailData, now));
        return new EmailCodeSentDto
        {
            Email = email.ToString(),
            ResendAfterSeconds = (int)AccessCodeChallenge.ResendAfter.TotalSeconds,
            ExpiresInSeconds = (int)AccessCodeChallenge.CodeLifetime.TotalSeconds,
        };
    }

    /// <summary>Refuses a code asked for within 60 seconds of the last one, or over the hourly cap.</summary>
    /// <param name="email">The email.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>A task that completes when the request may go ahead.</returns>
    /// <exception cref="TooManyRequestsException">Asked too soon, or too often.</exception>
    private async Task EnsureNotTooOftenAsync(EmailAddress email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await challenges.FirstOrDefaultAsync(new LatestAccessCodeSpec(email), cancellationToken);
        if (latest is not null && latest.ResendAt > now)
        {
            Refuse(latest.ResendAt - now, "Wait a moment before asking for a new code.");
        }

        var recent = await challenges.ListAsync(new AccessCodesSinceSpec(email, now - ViewerGateRules.CodeWindow), cancellationToken);
        if (recent.Count >= ViewerGateRules.MaxCodesPerWindow)
        {
            Refuse(recent.Min(challenge => challenge.RequestedAt) + ViewerGateRules.CodeWindow - now, "Too many codes for this email. Try again later.");
        }
    }

    /// <summary>Logs and throws the refusal.</summary>
    /// <param name="wait">How long to wait.</param>
    /// <param name="message">The message.</param>
    /// <exception cref="TooManyRequestsException">Always.</exception>
    private void Refuse(TimeSpan wait, string message)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        PublicAccessRules.LogCodeTooMany(logger, seconds);
        throw new TooManyRequestsException(ViewerGateRules.TooManyCode, message, TimeSpan.FromSeconds(seconds));
    }
}

/// <summary>
/// Composes the visitor code email (P-8): draws the code in the job, stores its salted hash, then returns the mail, so the
/// code never passes through the request or the queue. A request that was already used or is too old has nothing to send.
/// A retried job draws a new code, which replaces the one before.
/// </summary>
/// <param name="challenges">The codes.</param>
/// <param name="unitOfWork">Saves the hash before the mail goes out.</param>
/// <param name="secrets">The secrets.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class AccessCodeComposer(
    IRepository<AccessCodeChallenge, Guid> challenges,
    IUnitOfWork unitOfWork,
    IViewerSecrets secrets,
    TimeProvider timeProvider) : IEmailComposer
{
    /// <inheritdoc />
    public string Template => EmailTemplates.AccessCode;

    /// <inheritdoc />
    public async Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var challengeId = EmailData.GuidOf(email, "challengeId");
        var challenge = challengeId is { } id ? await challenges.GetByIdAsync(id, cancellationToken) : null;
        var code = secrets.NewCode();
        if (challenge is null || !challenge.IssueCode(secrets.HashCode(code), timeProvider.GetUtcNow()))
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmailContent.GateCode(email.To, code);
    }
}

/// <summary>
/// Asks for access to a locked player (<c>POST /public/access-requests</c>). Public. A signed-in viewer
/// (<paramref name="AccountToken"/>) sends in one click with the session's email; a visitor proves their email with a
/// code and passes Turnstile. Checks run in this order: input, player, session or Turnstile, code, a repeat while pending,
/// the per-email and per-player limits.
/// </summary>
/// <param name="PlayerSlug">The player asked for.</param>
/// <param name="About">What the visitor writes about themselves.</param>
/// <param name="Email">The email, for a visitor.</param>
/// <param name="Code">The 6-digit code, for a visitor.</param>
/// <param name="TurnstileToken">The Turnstile token, for a visitor.</param>
/// <param name="AccountToken">The signed-in viewer's session token, or <see langword="null"/>.</param>
/// <param name="ClientIp">The visitor's address, for Turnstile.</param>
public sealed record SubmitAccessRequestCommand(
    string? PlayerSlug,
    string? About,
    string? Email,
    string? Code,
    string? TurnstileToken,
    string? AccountToken,
    string? ClientIp) : ICommand<AccessRequestOutcome>;

/// <summary>The outcome of an access request: sent, or why the code did not verify.</summary>
/// <param name="Sent">Whether the request was made (or already pending).</param>
/// <param name="FailureCode">Why the code failed (<c>email_code.*</c>), or <see langword="null"/>.</param>
/// <param name="FailureMessage">A message for the visitor, or <see langword="null"/>.</param>
/// <param name="AttemptsLeft">How many tries are left.</param>
public sealed record AccessRequestOutcome(bool Sent, string? FailureCode, string? FailureMessage, int AttemptsLeft)
{
    /// <summary>The request was made.</summary>
    public static AccessRequestOutcome Success { get; } = new(true, null, null, 0);

    /// <summary>Describes a code that did not verify.</summary>
    /// <param name="check">What the check found.</param>
    /// <param name="attemptsLeft">How many tries are left.</param>
    /// <returns>The outcome.</returns>
    public static AccessRequestOutcome Failed(EmailCodeCheck check, int attemptsLeft)
    {
        var failure = EmailCodeVerification.Failed(check, attemptsLeft);
        return new AccessRequestOutcome(false, failure.FailureCode, failure.FailureMessage, failure.AttemptsLeft);
    }
}

/// <summary>The form is complete; a visitor's email and code are checked here, their formats in the handler.</summary>
public sealed class SubmitAccessRequestCommandValidator : AbstractValidator<SubmitAccessRequestCommand>
{
    /// <summary>The longest Turnstile token Cloudflare issues.</summary>
    public const int MaxTurnstileTokenLength = 2048;

    /// <summary>Builds the rules.</summary>
    public SubmitAccessRequestCommandValidator()
    {
        RuleFor(command => command.PlayerSlug).NotEmpty().MaximumLength(120).OverridePropertyName("playerSlug");
        RuleFor(command => command.About)
            .Must(about => !string.IsNullOrWhiteSpace(about) && about.Trim().Length <= AccessRequest.MaxAboutLength)
            .WithMessage($"Describe yourself in 1 to {AccessRequest.MaxAboutLength} characters.")
            .OverridePropertyName("about");
        RuleFor(command => command.AccountToken).MaximumLength(SignInRules.MaxTokenLength).OverridePropertyName("accountToken");
        RuleFor(command => command.TurnstileToken).MaximumLength(MaxTurnstileTokenLength).OverridePropertyName("turnstileToken");
        When(command => string.IsNullOrWhiteSpace(command.AccountToken), () =>
        {
            RuleFor(command => command.Email).NotEmpty().WithMessage("Enter your email.").OverridePropertyName("email");
            RuleFor(command => command.Code)
                .NotEmpty()
                .Matches($"^[0-9]{{{AccessCodeChallenge.CodeLength}}}$")
                .WithMessage("Enter all 6 digits.")
                .OverridePropertyName("code");
        });
    }
}

/// <summary>Handles <see cref="SubmitAccessRequestCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="requests">The access requests.</param>
/// <param name="challenges">The visitor codes.</param>
/// <param name="sessions">The account sessions (P-5); a viewer's is the signed-in one.</param>
/// <param name="secrets">The secrets.</param>
/// <param name="turnstile">Checks Turnstile tokens.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class SubmitAccessRequestCommandHandler(
    IRepository<Player, Guid> players,
    IRepository<AccessRequest, Guid> requests,
    IRepository<AccessCodeChallenge, Guid> challenges,
    IRepository<AccountSession, Guid> sessions,
    IViewerSecrets secrets,
    ITurnstileVerifier turnstile,
    TimeProvider timeProvider,
    ILogger<SubmitAccessRequestCommandHandler> logger) : ICommandHandler<SubmitAccessRequestCommand, AccessRequestOutcome>
{
    /// <summary>Runs the checks in order and stores one pending request.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Sent, or why the code did not verify (a wrong code is saved as a used try).</returns>
    /// <exception cref="NotFoundException">No such published player.</exception>
    /// <exception cref="SignInRequiredException">The signed-in viewer's session is not live.</exception>
    /// <exception cref="DomainException"><c>turnstile.failed</c>, <c>email.invalid</c> or <c>access_request.about_invalid</c>.</exception>
    /// <exception cref="TooManyRequestsException">A limit was reached.</exception>
    public async Task<AccessRequestOutcome> Handle(SubmitAccessRequestCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var player = await players.RequirePublishedAsync(request.PlayerSlug, cancellationToken);

        EmailAddress email;
        AccessRequestSource source;
        if (!string.IsNullOrWhiteSpace(request.AccountToken))
        {
            var session = await SignInRules.FindLiveSessionAsync(sessions, secrets, SignInAudience.Viewer, request.AccountToken, now, cancellationToken)
                ?? throw new SignInRequiredException();
            email = session.Email;
            source = AccessRequestSource.SignedIn;
        }
        else
        {
            email = EmailAddress.Create(request.Email);
            source = AccessRequestSource.EmailCode;
            if (!await turnstile.VerifyAsync(request.TurnstileToken, request.ClientIp, cancellationToken))
            {
                PublicAccessRules.LogTurnstileFailed(logger, player.Id);
                throw new DomainException(PublicAccessRules.TurnstileFailedCode, "We could not confirm you are not a robot. Try again.");
            }

            var failure = await CheckCodeAsync(player.Id, email, request.Code!, now, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
        }

        if (await requests.AnyAsync(new PendingAccessRequestSpec(email, player.Id), cancellationToken))
        {
            return AccessRequestOutcome.Success;
        }

        await EnsureWithinLimitsAsync(player.Id, email, now, cancellationToken);
        var made = AccessRequest.Submit(player.Id, email, request.About, source, now);
        requests.Add(made);
        PublicAccessRules.LogRequestMade(logger, made.Id, player.Id, source);
        return AccessRequestOutcome.Success;
    }

    /// <summary>Checks the newest code for the email.</summary>
    /// <param name="playerId">The player, for the log.</param>
    /// <param name="email">The email.</param>
    /// <param name="typed">The typed code.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="null"/> when the code verified, otherwise the outcome to answer with.</returns>
    private async Task<AccessRequestOutcome?> CheckCodeAsync(Guid playerId, EmailAddress email, string typed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var challenge = await challenges.FirstOrDefaultAsync(new LatestAccessCodeSpec(email), cancellationToken);
        if (challenge is null)
        {
            PublicAccessRules.LogCodeFailed(logger, playerId, EmailCodeCheck.Expired, 0);
            return AccessRequestOutcome.Failed(EmailCodeCheck.Expired, 0);
        }

        var check = challenge.Check(secrets.CodeMatches(typed, challenge.CodeHash), now);
        if (check == EmailCodeCheck.Verified)
        {
            return null;
        }

        PublicAccessRules.LogCodeFailed(logger, playerId, check, challenge.AttemptsLeft);
        return AccessRequestOutcome.Failed(check, challenge.AttemptsLeft);
    }

    /// <summary>Refuses a request over the per-email or per-player limit.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="email">The email.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>A task that completes when the request may go ahead.</returns>
    /// <exception cref="TooManyRequestsException">A limit was reached.</exception>
    private async Task EnsureWithinLimitsAsync(Guid playerId, EmailAddress email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var byEmail = await requests.ListAsync(new AccessRequestsByEmailSinceSpec(email, now - PublicAccessRules.EmailWindow), cancellationToken);
        if (byEmail.Count >= PublicAccessRules.MaxRequestsPerEmail)
        {
            Refuse(playerId, "email", byEmail.Min(item => item.CreatedAt) + PublicAccessRules.EmailWindow - now);
        }

        var forPlayer = await requests.ListAsync(new AccessRequestsForPlayerSinceSpec(playerId, now - PublicAccessRules.PlayerWindow), cancellationToken);
        if (forPlayer.Count >= PublicAccessRules.MaxRequestsPerPlayer)
        {
            Refuse(playerId, "player", forPlayer.Min(item => item.CreatedAt) + PublicAccessRules.PlayerWindow - now);
        }
    }

    /// <summary>Logs and throws the refusal.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="limit">Which limit.</param>
    /// <param name="wait">How long to wait.</param>
    /// <exception cref="TooManyRequestsException">Always.</exception>
    private void Refuse(Guid playerId, string limit, TimeSpan wait)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        PublicAccessRules.LogRequestLimit(logger, playerId, limit, seconds);
        throw new TooManyRequestsException(PublicAccessRules.TooManyRequestsCode, "Too many requests. Try again later.", TimeSpan.FromSeconds(seconds));
    }
}
