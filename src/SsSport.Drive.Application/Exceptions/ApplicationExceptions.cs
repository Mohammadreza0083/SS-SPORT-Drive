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

/// <summary>The caller is signed in but lacks a required role. The API returns 403.</summary>
public sealed class ForbiddenAccessException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public ForbiddenAccessException()
        : base("The caller does not have a required role.")
    {
    }
}
