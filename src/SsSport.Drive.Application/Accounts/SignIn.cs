using Ardalis.Specification;
using FluentValidation;
using Microsoft.Extensions.Logging;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Abstractions.Web;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Accounts;

/// <summary>The rules of magic-link sign-in (A-1, P-5; decisions §7a).</summary>
public static partial class SignInRules
{
    /// <summary>The most sign-in links one email may ask for per audience in <see cref="LinkWindow"/> (proposed).</summary>
    public const int MaxLinksPerWindow = 5;

    /// <summary>The window of the cap (proposed).</summary>
    public static readonly TimeSpan LinkWindow = TimeSpan.FromHours(1);

    /// <summary>The longest token the API looks up; real tokens are 43 characters.</summary>
    public const int MaxTokenLength = 256;

    /// <summary>Gets the email template of an audience's sign-in link.</summary>
    /// <param name="audience">Who signs in.</param>
    /// <returns>The template key.</returns>
    public static string TemplateOf(SignInAudience audience) =>
        audience == SignInAudience.Admin ? EmailTemplates.AdminSignIn : EmailTemplates.ViewerSignIn;

    /// <summary>Finds a live session of an audience by its token.</summary>
    /// <param name="sessions">The sessions.</param>
    /// <param name="secrets">The keyed hashes.</param>
    /// <param name="audience">Who the session must be for.</param>
    /// <param name="token">The session token from the header, or <see langword="null"/>.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The session, or <see langword="null"/> when there is none, it ended or it is for another audience.</returns>
    public static async Task<AccountSession?> FindLiveSessionAsync(
        IRepository<AccountSession, Guid> sessions,
        IViewerSecrets secrets,
        SignInAudience audience,
        string? token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return null;
        }

        var session = await sessions.FirstOrDefaultAsync(new AccountSessionByTokenSpec(secrets.HashToken(token)), cancellationToken);
        return session is not null && session.Audience == audience && session.IsActiveAt(now) ? session : null;
    }

    /// <summary>Logs an admin link asked for an address that is not an active admin's (suspicious activity).</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "An admin sign-in link was asked for an address that is not an active admin's")]
    public static partial void LogNotAnAdmin(ILogger logger);

    /// <summary>Logs a sign-in link request over the hourly cap.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Audience">Who the link was for.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "A {Audience} sign-in link was asked for too often; nothing was sent")]
    public static partial void LogTooMany(ILogger logger, SignInAudience Audience);

    /// <summary>Logs a sign-in token that did not work.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="Audience">Who tried.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "A {Audience} sign-in token was refused")]
    public static partial void LogTokenRefused(ILogger logger, SignInAudience Audience);
}

/// <summary>A user by email address (admin sign-in).</summary>
public sealed class UserByEmailSpec : Specification<User>, ISingleResultSpecification<User>
{
    /// <summary>Filters by email.</summary>
    /// <param name="email">The address.</param>
    public UserByEmailSpec(EmailAddress email) => Query.Where(user => user.Email == email);
}

/// <summary>A sign-in link by the keyed hash of its token.</summary>
public sealed class SignInLinkByTokenSpec : Specification<SignInLink>, ISingleResultSpecification<SignInLink>
{
    /// <summary>Filters by audience and token hash.</summary>
    /// <param name="audience">Who the link is for.</param>
    /// <param name="tokenHash">The keyed hash of the token.</param>
    public SignInLinkByTokenSpec(SignInAudience audience, string tokenHash) =>
        Query.Where(link => link.Audience == audience && link.TokenHash == tokenHash);
}

/// <summary>The links an email asked for since a moment (the hourly cap).</summary>
public sealed class SignInLinksSinceSpec : Specification<SignInLink>
{
    /// <summary>Filters by email, audience and time.</summary>
    /// <param name="email">The address.</param>
    /// <param name="audience">Who the links are for.</param>
    /// <param name="since">The start of the window (UTC).</param>
    public SignInLinksSinceSpec(EmailAddress email, SignInAudience audience, DateTimeOffset since) =>
        Query.Where(link => link.Email == email && link.Audience == audience && link.CreatedAt >= since);
}

