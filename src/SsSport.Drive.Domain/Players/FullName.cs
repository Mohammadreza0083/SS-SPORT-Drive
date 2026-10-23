using System.Text;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>
/// A player's full name in Latin script (accented Latin letters allowed), spaces collapsed and Title Cased:
/// <c>"jean-luc o'neil"</c> becomes <c>"Jean-Luc O'Neil"</c>. Letters, spaces, hyphens and apostrophes only.
/// </summary>
public sealed record FullName
{
    /// <summary>The longest name accepted.</summary>
    public const int MaxLength = 100;

    /// <summary>Initializes the name with an already normalised value.</summary>
    /// <param name="value">The normalised name.</param>
    private FullName(string value) => Value = value;

    /// <summary>Gets the normalised name.</summary>
    public string Value { get; }

    /// <summary>Validates and normalises a name.</summary>
    /// <param name="raw">The name as typed or parsed from a folder name.</param>
    /// <returns>The name.</returns>
    /// <exception cref="DomainException"><c>player.name_required</c>, <c>player.name_invalid_characters</c> or <c>player.name_too_long</c>.</exception>
    public static FullName Create(string? raw)
    {
        var collapsed = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace('’', '\'');

        if (collapsed.Length == 0)
        {
            throw new DomainException("player.name_required", "A player name is required.");
        }

        if (collapsed.Length > MaxLength)
        {
            throw new DomainException("player.name_too_long", $"A player name can have at most {MaxLength} characters.");
        }

        if (!collapsed.All(character => IsLatinLetter(character) || IsSeparator(character)))
        {
            throw new DomainException("player.name_invalid_characters", "A player name may only contain Latin letters, spaces, hyphens and apostrophes.");
        }

        return new FullName(ToTitleCase(collapsed));
    }

    /// <summary>Returns the name.</summary>
    /// <returns>The normalised name.</returns>
    public override string ToString() => Value;

    /// <summary>Checks for a letter in the Latin Unicode blocks (Basic Latin to Latin Extended-B, and Latin Extended Additional).</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for Latin letters.</returns>
    private static bool IsLatinLetter(char character) =>
        char.IsLetter(character) && (character <= 'ɏ' || character is >= 'Ḁ' and <= 'ỿ');

    /// <summary>Checks for a character that separates name parts.</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for space, hyphen and apostrophe.</returns>
    private static bool IsSeparator(char character) => character is ' ' or '-' or '\'';

    /// <summary>Upper-cases the first letter of every part and lower-cases the rest.</summary>
    /// <param name="name">The validated name.</param>
    /// <returns>The Title Cased name.</returns>
    private static string ToTitleCase(string name)
    {
        var builder = new StringBuilder(name.Length);
        var startOfPart = true;
        foreach (var character in name)
        {
            builder.Append(startOfPart ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
            startOfPart = IsSeparator(character);
        }

        return builder.ToString();
    }
}
