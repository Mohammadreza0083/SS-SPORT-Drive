using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>
/// A URL-safe, unique handle built from the player's name (<c>arash-tehrani</c>). The Application layer picks
/// the first free one, adding <c>-2</c>, <c>-3</c>… on collisions.
/// </summary>
public sealed partial record Slug
{
    /// <summary>Accented Latin letters (inclusive code point ranges) and their ASCII forms.</summary>
    private static readonly (char First, char Last, string Ascii)[] _foldRanges =
    [
        ('À', 'Å', "a"), ('Æ', 'Æ', "ae"), ('Ç', 'Ç', "c"), ('È', 'Ë', "e"),
        ('Ì', 'Ï', "i"), ('Ð', 'Ð', "d"), ('Ñ', 'Ñ', "n"), ('Ò', 'Ö', "o"),
        ('Ø', 'Ø', "o"), ('Ù', 'Ü', "u"), ('Ý', 'Ý', "y"), ('Þ', 'Þ', "th"),
        ('ß', 'ß', "ss"), ('à', 'å', "a"), ('æ', 'æ', "ae"), ('ç', 'ç', "c"),
        ('è', 'ë', "e"), ('ì', 'ï', "i"), ('ð', 'ð', "d"), ('ñ', 'ñ', "n"),
        ('ò', 'ö', "o"), ('ø', 'ø', "o"), ('ù', 'ü', "u"), ('ý', 'ý', "y"),
        ('þ', 'þ', "th"), ('ÿ', 'ÿ', "y"), ('Ā', 'ą', "a"), ('Ć', 'č', "c"),
        ('Ď', 'đ', "d"), ('Ē', 'ě', "e"), ('Ĝ', 'ģ', "g"), ('Ĥ', 'ħ', "h"),
        ('Ĩ', 'ı', "i"), ('Ĳ', 'ĳ', "ij"), ('Ĵ', 'ĵ', "j"), ('Ķ', 'ĸ', "k"),
        ('Ĺ', 'ł', "l"), ('Ń', 'ŋ', "n"), ('Ō', 'ő', "o"), ('Œ', 'œ', "oe"),
        ('Ŕ', 'ř', "r"), ('Ś', 'š', "s"), ('Ţ', 'ŧ', "t"), ('Ũ', 'ų', "u"),
        ('Ŵ', 'ŵ', "w"), ('Ŷ', 'Ÿ', "y"), ('Ź', 'ž', "z"), ('ſ', 'ſ', "s"),
    ];

    /// <summary>Initializes the slug with a validated value.</summary>
    /// <param name="value">The slug.</param>
    private Slug(string value) => Value = value;

    /// <summary>Gets the slug: lower-case ASCII letters and digits in hyphen-separated parts.</summary>
    public string Value { get; }

    /// <summary>Builds the base slug for a name: accents removed, apostrophes dropped, spaces and hyphens become single hyphens.</summary>
    /// <param name="name">The player's name.</param>
    /// <returns>The slug.</returns>
    public static Slug FromName(FullName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(name.Value.Length);
        foreach (var character in name.Value.ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
            }
            else if (FoldAccent(character) is { } ascii)
            {
                builder.Append(ascii);
            }
            else if (character is not '\'' && (builder.Length == 0 || builder[^1] != '-'))
            {
                // Spaces, hyphens and Latin letters with no ASCII form separate the parts.
                builder.Append('-');
            }
        }

        return Create(builder.ToString().Trim('-'));
    }

    /// <summary>
    /// Folds an accented Latin letter (Latin-1 Supplement and Latin Extended-A) to ASCII, e.g. á → a, ß → ss, ł → l.
    /// Done by table because the app runs with invariant globalization, where Unicode decomposition is unavailable.
    /// </summary>
    /// <param name="character">A lower-case character.</param>
    /// <returns>The ASCII replacement, or <see langword="null"/> when the character is not a foldable letter.</returns>
    private static string? FoldAccent(char character)
    {
        foreach (var (first, last, ascii) in _foldRanges)
        {
            if (character >= first && character <= last)
            {
                return ascii;
            }
        }

        return null;
    }

    /// <summary>Validates a stored slug.</summary>
    /// <param name="value">The slug.</param>
    /// <returns>The slug.</returns>
    /// <exception cref="DomainException"><c>player.slug_invalid</c> when the value is not a well-formed slug.</exception>
    public static Slug Create(string? value)
    {
        if (value is null || !Format().IsMatch(value))
        {
            throw new DomainException("player.slug_invalid", "A slug may only contain lower-case letters and digits separated by single hyphens.");
        }

        return new Slug(value);
    }

    /// <summary>Returns the slug with a collision suffix.</summary>
    /// <param name="number">The suffix, starting at 2.</param>
    /// <returns>The suffixed slug, e.g. <c>arash-tehrani-2</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is below 2.</exception>
    public Slug WithSuffix(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 2);
        return new Slug($"{Value}-{number.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>Returns the slug.</summary>
    /// <returns>The slug text.</returns>
    public override string ToString() => Value;

    /// <summary>Lower-case ASCII letters and digits in hyphen-separated parts.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
