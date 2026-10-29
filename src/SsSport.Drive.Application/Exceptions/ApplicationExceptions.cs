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

/// <summary>The caller is signed in but lacks a required role. The API returns 403.</summary>
public sealed class ForbiddenAccessException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public ForbiddenAccessException()
        : base("The caller does not have a required role.")
    {
    }
}
