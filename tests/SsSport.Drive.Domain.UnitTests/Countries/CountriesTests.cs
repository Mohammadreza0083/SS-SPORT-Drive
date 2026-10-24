using SsSport.Drive.Domain.Countries;

namespace SsSport.Drive.Domain.UnitTests.Countries;

/// <summary>The country catalogue is the owner's list: ISO 3166-1 alpha-2 codes plus XK, AC and TA.</summary>
public sealed class CountriesTests
{
    /// <summary>The catalogue holds all 245 countries with unique, upper-case two-letter codes and a name.</summary>
    [Fact]
    public void Catalogue_holds_the_owners_list()
    {
        CountryCatalogue.All.Should().HaveCount(245);
        CountryCatalogue.All.Select(country => country.Code).Should().OnlyHaveUniqueItems()
            .And.AllSatisfy(code => code.Should().MatchRegex("^[A-Z]{2}$"));
        CountryCatalogue.All.Should().OnlyContain(country => country.Name.Length > 0);
    }

    /// <summary>Codes look up their country, ignoring case and spaces; the special codes are included.</summary>
    /// <param name="code">The code.</param>
    /// <param name="name">The expected name.</param>
    [Theory]
    [InlineData("IR", "Iran")]
    [InlineData(" ru ", "Russia")]
    [InlineData("XK", "Kosovo")]
    [InlineData("AC", "Ascension Island")]
    [InlineData("TA", "Tristan da Cunha")]
    [InlineData("CD", "Congo Democratic Republic")]
    public void Codes_find_their_country(string code, string name)
    {
        CountryCatalogue.TryFind(code, out var country).Should().BeTrue();
        country!.Name.Should().Be(name);
    }

    /// <summary>Codes outside the owner's list are not found.</summary>
    /// <param name="code">The code.</param>
    [Theory]
    [InlineData("UM")]
    [InlineData("XX")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_codes_are_not_found(string? code)
    {
        CountryCatalogue.TryFind(code, out var country).Should().BeFalse();
        country.Should().BeNull();
    }
}
