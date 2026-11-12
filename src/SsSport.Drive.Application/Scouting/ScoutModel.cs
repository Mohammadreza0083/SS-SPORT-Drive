namespace SsSport.Drive.Application.Scouting;

/// <summary>Which team a scout player or action belongs to.</summary>
public enum ScoutSide
{
    /// <summary>The home team (listed first in the file).</summary>
    Home,

    /// <summary>The away team.</summary>
    Away,
}

/// <summary>A skill in DataVolley's action codes.</summary>
public enum ScoutSkill
{
    /// <summary>Serve (<c>S</c>).</summary>
    Serve,

    /// <summary>Reception (<c>R</c>).</summary>
    Reception,

    /// <summary>Attack (<c>A</c>).</summary>
    Attack,

    /// <summary>Block (<c>B</c>).</summary>
    Block,

    /// <summary>Dig (<c>D</c>).</summary>
    Dig,

    /// <summary>Set (<c>E</c>).</summary>
    Set,

    /// <summary>Freeball (<c>F</c>); read, but not shown.</summary>
    Freeball,
}

/// <summary>How an action went, in DataVolley's symbols.</summary>
public enum ScoutEvaluation
{
    /// <summary>Perfect (<c>#</c>): an ace, a kill, a perfect pass.</summary>
    Perfect,

    /// <summary>Positive (<c>+</c>).</summary>
    Positive,

    /// <summary>Ok (<c>!</c>).</summary>
    Ok,

    /// <summary>Negative (<c>-</c>).</summary>
    Negative,

    /// <summary>Poor (<c>/</c>): a blocked attack, an overpass.</summary>
    Poor,

    /// <summary>Error (<c>=</c>).</summary>
    Error,
}

/// <summary>A team in the match header.</summary>
/// <param name="Code">The team code, e.g. <c>AUR</c>.</param>
/// <param name="Name">The team name.</param>
/// <param name="SetsWon">The sets the team won.</param>
public sealed record ScoutTeam(string Code, string Name, int SetsWon);

/// <summary>The final score of one played set.</summary>
/// <param name="Home">The home team's points.</param>
/// <param name="Away">The away team's points.</param>
public sealed record ScoutSetScore(int Home, int Away);

/// <summary>A player on a team's roster.</summary>
/// <param name="Side">The team.</param>
/// <param name="Number">The shirt number.</param>
/// <param name="PlayerId">The DataVolley player id, or <see langword="null"/>.</param>
/// <param name="LastName">The last name.</param>
/// <param name="FirstName">The first name.</param>
public sealed record ScoutPlayer(ScoutSide Side, int Number, string? PlayerId, string LastName, string FirstName);

/// <summary>One player action from the scout section.</summary>
/// <param name="Side">The team.</param>
/// <param name="Number">The shirt number.</param>
/// <param name="Skill">The skill.</param>
/// <param name="Evaluation">How it went.</param>
public sealed record ScoutAction(ScoutSide Side, int Number, ScoutSkill Skill, ScoutEvaluation Evaluation);