/// <summary>A session by the keyed hash of its token.</summary>
public sealed class AccountSessionByTokenSpec : Specification<AccountSession>, ISingleResultSpecification<AccountSession>
{
    /// <summary>Filters by token hash.</summary>
    /// <param name="tokenHash">The keyed hash of the token.</param>
    public AccountSessionByTokenSpec(string tokenHash) => Query.Where(session => session.TokenHash == tokenHash);
}

/// <summary>A viewer account by email.</summary>
public sealed class ViewerAccountByEmailSpec : Specification<ViewerAccount>, ISingleResultSpecification<ViewerAccount>
{
    /// <summary>Filters by email.</summary>
    /// <param name="email">The address.</param>
    public ViewerAccountByEmailSpec(EmailAddress email) => Query.Where(account => account.Email == email);
}

/// <summary>The body of <c>POST /auth/{audience}/link</c>.</summary>
public sealed record SignInLinkFormDto
{
    /// <summary>Gets the email to send the link to.</summary>
    public string? Email { get; init; }
}

/// <summary>The body of <c>POST /auth/{audience}/session</c>.</summary>
public sealed record SignInSessionFormDto
{
    /// <summary>Gets the one-time token from the emailed link.</summary>
    public string? Token { get; init; }
}

/// <summary>A new session, answered when a link is used.</summary>
public sealed record SignInSessionDto
{
    /// <summary>Gets the session token; the web app keeps it in a <c>__Host-</c> cookie and sends it in a header.</summary>
    public required string SessionToken { get; init; }

    /// <summary>Gets when the session runs out.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Gets the signed-in email.</summary>
    public required string Email { get; init; }

    /// <summary>Gets who signed in: <c>Admin</c> or <c>Viewer</c>.</summary>
    public required string Audience { get; init; }
}

/// <summary>Who is signed in.</summary>
public sealed record SignedInDto
{
    /// <summary>Gets the signed-in email.</summary>
    public required string Email { get; init; }

    /// <summary>Gets the admin's display name, or <see langword="null"/> for a viewer.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets who signed in: <c>Admin</c> or <c>Viewer</c>.</summary>
    public required string Audience { get; init; }

    /// <summary>Gets when the session runs out.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>An admin session resolved for authentication: who the admin is, in the terms the claims need.</summary>
/// <param name="UserId">The user id.</param>
/// <param name="TelegramId">The Telegram id.</param>
/// <param name="Role">The role name.</param>
public sealed record AdminSessionDto(Guid UserId, long TelegramId, string Role);

/// <summary>Asks for a sign-in link by email. Always succeeds, so nobody can probe for admins.</summary>
/// <param name="Audience">Who signs in.</param>
/// <param name="Email">The address.</param>
public sealed record RequestSignInLinkCommand(SignInAudience Audience, string? Email) : ICommand;

/// <summary>The email must be filled in and valid (400), not a 422 as at the gate: the sign-in form is a plain form.</summary>
public sealed class RequestSignInLinkCommandValidator : AbstractValidator<RequestSignInLinkCommand>
{
    /// <summary>Builds the rules.</summary>
    public RequestSignInLinkCommandValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("Enter your email.")
            .Must(IsValidEmail).WithMessage("Enter a valid email address, e.g. name@club.com.")
            .OverridePropertyName("email");

