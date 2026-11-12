using System.Globalization;
using System.Text.RegularExpressions;

namespace SsSport.Drive.Application.Scouting;

/// <summary>
/// Reads a DataVolley scout file (<c>.dvw</c>, V-8). Sections start with a <c>[3NAME]</c> line and fields are separated
/// by <c>;</c>. Only what the viewer page shows is read: the match header, both rosters and the action codes of
/// <c>[3SCOUT]</c>. Everything else is ignored.
/// </summary>
public static partial class DataVolleyFile
{
    /// <summary>The date formats DataVolley writes in the header.</summary>
    private static readonly string[] _dateFormats = ["dd/MM/yyyy", "dd.MM.yyyy", "d/M/yyyy"];

    /// <summary>Parses a scout file.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The match.</returns>
    /// <exception cref="FormatException">The text is not a scout file (no two teams or no scout section). The text is never put in the message.</exception>
    public static ScoutMatch Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sections = Split(text.TrimStart('﻿'));
        if (!sections.TryGetValue("3SCOUT", out var scout) || !sections.TryGetValue("3TEAMS", out var teams) || teams.Count < 2)
        {
            throw new FormatException("Not a DataVolley scout file.");
        }

        return new ScoutMatch
        {
            Date = ReadDate(sections.GetValueOrDefault("3MATCH")),
            Home = ReadTeam(teams[0]),
            Away = ReadTeam(teams[1]),
            Sets = ReadSets(sections.GetValueOrDefault("3SET")),
            Players = [.. ReadPlayers(sections.GetValueOrDefault("3PLAYERS-H"), ScoutSide.Home), .. ReadPlayers(sections.GetValueOrDefault("3PLAYERS-V"), ScoutSide.Away)],
            Actions = ReadActions(scout),
        };
    }

    /// <summary>Splits the file into its sections.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The lines of each section, by section name without the brackets.</returns>
    private static Dictionary<string, List<string>> Split(string text)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');
            if (line.Length > 2 && line[0] == '[' && line[^1] == ']')
            {
                current = sections[line[1..^1]] = [];
            }
            else if (current is not null && line.Length > 0)
            {
                current.Add(line);
            }
        }

        return sections;
    }

    /// <summary>Reads the match date from the first line of the header.</summary>
    /// <param name="lines">The <c>[3MATCH]</c> lines.</param>
    /// <returns>The date, or <see langword="null"/>.</returns>
    private static DateOnly? ReadDate(List<string>? lines) =>
        lines is { Count: > 0 } && DateOnly.TryParseExact(lines[0].Split(';')[0].Trim(), _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Reads a team line: code, name, sets won.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The team.</returns>
    private static ScoutTeam ReadTeam(string line)
    {
        var fields = line.Split(';');
        _ = int.TryParse(fields.ElementAtOrDefault(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sets);
        return new ScoutTeam(fields[0].Trim(), fields.ElementAtOrDefault(1)?.Trim() ?? string.Empty, sets);
    }

    /// <summary>Reads the final score of every played set.</summary>
    /// <param name="lines">The <c>[3SET]</c> lines.</param>
    /// <returns>The scores, in order.</returns>
    private static List<ScoutSetScore> ReadSets(List<string>? lines)
    {
        var sets = new List<ScoutSetScore>();
        foreach (var line in lines ?? [])
        {
            var fields = line.Split(';');
            if (!fields[0].Trim().Equals("True", StringComparison.OrdinalIgnoreCase) || fields.Length < 5)
            {
                continue;
            }

            var score = fields[4].Split('-');
            if (score.Length == 2
                && int.TryParse(score[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var home)
                && int.TryParse(score[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var away))
            {
                sets.Add(new ScoutSetScore(home, away));
            }
        }

        return sets;
    }

    /// <summary>Reads a roster.</summary>
    /// <param name="lines">The roster lines.</param>
    /// <param name="side">The team.</param>
    /// <returns>The players.</returns>
    private static IEnumerable<ScoutPlayer> ReadPlayers(List<string>? lines, ScoutSide side)
    {
        foreach (var line in lines ?? [])
        {
            var fields = line.Split(';');
            if (fields.Length > 10 && int.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                var id = fields[8].Trim();
                yield return new ScoutPlayer(side, number, id.Length == 0 ? null : id, fields[9].Trim(), fields[10].Trim());
            }
        }
    }

    /// <summary>Reads the player actions of the scout section and skips every other line.</summary>
    /// <param name="lines">The <c>[3SCOUT]</c> lines.</param>
    /// <returns>The actions, in file order.</returns>
    private static List<ScoutAction> ReadActions(List<string> lines)
    {
        var actions = new List<ScoutAction>();
        foreach (var line in lines)
        {
            var match = ActionCode().Match(line.Split(';')[0]);
            if (match.Success)
            {
                actions.Add(new ScoutAction(
                    match.Groups["side"].Value == "*" ? ScoutSide.Home : ScoutSide.Away,
                    int.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture),
                    SkillOf(match.Groups["skill"].Value[0]),
                    EvaluationOf(match.Groups["eval"].Value[0])));
            }
        }

        return actions;
    }

    /// <summary>The skill of a code letter.</summary>
    /// <param name="code">The letter.</param>
    /// <returns>The skill.</returns>
    private static ScoutSkill SkillOf(char code) => code switch
    {
        'S' => ScoutSkill.Serve,
        'R' => ScoutSkill.Reception,
        'A' => ScoutSkill.Attack,
        'B' => ScoutSkill.Block,
        'D' => ScoutSkill.Dig,
        'E' => ScoutSkill.Set,
        _ => ScoutSkill.Freeball,
    };

    /// <summary>The evaluation of a symbol.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>The evaluation.</returns>
    private static ScoutEvaluation EvaluationOf(char symbol) => symbol switch
    {
        '#' => ScoutEvaluation.Perfect,
        '+' => ScoutEvaluation.Positive,
        '!' => ScoutEvaluation.Ok,
        '-' => ScoutEvaluation.Negative,
        '/' => ScoutEvaluation.Poor,
        _ => ScoutEvaluation.Error,
    };

    /// <summary>A player action code: side, two-digit number, skill, type and evaluation. Anything after the evaluation is ignored.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^(?<side>[*a])(?<num>\d{2})(?<skill>[SRABDEF])(?<type>[HMQTUNO])(?<eval>[#+!\-/=])")]
    private static partial Regex ActionCode();
}
