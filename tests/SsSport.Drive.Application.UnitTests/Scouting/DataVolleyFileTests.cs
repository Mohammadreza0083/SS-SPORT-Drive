using SsSport.Drive.Application.Scouting;

namespace SsSport.Drive.Application.UnitTests.Scouting;

/// <summary>
/// Reading a DataVolley scout file (<c>.dvw</c>, V-8). The parser reads only what the viewer page shows: the match
/// header (date, teams, sets won, each played set's final score), both rosters and the action codes of the
/// <c>[3SCOUT]</c> section. Everything else in the file is ignored.
/// </summary>
public sealed class DataVolleyFileTests
{
    /// <summary>
    /// A realistic file. Club Aurora (home) beat Volley Nord 3-1 on 12 March 2026. Arash Tehrani is home #7, Reza Moradi
    /// home #12, Adam Nowicki away #7. Arash's actions:
    /// serve # # + - = (5), reception # # + ! - = (6), attack # # # + - / = (7), block # + = (3), dig + + = (3), no sets.
    /// The scout section also holds point, rotation, set-end and team-error lines, and actions of other players.
    /// </summary>
    internal const string Match = """
        [3DATAVOLLEYSCOUT]
        FILEFORMAT: 2.0
        GENERATOR-DAY: 2026/03/12 21.40.10
        GENERATOR-PRG: Data Volley
        [3MATCH]
        12/03/2026;20.00;2025/2026;Super League;Regular season;;;;;1;Z;0;
        [3TEAMS]
        AUR;Club Aurora;3;Marco Bianchi;Luca Neri;;
        NRD;Volley Nord;1;Jan Kowalski;Piotr Nowak;;
        [3MORE]
        ;;;;;;;
        [3COMMENTS]
        [3SET]
        True; 8- 6;16-12;21-18;25-21;28;
        True; 6- 8;12-16;18-21;22-25;30;
        True; 8- 5;16-11;21-17;25-19;26;
        True; 8- 7;16-14;21-20;26-24;31;
        False;;;;;;
        [3PLAYERS-H]
        0;7;1;1;1;1;1;;AUR-007;Tehrani;Arash;;;;;False;
        0;12;2;2;2;2;2;;AUR-012;Moradi;Reza;;;;;False;
        [3PLAYERS-V]
        1;7;13;1;1;1;1;;NRD-007;Nowicki;Adam;;;;;False;
        1;10;14;2;2;2;2;;NRD-010;Wolski;Piotr;;;;;False;
        [3ATTACKCOMBINATION]
        X5;2;H;Q;Quick;;
        [3SETTERCALL]
        [3WINNINGSYMBOLS]
        [3RESERVE]
        [3VIDEO]
        [3SCOUT]
        **1set;;;;;;;20.00.00;1;1;1;1;0;;
        *z1;;;;;;;20.00.00;1;1;1;1;0;;
        *07SH#;;;;;;;20.01.00;1;1;1;1;100;;
        *p1:0;;;;;;;20.01.00;1;1;1;1;100;;
        *07SH#;;;;;;;20.01.30;1;1;1;1;130;;
        *07SM+;;;;;;;20.02.00;1;1;1;1;160;;
        *07SQ-;;;;;;;20.02.30;1;1;1;1;190;;
        *07SH=;;;;;;;20.03.00;1;1;1;1;220;;
        *07RH#;;;;;;;20.03.30;1;1;1;1;250;;
        *07RH#;;;;;;;20.04.00;1;1;1;1;280;;
        *07RM+;;;;;;;20.04.30;1;1;1;1;310;;
        *07RQ!;;;;;;;20.05.00;1;1;1;1;340;;
        *07RH-;;;;;;;20.05.30;1;1;1;1;370;;
        *07RH=;;;;;;;20.06.00;1;1;1;1;400;;
        *07AH#X5~47;;;;;;;20.06.30;1;1;1;1;430;;
        *07AH#;;;;;;;20.07.00;1;1;1;1;460;;
        *07AM#;;;;;;;20.07.30;1;1;1;1;490;;
        *07AH+;;;;;;;20.08.00;1;1;1;1;520;;
        *07AH-;;;;;;;20.08.30;1;1;1;1;550;;
        *07AQ/;;;;;;;20.09.00;1;1;1;1;580;;
        *07AH=;;;;;;;20.09.30;1;1;1;1;610;;
        *07BH#;;;;;;;20.10.00;1;1;1;1;640;;
        *07BH+;;;;;;;20.10.30;1;1;1;1;670;;
        *07BH=;;;;;;;20.11.00;1;1;1;1;700;;
        *07DH+;;;;;;;20.11.30;1;1;1;1;730;;
        *07DH+;;;;;;;20.12.00;1;1;1;1;760;;
        *07DH=;;;;;;;20.12.30;1;1;1;1;790;;
        *12AH#;;;;;;;20.13.00;1;1;1;1;820;;
        *12EH#;;;;;;;20.13.30;1;1;1;1;850;;
        a07AH#;;;;;;;20.14.00;1;1;1;1;880;;
        a07SH#;;;;;;;20.14.30;1;1;1;1;910;;
        a10SH=;;;;;;;20.15.00;1;1;1;1;940;;
        a$$&H#;;;;;;;20.15.30;1;1;1;1;970;;
        ap1:1;;;;;;;20.15.30;1;1;1;1;970;;
        *P07>LUp;;;;;;;20.16.00;1;1;1;1;1000;;
        **2set;;;;;;;20.30.00;1;1;1;1;2000;;
        """;