    /// <summary>Tells whether the text is a valid email address.</summary>
    /// <param name="value">The text.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    private static bool IsValidEmail(string? value)
    {
        try
        {
            EmailAddress.Create(value);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}

/// <summary>
/// Handles <see cref="RequestSignInLinkCommand"/>: only an active admin's address gets an admin link, any address gets a
/// viewer link, at most 5 per hour. The link is emailed through the queue; its token is drawn in the email job.
/// </summary>
/// <param name="users">The users.</param>
/// <param name="links">The sign-in links.</param>
/// <param name="emails">The email queue.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class RequestSignInLinkCommandHandler(
    IRepository<User, Guid> users,
    IRepository<SignInLink, Guid> links,
    IRepository<OutgoingEmail, Guid> emails,
    TimeProvider timeProvider,
    ILogger<RequestSignInLinkCommandHandler> logger) : ICommandHandler<RequestSignInLinkCommand>
{
    /// <summary>Issues the link and queues its email, or quietly does nothing.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the request is handled.</returns>
    public async Task Handle(RequestSignInLinkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var email = EmailAddress.Create(request.Email);
        if (request.Audience == SignInAudience.Admin
            && await users.FirstOrDefaultAsync(new UserByEmailSpec(email), cancellationToken) is not { IsAdmin: true })
        {
            SignInRules.LogNotAnAdmin(logger);
            return;
        }

        var recent = await links.CountAsync(new SignInLinksSinceSpec(email, request.Audience, now - SignInRules.LinkWindow), cancellationToken);
        if (recent >= SignInRules.MaxLinksPerWindow)
        {
            SignInRules.LogTooMany(logger, request.Audience);
            return;
        }

        var link = SignInLink.Issue(request.Audience, email, now);
        links.Add(link);
        var data = $$"""{"signInLinkId":"{{link.Id:D}}"}""";
        emails.Add(OutgoingEmail.Queue(EmailKind.Code, SignInRules.TemplateOf(request.Audience), email, data, now));
    }
}

/// <summary>
/// Composes a sign-in link email (A-1, P-5): draws the token in the job, stores its keyed hash on the link and returns the
/// mail with the link. A retried job draws a new token, which replaces the one before.
/// </summary>
/// <param name="audience">Who the composer is for.</param>
/// <param name="links">The sign-in links.</param>
/// <param name="unitOfWork">Saves the hash before the mail goes out.</param>
/// <param name="secrets">The keyed hashes.</param>
/// <param name="webLinks">Builds the sign-in address.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class SignInEmailComposer(
    SignInAudience audience,
    IRepository<SignInLink, Guid> links,
    IUnitOfWork unitOfWork,
    IViewerSecrets secrets,
    IWebLinks webLinks,
    TimeProvider timeProvider) : IEmailComposer
{
    /// <inheritdoc />
    public string Template => SignInRules.TemplateOf(audience);

    /// <inheritdoc />
    public async Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var linkId = EmailData.GuidOf(email, "signInLinkId");
        var link = linkId is { } id ? await links.GetByIdAsync(id, cancellationToken) : null;
        var token = secrets.NewToken();
        if (link is null || !link.Arm(secrets.HashToken(token), timeProvider.GetUtcNow()))
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var address = audience == SignInAudience.Admin ? webLinks.AdminSignIn(token) : webLinks.ViewerSignIn(token);
        return EmailContent.SignIn(email.To, address, "15 minutes");
    }
}

/// <summary>Uses a sign-in link and starts a session.</summary>
/// <param name="Audience">Who signs in.</param>
/// <param name="Token">The one-time token from the emailed link.</param>
public sealed record RedeemSignInLinkCommand(SignInAudience Audience, string? Token) : ICommand<SignInSessionDto>;

/// <summary>The token is filled in and not absurdly long (400).</summary>
public sealed class RedeemSignInLinkCommandValidator : AbstractValidator<RedeemSignInLinkCommand>
{
    /// <summary>Builds the rules.</summary>
    public RedeemSignInLinkCommandValidator() =>
        RuleFor(command => command.Token).NotEmpty().MaximumLength(SignInRules.MaxTokenLength).OverridePropertyName("token");
}

