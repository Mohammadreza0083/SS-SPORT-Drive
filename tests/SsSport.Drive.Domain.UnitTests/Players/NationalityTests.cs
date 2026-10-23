using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>Nationality is an ISO 3166-1 alpha-2 country code.</summary>
public sealed class NationalityTests
{
    /// <summary>Valid codes are accepted in any case and stored upper-case.</summary>
    /// <param name="input">The raw code.</param>
    /// <param name="expected">The stored code.</param>
    [Theory]
    [InlineData("ir", "IR")]
    [InlineData("RU", "RU")]
    [InlineData(" br ", "BR")]
    [InlineData("me", "ME")]
    public void Accepts_iso_codes(string input, string expected)
    {
        Nationality.Create(input).Code.Should().Be(expected);
    }

    /// <summary>Unknown, malformed or alpha-3 codes are rejected.</summary>
    /// <param name="input">The raw code.</param>
    [Theory]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("IRN")]
    [InlineData("I")]
    [InlineData("1R")]
    public void Rejects_invalid_codes(string input)
    {
        var create = () => Nationality.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.nationality_invalid");
    }
}
