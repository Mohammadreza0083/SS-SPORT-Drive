using SsSport.Drive.Application.Scouting;

namespace SsSport.Drive.Application.UnitTests.Scouting;

/// <summary>
/// One player's stats per skill from a scout file (V-8, owner-approved 2026-11-12: parsed stats, no raw file). Six
/// skills in a fixed order: Serve, Reception, Attack, Block, Dig, Set. Each has the total and the count of every
/// evaluation (<c>#</c> perfect, <c>+</c> positive, <c>!</c> ok, <c>-</c> negative, <c>/</c> poor, <c>=</c> error),
/// a success rate and an efficiency in whole percent (rounded half away from zero), or <see langword="null"/> when the
/// player has no action of that skill:
/// <list type="table">
/// <item><term>Serve</term><description>success = # / total (aces); efficiency = (# − =) / total.</description></item>
/// <item><term>Reception</term><description>success = (# + +) / total (positive); efficiency = (# + + − =) / total.</description></item>
/// <item><term>Attack</term><description>success = # / total (kills); efficiency = (# − = − /) / total.</description></item>
/// <item><term>Block</term><description>success = # / total; efficiency = (# − =) / total.</description></item>
/// <item><term>Dig</term><description>success = (# + +) / total; efficiency = (# + + − =) / total.</description></item>
/// <item><term>Set</term><description>success = # / total; efficiency = (# − =) / total.</description></item>
/// </list>
/// Freeball actions are not shown.
/// </summary>
public sealed class ScoutStatsTests
{
    /// <summary>The stats of Arash Tehrani (home #7) in the sample file.</summary>
    /// <returns>The stats by skill.</returns>
    private static Dictionary<ScoutSkill, SkillStats> Arash()
    {
        var match = DataVolleyFile.Parse(DataVolleyFileTests.Match);
        return ScoutStats.For(match, match.FindPlayer("Arash Tehrani")!).ToDictionary(stats => stats.Skill);
    }

    /// <summary>All six skills, always, in the page's order.</summary>
    [Fact]
    public void Has_six_skills_in_order()
    {
        var match = DataVolleyFile.Parse(DataVolleyFileTests.Match);

        ScoutStats.For(match, match.FindPlayer("Arash Tehrani")!).Select(stats => stats.Skill)
            .Should().Equal(ScoutSkill.Serve, ScoutSkill.Reception, ScoutSkill.Attack, ScoutSkill.Block, ScoutSkill.Dig, ScoutSkill.Set);
    }

    /// <summary>Serve: # # + - = → 5 serves, 2 aces, 1 error; 40 % aces, efficiency (2 − 1) / 5 = 20 %.</summary>
    [Fact]
    public void Serve()
    {
        Arash()[ScoutSkill.Serve].Should().Be(new SkillStats(ScoutSkill.Serve, Total: 5, Perfect: 2, Positive: 1, Ok: 0, Negative: 1, Poor: 0, Error: 1, SuccessRate: 40, Efficiency: 20));
    }

    /// <summary>Reception: # # + ! - = → 50 % positive, efficiency (2 + 1 − 1) / 6 = 33 %.</summary>
    [Fact]
    public void Reception()
    {
        Arash()[ScoutSkill.Reception].Should().Be(new SkillStats(ScoutSkill.Reception, 6, 2, 1, 1, 1, 0, 1, 50, 33));
    }

    /// <summary>Attack: # # # + - / = → 3 / 7 = 43 % kills, efficiency (3 − 1 − 1) / 7 = 14 %.</summary>
    [Fact]
    public void Attack()
    {
        Arash()[ScoutSkill.Attack].Should().Be(new SkillStats(ScoutSkill.Attack, 7, 3, 1, 0, 1, 1, 1, 43, 14));
    }

    /// <summary>Block: # + = → 33 %, efficiency 0 %.</summary>
    [Fact]
    public void Block()
    {
        Arash()[ScoutSkill.Block].Should().Be(new SkillStats(ScoutSkill.Block, 3, 1, 1, 0, 0, 0, 1, 33, 0));
    }

    /// <summary>Dig: + + = → (0 + 2) / 3 = 67 %, efficiency (2 − 1) / 3 = 33 %.</summary>
    [Fact]
    public void Dig()
    {
        Arash()[ScoutSkill.Dig].Should().Be(new SkillStats(ScoutSkill.Dig, 3, 0, 2, 0, 0, 0, 1, 67, 33));
    }

    /// <summary>No sets: zeros and no rates (shown as a dash), not 0 %.</summary>
    [Fact]
    public void A_skill_without_actions_has_no_rates()
    {
        Arash()[ScoutSkill.Set].Should().Be(new SkillStats(ScoutSkill.Set, 0, 0, 0, 0, 0, 0, 0, null, null));
    }

    /// <summary>Only this player's actions count: the away #7's attack and serve are not Arash's, though the number is the same.</summary>
    [Fact]
    public void Counts_only_this_side_and_number()
    {
        var match = DataVolleyFile.Parse(DataVolleyFileTests.Match);

        var adam = ScoutStats.For(match, match.FindPlayer("Adam Nowicki")!).ToDictionary(stats => stats.Skill);

        adam[ScoutSkill.Attack].Total.Should().Be(1);
        adam[ScoutSkill.Serve].Total.Should().Be(1);
        adam[ScoutSkill.Reception].Total.Should().Be(0);
    }

    /// <summary>A negative efficiency is kept (more errors than points), rounded half away from zero.</summary>
    [Fact]
    public void Efficiency_can_be_negative()
    {
        var match = DataVolleyFile.Parse(DataVolleyFileTests.Match);

        var piotr = ScoutStats.For(match, match.FindPlayer("Piotr Wolski")!).Single(stats => stats.Skill == ScoutSkill.Serve);

        piotr.Should().Be(new SkillStats(ScoutSkill.Serve, 1, 0, 0, 0, 0, 0, 1, 0, -100));
    }
}
