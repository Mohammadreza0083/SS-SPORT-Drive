namespace SsSport.Drive.Application.Scouting;

/// <summary>One player's numbers for one skill.</summary>
/// <param name="Skill">The skill.</param>
/// <param name="Total">All actions of the skill.</param>
/// <param name="Perfect">Perfect (<c>#</c>).</param>
/// <param name="Positive">Positive (<c>+</c>).</param>
/// <param name="Ok">Ok (<c>!</c>).</param>
/// <param name="Negative">Negative (<c>-</c>).</param>
/// <param name="Poor">Poor (<c>/</c>).</param>
/// <param name="Error">Errors (<c>=</c>).</param>
/// <param name="SuccessRate">Whole percent, or <see langword="null"/> without actions.</param>
/// <param name="Efficiency">Whole percent (can be negative), or <see langword="null"/> without actions.</param>
public sealed record SkillStats(
    ScoutSkill Skill,
    int Total,
    int Perfect,
    int Positive,
    int Ok,
    int Negative,
    int Poor,
    int Error,
    int? SuccessRate,
    int? Efficiency);

/// <summary>
/// Counts one player's actions per skill (V-8). Six skills, always in this order: Serve, Reception, Attack, Block, Dig,
/// Set. Success and efficiency per skill (proposed formulas, pinned by the tests):
/// Serve, Block and Set: success = # / total, efficiency = (# - =) / total;
/// Reception and Dig: success = (# + +) / total, efficiency = (# + + - =) / total;
/// Attack: success = # / total, efficiency = (# - = - /) / total.
/// </summary>
public static class ScoutStats
{
    /// <summary>The shown skills, in order.</summary>
    private static readonly ScoutSkill[] _skills = [ScoutSkill.Serve, ScoutSkill.Reception, ScoutSkill.Attack, ScoutSkill.Block, ScoutSkill.Dig, ScoutSkill.Set];

    /// <summary>Computes a player's stats.</summary>
    /// <param name="match">The match.</param>
    /// <param name="player">The player, from <see cref="ScoutMatch.FindPlayer"/>.</param>
    /// <returns>One entry per shown skill.</returns>
    public static IReadOnlyList<SkillStats> For(ScoutMatch match, ScoutPlayer player)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(player);
        var mine = match.Actions.Where(action => action.Side == player.Side && action.Number == player.Number).ToList();
        return [.. _skills.Select(skill => Count(skill, mine.Where(action => action.Skill == skill).ToList()))];
    }

    /// <summary>Counts one skill.</summary>
    /// <param name="skill">The skill.</param>
    /// <param name="actions">The player's actions of that skill.</param>
    /// <returns>The numbers.</returns>
    private static SkillStats Count(ScoutSkill skill, List<ScoutAction> actions)
    {
        int Of(ScoutEvaluation evaluation) => actions.Count(action => action.Evaluation == evaluation);
        var perfect = Of(ScoutEvaluation.Perfect);
        var positive = Of(ScoutEvaluation.Positive);
        var ok = Of(ScoutEvaluation.Ok);
        var negative = Of(ScoutEvaluation.Negative);
        var poor = Of(ScoutEvaluation.Poor);
        var error = Of(ScoutEvaluation.Error);
        var total = actions.Count;
        if (total == 0)
        {
            return new SkillStats(skill, 0, 0, 0, 0, 0, 0, 0, null, null);
        }

        var passing = skill is ScoutSkill.Reception or ScoutSkill.Dig;
        var success = passing ? perfect + positive : perfect;
        var efficiency = skill switch
        {
            ScoutSkill.Reception or ScoutSkill.Dig => perfect + positive - error,
            ScoutSkill.Attack => perfect - error - poor,
            _ => perfect - error,
        };
        return new SkillStats(skill, total, perfect, positive, ok, negative, poor, error, Percent(success, total), Percent(efficiency, total));
    }

    /// <summary>A share in whole percent, rounded half away from zero.</summary>
    /// <param name="part">The part.</param>
    /// <param name="total">The total (above zero).</param>
    /// <returns>The percent.</returns>
    private static int Percent(int part, int total) => (int)Math.Round(100.0 * part / total, MidpointRounding.AwayFromZero);
}
