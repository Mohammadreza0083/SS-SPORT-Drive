using System.Text;

namespace SsSport.Drive.Application.Scouting;

/// <summary>What the viewer page reads from a DataVolley file: the match header, both rosters and every player action.</summary>
public sealed class ScoutMatch
{
    /// <summary>Gets the match date, or <see langword="null"/> when the header has none.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Gets the home team.</summary>
    public required ScoutTeam Home { get; init; }

    /// <summary>Gets the away team.</summary>
    public required ScoutTeam Away { get; init; }

    /// <summary>Gets the final score of each played set, in order.</summary>
    public required IReadOnlyList<ScoutSetScore> Sets { get; init; }

    /// <summary>Gets both rosters, home first.</summary>
    public required IReadOnlyList<ScoutPlayer> Players { get; init; }

    /// <summary>Gets every player action, in file order.</summary>
    public required IReadOnlyList<ScoutAction> Actions { get; init; }

    /// <summary>
    /// Finds a player by full name, ignoring case, accents, extra spaces and the order of first and last name. A partial
    /// name never matches.
    /// </summary>
    /// <param name="fullName">The name as the player page shows it.</param>
    /// <returns>The first matching player in file order, or <see langword="null"/>.</returns>
    public ScoutPlayer? FindPlayer(string fullName)
    {
        var wanted = Normalise(fullName);
        if (wanted.Length == 0)
        {
            return null;
        }

        return Players.FirstOrDefault(player =>
            Normalise($"{player.FirstName} {player.LastName}") == wanted || Normalise($"{player.LastName} {player.FirstName}") == wanted);
    }

    /// <summary>
    /// Letters with accents and the plain letter each stands for. The host runs with invariant globalization, where
    /// <see cref="string.Normalize()"/> cannot take accents off, so the common Latin ones are listed here.
    /// </summary>
    private static readonly Dictionary<char, char> _plainLetters = BuildPlainLetters();

    /// <summary>Lower-cases, drops accents and collapses whitespace.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalised text.</returns>
    private static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var character in text)
        {
            var lower = char.ToLowerInvariant(character);
            builder.Append(_plainLetters.GetValueOrDefault(lower, lower));
        }

        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Builds the accent table.</summary>
    /// <returns>Each accented letter with its plain letter.</returns>
    private static Dictionary<char, char> BuildPlainLetters()
    {
        var groups = new (char Plain, string Accented)[]
        {
            ('a', "àáâãäåāăą"),
            ('c', "çćč"),
            ('d', "ďđ"),
            ('e', "èéêëēęěė"),
            ('g', "ğ"),
            ('i', "ìíîïīı"),
            ('l', "łľ"),
            ('n', "ńňñ"),
            ('o', "òóôõöøōő"),
            ('r', "řŕ"),
            ('s', "śşšș"),
            ('t', "ťţț"),
            ('u', "ùúûüūůűų"),
            ('y', "ýÿ"),
            ('z', "źżž"),
        };
        return groups.SelectMany(group => group.Accented.Select(accented => (accented, group.Plain))).ToDictionary(pair => pair.accented, pair => pair.Plain);
    }
}