/// <summary>
/// Handles <see cref="RedeemSignInLinkCommand"/>: the link works once and in time; an admin must still be an active admin;
/// a viewer gets an account on the first sign-in. Every refusal is the same 401 <c>sign_in.invalid</c>.
/// </summary>
/// <param name="links">The sign-in links.</param>
/// <param name="users">The users.</param>
/// <param name="viewerAccounts">The viewer accounts.</param>
/// <param name="sessions">The sessions.</param>
/// <param name="secrets">The keyed hashes.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class RedeemSignInLinkCommandHandler(
    IRepository<SignInLink, Guid> links,
    IRepository<User, Guid> users,
    IRepository<ViewerAccount, Guid> viewerAccounts,
    IRepository<AccountSession, Guid> sessions,
    IViewerSecrets secrets,
    TimeProvider timeProvider,
    ILogger<RedeemSignInLinkCommandHandler> logger) : ICommandHandler<RedeemSignInLinkCommand, SignInSessionDto>
{
    /// <summary>Redeems the link and starts the session.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The new session.</returns>
    /// <exception cref="SignInInvalidException">The link is unknown, used, expired or not for this audience.</exception>
    public async Task<SignInSessionDto> Handle(RedeemSignInLinkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var link = await links.FirstOrDefaultAsync(new SignInLinkByTokenSpec(request.Audience, secrets.HashToken(request.Token!)), cancellationToken);
        if (link is null || !link.Redeem(now))
        {
            SignInRules.LogTokenRefused(logger, request.Audience);
            throw new SignInInvalidException();
        }

        var subjectId = request.Audience == SignInAudience.Admin
            ? await AdminIdAsync(link.Email, cancellationToken)
            : await ViewerIdAsync(link.Email, now, cancellationToken);
        var token = secrets.NewToken();
        var session = AccountSession.Start(request.Audience, subjectId, link.Email, secrets.HashToken(token), now);
        sessions.Add(session);
        return new SignInSessionDto
        {
            SessionToken = token,
            ExpiresAt = session.ExpiresAt,
            Email = link.Email.Value,
            Audience = request.Audience.ToString(),
        };
    }

    /// <summary>Finds the admin behind an address; they must still be an active admin.</summary>
    /// <param name="email">The address.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user id.</returns>
    /// <exception cref="SignInInvalidException">The address is not an active admin's any more.</exception>
    private async Task<Guid> AdminIdAsync(EmailAddress email, CancellationToken cancellationToken)
    {
        if (await users.FirstOrDefaultAsync(new UserByEmailSpec(email), cancellationToken) is not { IsAdmin: true } admin)
        {
            SignInRules.LogTokenRefused(logger, SignInAudience.Admin);
            throw new SignInInvalidException();
        }

        return admin.Id;
    }

    /// <summary>Finds or makes the viewer's account.</summary>
    /// <param name="email">The address.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The account id.</returns>
    private async Task<Guid> ViewerIdAsync(EmailAddress email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = await viewerAccounts.FirstOrDefaultAsync(new ViewerAccountByEmailSpec(email), cancellationToken);
        if (account is null)
        {
            account = ViewerAccount.Register(email, now);
            viewerAccounts.Add(account);
        }
        else
        {
            account.SignedIn(now);
        }

        return account.Id;
    }
}

/// <summary>Reads who is signed in from a session token.</summary>
/// <param name="Audience">Who the session must be for.</param>
/// <param name="SessionToken">The session token from the header, or <see langword="null"/>.</param>
public sealed record GetSignedInQuery(SignInAudience Audience, string? SessionToken) : IQuery<SignedInDto>;

/// <summary>Handles <see cref="GetSignedInQuery"/>.</summary>
/// <param name="sessions">The sessions.</param>
/// <param name="users">The users.</param>
/// <param name="secrets">The keyed hashes.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetSignedInQueryHandler(
    IRepository<AccountSession, Guid> sessions,
    IRepository<User, Guid> users,
    IViewerSecrets secrets,
    TimeProvider timeProvider) : IQueryHandler<GetSignedInQuery, SignedInDto>
{
    /// <summary>Reads the signed-in person.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Who is signed in.</returns>
    /// <exception cref="SignInRequiredException">There is no live session of this audience, or the admin is no longer an active admin.</exception>
    public async Task<SignedInDto> Handle(GetSignedInQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = await SignInRules.FindLiveSessionAsync(sessions, secrets, request.Audience, request.SessionToken, timeProvider.GetUtcNow(), cancellationToken)
            ?? throw new SignInRequiredException();
        string? displayName = null;
        if (request.Audience == SignInAudience.Admin)
        {
            var admin = await users.GetByIdAsync(session.SubjectId, cancellationToken);
            displayName = admin is { IsAdmin: true } ? admin.DisplayName : throw new SignInRequiredException();
        }

        return new SignedInDto
        {
            Email = session.Email.Value,
            DisplayName = displayName,
            Audience = request.Audience.ToString(),
            ExpiresAt = session.ExpiresAt,
        };
    }
}

