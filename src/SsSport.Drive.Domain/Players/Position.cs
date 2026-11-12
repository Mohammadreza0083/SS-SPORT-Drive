namespace SsSport.Drive.Domain.Players;

/// <summary>A volleyball position. The short codes (S, OH, OPP, MB, L, DS) are what the UI and the bot show.</summary>
public enum Position
{
    /// <summary>Setter (S).</summary>
    Setter,

    /// <summary>Outside hitter (OH).</summary>
    OutsideHitter,

    /// <summary>Opposite (OPP).</summary>
    Opposite,

    /// <summary>Middle blocker (MB).</summary>
    MiddleBlocker,

    /// <summary>Libero (L).</summary>
    Libero,

    /// <summary>Defensive specialist (DS), added by the owner.</summary>
    DefensiveSpecialist,
}

/// <summary>Short codes for positions and parsing of the spellings used in forms and Drive folder names.</summary>
public static class PositionParser
{
    /// <summary>Accepted spellings (case-insensitive) for each position.</summary>
    private static readonly Dictionary<string, Position> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S"] = Position.Setter,
        ["Setter"] = Position.Setter,
        ["OH"] = Position.OutsideHitter,
        ["Outside"] = Position.OutsideHitter,
        ["Outside Hitter"] = Position.OutsideHitter,
        ["OPP"] = Position.Opposite,
        ["Opposite"] = Position.Opposite,
        ["MB"] = Position.MiddleBlocker,
        ["Middle"] = Position.MiddleBlocker,
        ["Middle Blocker"] = Position.MiddleBlocker,
        ["L"] = Position.Libero,
        ["Libero"] = Position.Libero,
        ["DS"] = Position.DefensiveSpecialist,
        ["Defensive Specialist"] = Position.DefensiveSpecialist,
    };

    /// <summary>Gets the English label of a position, as the player page shows it.</summary>
    /// <param name="position">The position.</param>
    /// <returns>Setter, Outside hitter, Opposite, Middle blocker, Libero or Defensive specialist.</returns>
    public static string Label(this Position position) => position switch
    {
        Position.Setter => "Setter",
        Position.OutsideHitter => "Outside hitter",
        Position.Opposite => "Opposite",
        Position.MiddleBlocker => "Middle blocker",
        Position.Libero => "Libero",
        Position.DefensiveSpecialist => "Defensive specialist",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position."),
    };

    /// <summary>Gets the short code of a position.</summary>
    /// <param name="position">The position.</param>
    /// <returns>S, OH, OPP, MB, L or DS.</returns>
    public static string Code(this Position position) => position switch
    {
        Position.Setter => "S",
        Position.OutsideHitter => "OH",
        Position.Opposite => "OPP",
        Position.MiddleBlocker => "MB",
        Position.Libero => "L",
        Position.DefensiveSpecialist => "DS",
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position."),
    };

    /// <summary>Parses a code or a full position name, ignoring case and surrounding spaces.</summary>
    /// <param name="text">The text, e.g. <c>OH</c>, <c>Setter</c> or <c>Libero</c>.</param>
    /// <param name="position">The parsed position.</param>
    /// <returns><see langword="true"/> when the text names a position.</returns>
    public static bool TryParse(string? text, out Position position) =>
        _aliases.TryGetValue(string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), out position);
}
