namespace SsSport.Drive.Application.Abstractions.Messaging;

/// <summary>
/// Marks a command or query that only the system itself sends (a scheduled job, say), never built from outside input, so
/// it needs no validator (decisions §1b rule 3). The architecture tests fail for any other request with input that has
/// no FluentValidation validator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InternalRequestAttribute : Attribute
{
    /// <summary>Marks the request as internal.</summary>
    /// <param name="reason">Why no outside input reaches it, e.g. "Sent only by a scheduled job with the clock's time."</param>
    /// <exception cref="ArgumentException">No reason is given.</exception>
    public InternalRequestAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Gets why no outside input reaches the request.</summary>
    public string Reason { get; }
}
