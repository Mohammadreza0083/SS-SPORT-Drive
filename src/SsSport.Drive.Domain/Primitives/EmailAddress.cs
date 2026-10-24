using System.Text.RegularExpressions;

namespace SsSport.Drive.Domain.Primitives;

/// <summary>
/// An email address, trimmed and lower-cased. <see cref="ToString"/> is masked (<c>s***@club.com</c>), so an address
/// that ends up in a log message is not exposed; use <see cref="Value"/> where the full address is really needed.
/// </summary>
public sealed partial record EmailAddress
{
    /// <summary>The longest address (RFC 5321 path limit).</summary>
    private const int MaxLength = 254;

    /// <summary>Initializes the address with a validated value.</summary>
    /// <param name="value">The normalised address.</param>
    private EmailAddress(string value) => Value = value;

    /// <summary>Gets the full, lower-case address.</summary>
    public string Value { get; }

    /// <summary>Validates an email address.</summary>
    /// <param name="raw">The address in any case.</param>
    /// <returns>The address.</returns>
    /// <exception cref="DomainException"><c>email.invalid</c> for malformed or overlong addresses.</exception>
    public static EmailAddress Create(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length > MaxLength || !Pattern().IsMatch(value))
        {
            throw new DomainException("email.invalid", "Enter a valid email address, e.g. name@club.com.");
        }

        return new EmailAddress(value);
    }

    /// <summary>Returns the masked address: first letter of the local part, then <c>***</c>, then the domain.</summary>
    /// <returns>The masked address.</returns>
    public override string ToString() => $"{Value[0]}***{Value[Value.IndexOf('@', StringComparison.Ordinal)..]}";

    /// <summary>One <c>@</c>, no whitespace, and a domain with at least one dot.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
