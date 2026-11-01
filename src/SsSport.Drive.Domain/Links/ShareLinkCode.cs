using System.Security.Cryptography;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Links;

/// <summary>
/// The code in a personal link's URL (<c>/r/{code}</c>): six characters drawn at random from a URL-safe alphabet without
/// look-alikes (no 0/O, 1/l/I), so it cannot be guessed and reads clearly when typed. Codes are case-sensitive.
/// </summary>
public sealed record ShareLinkCode
{
    /// <summary>How many characters a code has.</summary>
    public const int Length = 6;

    /// <summary>The characters a code is made of: letters and digits, without look-alikes.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>Initializes the code with a validated value.</summary>
    /// <param name="value">The code.</param>
    private ShareLinkCode(string value) => Value = value;

    /// <summary>Gets the code.</summary>
    public string Value { get; }

    /// <summary>Draws a new code with a cryptographic random generator.</summary>
    /// <returns>The code. The caller checks that no other link uses it.</returns>
    public static ShareLinkCode Generate() => new(RandomNumberGenerator.GetString(Alphabet, Length));

    /// <summary>Validates a code read from a URL or the database.</summary>
    /// <param name="raw">The code.</param>
    /// <returns>The code.</returns>
    /// <exception cref="DomainException"><c>share_link.code_invalid</c> for the wrong length or other characters.</exception>
    public static ShareLinkCode Create(string? raw)
    {
        if (raw is not { Length: Length } || !raw.All(character => Alphabet.Contains(character, StringComparison.Ordinal)))
        {
            throw new DomainException("share_link.code_invalid", $"A link code is {Length} letters and digits.");
        }

        return new ShareLinkCode(raw);
    }

    /// <summary>Returns the code.</summary>
    /// <returns>The code.</returns>
    public override string ToString() => Value;
}
