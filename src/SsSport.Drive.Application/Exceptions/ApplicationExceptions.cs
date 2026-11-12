namespace SsSport.Drive.Application.Exceptions;

/// <summary>
/// A request failed validation. The API returns it as 400 ValidationProblemDetails with <see cref="Errors"/>.
/// </summary>
public sealed class RequestValidationException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="errors">Error messages grouped by property name.</param>
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;

    /// <summary>Gets the error messages, grouped by property name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>The request needs a signed-in caller and there is none. The API returns 401.</summary>
public sealed class UnauthenticatedException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public UnauthenticatedException()
        : base("Sign-in is required.")
    {
    }
}

/// <summary>
/// Someone else changed the same record while this request was working on it, so the save was refused to avoid
/// overwriting their change (optimistic concurrency, D-13). The API returns 409 with <see cref="Code"/>; the client
/// reloads and tries again.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>The stable code clients see.</summary>
    public const string Code = "concurrency.conflict";

    /// <summary>Initializes the exception.</summary>
    /// <param name="innerException">The data store's own conflict error.</param>
    public ConcurrencyConflictException(Exception innerException)
        : base("This record was changed by someone else at the same time. Load it again and retry.", innerException)
    {
    }
}

/// <summary>An outside service the request needs (e.g. Google Drive) cannot be reached. The API returns 503 with <see cref="Code"/>.</summary>
public sealed class ServiceUnavailableException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="code">A stable code for clients, e.g. <c>drive.unavailable</c>.</param>
    /// <param name="message">A short explanation.</param>
    public ServiceUnavailableException(string code, string message)
        : base(message) => Code = code;

    /// <summary>Gets the stable code.</summary>
    public string Code { get; }
}

/// <summary>The requested record does not exist (or has nothing to return yet). The API returns 404.</summary>
public sealed class NotFoundException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="what">What was not found, e.g. <c>Player</c>.</param>
    public NotFoundException(string what)
        : base($"{what} was not found.")
    {
    }
}

/// <summary>
/// A personal link does not open: unknown, malformed, paused, revoked or expired. The API returns the same 404 with
/// <see cref="Code"/> for every case, so nobody can tell an unknown code from a stopped link (L-1).
/// </summary>
public sealed class ShareLinkUnavailableException : Exception
{
    /// <summary>The stable code clients see.</summary>
    public const string Code = "share_link.unavailable";

    /// <summary>Initializes the exception.</summary>
    public ShareLinkUnavailableException()
        : base("This link has expired or is not available. Ask SS-SPORT for a new link.")
    {
    }
}

/// <summary>The caller asked too often (e.g. for email codes, L-2). The API returns 429 with <see cref="Code"/> and <c>Retry-After</c>.</summary>
public sealed class TooManyRequestsException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="code">A stable code for clients, e.g. <c>email_code.too_many</c>.</param>
    /// <param name="message">A short explanation.</param>
    /// <param name="retryAfter">How long to wait; at least one second is reported.</param>
    public TooManyRequestsException(string code, string message, TimeSpan retryAfter)
        : base(message)
    {
        Code = code;
        RetryAfter = retryAfter;
    }

    /// <summary>Gets the stable code.</summary>
    public string Code { get; }

    /// <summary>Gets how long to wait before trying again.</summary>
    public TimeSpan RetryAfter { get; }
}

/// <summary>
/// The viewer has no trusted device or session for a link and must pass the email gate (L-2). The API returns 401 with
/// <see cref="Code"/>.
/// </summary>
public sealed class ViewerVerificationRequiredException : Exception
{
    /// <summary>The stable code clients see.</summary>
    public const string Code = "viewer.verification_required";

    /// <summary>Initializes the exception.</summary>
    public ViewerVerificationRequiredException()
        : base("Confirm your email to open this link.")
    {
    }
}

/// <summary>
/// A forwarded email verified on a link, but no admin has kept it yet (L-3), so the link does not open. The API returns 403
/// with <see cref="Code"/>; the gate shows the "waiting for approval" step.
/// </summary>
public sealed class ForwardPendingException : Exception
{
    /// <summary>The stable code clients see.</summary>
    public const string Code = "share_link.forward_pending";

    /// <summary>Initializes the exception.</summary>
    public ForwardPendingException()
        : base("SS-SPORT is checking this request. The link opens here once it is approved.")
    {
    }
}

/// <summary>The caller is signed in but lacks a required role. The API returns 403.</summary>
public sealed class ForbiddenAccessException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public ForbiddenAccessException()
        : base("The caller does not have a required role.")
    {
    }
}