/// <summary>Resolves an admin session for request authentication: the same claims the Mini App signature gives.</summary>
/// <param name="SessionToken">The session token from the <c>X-Admin-Session</c> header.</param>
public sealed record ResolveAdminSessionQuery(string? SessionToken) : IQuery<AdminSessionDto?>;

/// <summary>Handles <see cref="ResolveAdminSessionQuery"/>.</summary>
/// <param name="sessions">The sessions.</param>
/// <param name="users">The users.</param>
/// <param name="secrets">The keyed hashes.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ResolveAdminSessionQueryHandler(
    IRepository<AccountSession, Guid> sessions,
    IRepository<User, Guid> users,
    IViewerSecrets secrets,
    TimeProvider timeProvider) : IQueryHandler<ResolveAdminSessionQuery, AdminSessionDto?>
{
    /// <summary>Finds the live admin session and its admin.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The admin, or <see langword="null"/> when the session is missing, ended, expired or its user is no longer an active admin.</returns>
    public async Task<AdminSessionDto?> Handle(ResolveAdminSessionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = await SignInRules.FindLiveSessionAsync(sessions, secrets, SignInAudience.Admin, request.SessionToken, timeProvider.GetUtcNow(), cancellationToken);
        var admin = session is null ? null : await users.GetByIdAsync(session.SubjectId, cancellationToken);
        return admin is { IsAdmin: true } ? new AdminSessionDto(admin.Id, admin.TelegramId, admin.Role.Name) : null;
    }
}

/// <summary>Signs out: ends the session. Always succeeds.</summary>
/// <param name="Audience">Who signs out.</param>
/// <param name="SessionToken">The session token from the header, or <see langword="null"/>.</param>
public sealed record SignOutCommand(SignInAudience Audience, string? SessionToken) : ICommand;

/// <summary>Handles <see cref="SignOutCommand"/>.</summary>
/// <param name="sessions">The sessions.</param>
/// <param name="secrets">The keyed hashes.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class SignOutCommandHandler(
    IRepository<AccountSession, Guid> sessions,
    IViewerSecrets secrets,
    TimeProvider timeProvider) : ICommandHandler<SignOutCommand>
{
    /// <summary>Ends the session if there is a live one.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the session is ended.</returns>
    public async Task Handle(SignOutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var session = await SignInRules.FindLiveSessionAsync(sessions, secrets, request.Audience, request.SessionToken, now, cancellationToken);
        session?.End(now);
    }
}

/// <summary>Validates <see cref="GetSignedInQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetSignedInQueryValidator : AbstractValidator<GetSignedInQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetSignedInQueryValidator()
    {
        RuleFor(r => r.Audience).IsInEnum().OverridePropertyName("audience");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
    }
}

/// <summary>Validates <see cref="ResolveAdminSessionQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class ResolveAdminSessionQueryValidator : AbstractValidator<ResolveAdminSessionQuery>
{
    /// <summary>Builds the rules.</summary>
    public ResolveAdminSessionQueryValidator()
    {
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
    }
}

/// <summary>Validates <see cref="SignOutCommand"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class SignOutCommandValidator : AbstractValidator<SignOutCommand>
{
    /// <summary>Builds the rules.</summary>
    public SignOutCommandValidator()
    {
        RuleFor(r => r.Audience).IsInEnum().OverridePropertyName("audience");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
    }
}
