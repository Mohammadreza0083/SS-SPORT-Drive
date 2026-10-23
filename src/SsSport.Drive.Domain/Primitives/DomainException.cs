namespace SsSport.Drive.Domain.Primitives;

/// <summary>
/// Thrown when an operation would break a domain rule (for example "a player can have at most two positions").
/// The <see cref="Code"/> is stable and machine-readable, so the API can map it to a ProblemDetails response;
/// the message is for logs and developers, not end users.
/// </summary>
public sealed class DomainException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="code">A stable, dot-separated rule code, e.g. <c>player.too_many_positions</c>.</param>
    /// <param name="message">A readable description of the broken rule.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is empty or whitespace.</exception>
    public DomainException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    /// <summary>Gets the stable, machine-readable code of the broken rule.</summary>
    public string Code { get; }
}
