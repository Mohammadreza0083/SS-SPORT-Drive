using System.Text.RegularExpressions;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Archive;

/// <summary>What an archive folder name suggests about its player (M-2). Only a suggestion: every player still waits for an admin.</summary>
/// <param name="HasPrefix">Whether the name starts with an <c>SS-SPORT</c> prefix, in any of its archive spellings.</param>
/// <param name="Name">The player name under the player rules (Title Cased), or <see langword="null"/> when the rules refuse it.</param>
/// <param name="Positions">Zero, one or two positions, the primary one first.</param>
public sealed record ArchiveFolderSuggestion(bool HasPrefix, string? Name, IReadOnlyList<Position> Positions);

/// <summary>
/// Reads archive folder names (decisions §4a): <c>&lt;prefix&gt; &lt;Full Name&gt;／&lt;position&gt;</c>, where the prefix is
/// <c>SS-SPORT </c>, <c>SS-SPORT - </c>, <c>SS-Sport </c> or the typo <c>SS-SPRT </c>, the separator a full-width slash (or a
/// plain one) with or without spaces, and the position <c>OH</c>, <c>OPP</c>, <c>MB</c>, <c>Setter</c>, <c>Libero</c> or two of
/// them joined by <c>-</c>. Category folders on top are <c>Men Volleyball</c> and <c>Women Volleyball</c>.
/// </summary>
public static partial class ArchiveFolderName
{
    /// <summary>Parses a player folder name.</summary>
    /// <param name="folderName">The Drive folder name.</param>
    /// <returns>The suggestion.</returns>
    public static ArchiveFolderSuggestion Parse(string? folderName)
    {
        var text = (folderName ?? string.Empty).Trim();
        var prefix = Prefix().Match(text);
        if (prefix.Success)
        {
            text = text[prefix.Length..];
        }

        var slash = text.IndexOfAny(['／', '/']);
        var namePart = slash < 0 ? text : text[..slash];
        var positionPart = slash < 0 ? string.Empty : text[(slash + 1)..];
        return new ArchiveFolderSuggestion(prefix.Success, NameOrNull(namePart), Positions(positionPart));
    }

    /// <summary>Reads a top-level category folder.</summary>
    /// <param name="folderName">The folder name.</param>
    /// <param name="category">Men or Women when it is a category folder.</param>
    /// <returns><see langword="true"/> for <c>Men Volleyball</c> or <c>Women Volleyball</c>, in any case.</returns>
    public static bool TryReadCategory(string? folderName, out PlayerCategory category)
    {
        var name = string.Join(' ', (folderName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.Equals(name, "Men Volleyball", StringComparison.OrdinalIgnoreCase))
        {
            category = PlayerCategory.Men;
            return true;
        }

        if (string.Equals(name, "Women Volleyball", StringComparison.OrdinalIgnoreCase))
        {
            category = PlayerCategory.Women;
            return true;
        }

        category = default;
        return false;
    }

    /// <summary>Applies the player name rules, or gives up.</summary>
    /// <param name="raw">The name part of the folder name.</param>
    /// <returns>The normalised name, or <see langword="null"/>.</returns>
    private static string? NameOrNull(string raw)
    {
        try
        {
            return FullName.Create(raw).Value;
        }
        catch (DomainException)
        {
            return null;
        }
    }

    /// <summary>Reads up to two distinct positions joined by dashes; unknown words are skipped.</summary>
    /// <param name="raw">The part after the slash.</param>
    /// <returns>The positions, the primary one first.</returns>
    private static List<Position> Positions(string raw)
    {
        var positions = new List<Position>();
        foreach (var part in raw.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (PositionParser.TryParse(part, out var position) && !positions.Contains(position) && positions.Count < PlayerPositions.MaxCount)
            {
                positions.Add(position);
            }
        }

        return positions;
    }

    /// <summary>The <c>SS-SPORT</c> prefix in its archive spellings, with an optional dash after it.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^SS\s*-\s*SPO?RT\b\s*(?:-\s*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Prefix();
}
