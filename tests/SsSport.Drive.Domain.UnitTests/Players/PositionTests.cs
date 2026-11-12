using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>Positions: S, OH, OPP, MB, L, DS; a player has 1–2, the first being primary.</summary>
public sealed class PositionTests
{
    /// <summary>Codes and the spellings seen in Drive folder names are recognised.</summary>
    /// <param name="input">The raw text.</param>
    /// <param name="expected">The position.</param>
    [Theory]
    [InlineData("S", Position.Setter)]
    [InlineData("setter", Position.Setter)]
    [InlineData("OH", Position.OutsideHitter)]
    [InlineData("Outside Hitter", Position.OutsideHitter)]
    [InlineData("opp", Position.Opposite)]
    [InlineData("Opposite", Position.Opposite)]
    [InlineData("MB", Position.MiddleBlocker)]
    [InlineData("Middle Blocker", Position.MiddleBlocker)]
    [InlineData("L", Position.Libero)]
    [InlineData(" Libero ", Position.Libero)]
    [InlineData("DS", Position.DefensiveSpecialist)]
    [InlineData("Defensive Specialist", Position.DefensiveSpecialist)]
    public void Parses_codes_and_names(string input, Position expected)
    {
        PositionParser.TryParse(input, out var position).Should().BeTrue();
        position.Should().Be(expected);
    }

    /// <summary>Unknown text is not a position.</summary>
    [Fact]
    public void Rejects_unknown_text()
    {
        PositionParser.TryParse("Coach", out _).Should().BeFalse();
    }

    /// <summary>Every position has its short code.</summary>
    [Fact]
    public void Exposes_short_codes()
    {
        Enum.GetValues<Position>().Select(position => position.Code())
            .Should().Equal("S", "OH", "OPP", "MB", "L", "DS");
    }

    /// <summary>Each position has the English label the player page shows next to the code.</summary>
    [Fact]
    public void Exposes_display_labels()
    {
        Enum.GetValues<Position>().Select(position => position.Label())
            .Should().Equal("Setter", "Outside hitter", "Opposite", "Middle blocker", "Libero", "Defensive specialist");
    }

    /// <summary>One or two distinct positions are accepted, in order; the first is primary.</summary>
    [Fact]
    public void Keeps_order_with_the_first_as_primary()
    {
        var positions = PlayerPositions.Create([Position.Opposite, Position.OutsideHitter]);

        positions.Primary.Should().Be(Position.Opposite);
        positions.Secondary.Should().Be(Position.OutsideHitter);
        positions.Items.Should().Equal(Position.Opposite, Position.OutsideHitter);
    }

    /// <summary>A single position has no secondary.</summary>
    [Fact]
    public void Single_position_has_no_secondary()
    {
        PlayerPositions.Create([Position.Libero]).Secondary.Should().BeNull();
    }

    /// <summary>At least one position is required.</summary>
    [Fact]
    public void Requires_a_position()
    {
        var create = () => PlayerPositions.Create([]);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.positions_required");
    }

    /// <summary>More than two positions are rejected.</summary>
    [Fact]
    public void Rejects_more_than_two()
    {
        var create = () => PlayerPositions.Create([Position.Setter, Position.Opposite, Position.Libero]);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.too_many_positions");
    }

    /// <summary>The same position twice is rejected.</summary>
    [Fact]
    public void Rejects_duplicates()
    {
        var create = () => PlayerPositions.Create([Position.MiddleBlocker, Position.MiddleBlocker]);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.duplicate_position");
    }

    /// <summary>Two position sets with the same items in the same order are equal.</summary>
    [Fact]
    public void Has_value_equality()
    {
        PlayerPositions.Create([Position.Setter]).Should().Be(PlayerPositions.Create([Position.Setter]));
        PlayerPositions.Create([Position.Setter, Position.Libero]).Should().NotBe(PlayerPositions.Create([Position.Libero, Position.Setter]));
    }
}