    /// <summary>The match header: date, both teams with their sets won, and the final score of every played set.</summary>
    [Fact]
    public void Reads_the_match_header()
    {
        var match = DataVolleyFile.Parse(Match);

        match.Date.Should().Be(new DateOnly(2026, 3, 12));
        match.Home.Should().Be(new ScoutTeam("AUR", "Club Aurora", 3));
        match.Away.Should().Be(new ScoutTeam("NRD", "Volley Nord", 1));
        match.Sets.Should().Equal(new ScoutSetScore(25, 21), new ScoutSetScore(22, 25), new ScoutSetScore(25, 19), new ScoutSetScore(26, 24));
    }

    /// <summary>Both rosters: side, shirt number, DataVolley player id and names.</summary>
    [Fact]
    public void Reads_both_rosters()
    {
        var match = DataVolleyFile.Parse(Match);

        match.Players.Should().Equal(
            new ScoutPlayer(ScoutSide.Home, 7, "AUR-007", "Tehrani", "Arash"),
            new ScoutPlayer(ScoutSide.Home, 12, "AUR-012", "Moradi", "Reza"),
            new ScoutPlayer(ScoutSide.Away, 7, "NRD-007", "Nowicki", "Adam"),
            new ScoutPlayer(ScoutSide.Away, 10, "NRD-010", "Wolski", "Piotr"));
    }

    /// <summary>
    /// Only player actions are read: <c>{* or a}{two-digit number}{skill}{type}{evaluation}</c>. Points, rotations, set
    /// ends, team errors (<c>$$</c>) and substitutions (<c>P</c>) are skipped.
    /// </summary>
    [Fact]
    public void Reads_only_player_actions()
    {
        var match = DataVolleyFile.Parse(Match);

        match.Actions.Should().HaveCount(29);
        match.Actions[0].Should().Be(new ScoutAction(ScoutSide.Home, 7, ScoutSkill.Serve, ScoutEvaluation.Perfect));
        match.Actions.Should().Contain(new ScoutAction(ScoutSide.Home, 12, ScoutSkill.Set, ScoutEvaluation.Perfect));
        match.Actions.Should().Contain(new ScoutAction(ScoutSide.Away, 10, ScoutSkill.Serve, ScoutEvaluation.Error));
        match.Actions.Count(action => action is { Side: ScoutSide.Home, Number: 7 }).Should().Be(24);
    }

    /// <summary>Windows line endings and a byte-order mark are read the same as plain newlines.</summary>
    [Fact]
    public void Reads_windows_line_endings_and_a_byte_order_mark()
    {
        var match = DataVolleyFile.Parse("﻿" + Match.Replace("\n", "\r\n", StringComparison.Ordinal));

        match.Home.Name.Should().Be("Club Aurora");
        match.Actions.Should().HaveCount(29);
    }

    /// <summary>A match with no date in the header still reads; its date is unknown.</summary>
    [Fact]
    public void A_missing_date_is_unknown()
    {
        var match = DataVolleyFile.Parse(Match.Replace("12/03/2026;20.00", ";20.00", StringComparison.Ordinal));

        match.Date.Should().BeNull();
    }

    /// <summary>Text that is not a DataVolley file (no <c>[3TEAMS]</c> or no <c>[3SCOUT]</c> section) is refused.</summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("this is not a scout file")]
    [InlineData("[3DATAVOLLEYSCOUT]\n[3TEAMS]\nAUR;Club Aurora;3;;\nNRD;Volley Nord;1;;\n")]
    [InlineData("[3DATAVOLLEYSCOUT]\n[3SCOUT]\n*07SH#;;\n")]
    public void Refuses_text_that_is_not_a_scout_file(string text)
    {
        var act = () => DataVolleyFile.Parse(text);

        act.Should().Throw<FormatException>();
    }

    /// <summary>
    /// The page's player is found in the file by name, ignoring case, accents, extra spaces and the order of first and
    /// last name.
    /// </summary>
    /// <param name="name">The player's full name on the page.</param>
    /// <param name="number">The shirt number found.</param>
    [Theory]
    [InlineData("Arash Tehrani", 7)]
    [InlineData("TEHRANI  arash", 7)]
    [InlineData("Reza Moradi", 12)]
    [InlineData("Adam Nówicki", 7)]
    public void Finds_the_player_by_name(string name, int number)
    {
        var match = DataVolleyFile.Parse(Match);

        match.FindPlayer(name)!.Number.Should().Be(number);
    }

    /// <summary>A name the file does not hold is not found; a partial name is not enough.</summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("Sara Karimi")]
    [InlineData("Arash")]
    [InlineData("")]
    public void Does_not_find_someone_else(string name)
    {
        var match = DataVolleyFile.Parse(Match);

        match.FindPlayer(name).Should().BeNull();
    }
}
