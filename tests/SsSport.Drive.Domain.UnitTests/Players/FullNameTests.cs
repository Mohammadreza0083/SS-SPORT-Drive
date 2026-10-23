using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>Player names are Latin-only, tidied (spaces) and Title Cased.</summary>
public sealed class FullNameTests
{
    /// <summary>Names are trimmed, inner spaces collapsed and Title Cased, including after hyphens and apostrophes.</summary>
    /// <param name="input">The raw name.</param>
    /// <param name="expected">The stored name.</param>
    [Theory]
    [InlineData("arash tehrani", "Arash Tehrani")]
    [InlineData("  SALTANOV   ARTEM  KONSTANTINOVICH ", "Saltanov Artem Konstantinovich")]
    [InlineData("jean-luc o'neil", "Jean-Luc O'Neil")]
    [InlineData("Yanileydis Sánchez", "Yanileydis Sánchez")]
    [InlineData("aj lewis", "Aj Lewis")]
    public void Normalizes_the_name(string input, string expected)
    {
        FullName.Create(input).Value.Should().Be(expected);
    }

    /// <summary>Empty names are rejected.</summary>
    /// <param name="input">The raw name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_names(string input)
    {
        var create = () => FullName.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.name_required");
    }

    /// <summary>Non-Latin letters, digits and symbols are rejected.</summary>
    /// <param name="input">The raw name.</param>
    [Theory]
    [InlineData("علی حسینی")]
    [InlineData("Иван Валеев")]
    [InlineData("Player 23")]
    [InlineData("Ali / OH")]
    public void Rejects_non_latin_or_symbol_characters(string input)
    {
        var create = () => FullName.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.name_invalid_characters");
    }

    /// <summary>Names longer than 100 characters are rejected.</summary>
    [Fact]
    public void Rejects_names_that_are_too_long()
    {
        var create = () => FullName.Create(new string('a', 101));

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.name_too_long");
    }
}
