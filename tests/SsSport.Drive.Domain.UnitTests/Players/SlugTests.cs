using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>Slugs are URL-safe forms of the name; collisions get a numeric suffix (-2, -3…).</summary>
public sealed class SlugTests
{
    /// <summary>A slug is lower-case ASCII with hyphens; accents and apostrophes are removed.</summary>
    /// <param name="name">The player name.</param>
    /// <param name="expected">The slug.</param>
    [Theory]
    [InlineData("Arash Tehrani", "arash-tehrani")]
    [InlineData("Jean-Luc O'Neil", "jean-luc-oneil")]
    [InlineData("Yanileydis Sánchez", "yanileydis-sanchez")]
    [InlineData("Daria Drodz", "daria-drodz")]
    [InlineData("Özlem Tuğral", "ozlem-tugral")]
    [InlineData("Łukasz Kaczmarek", "lukasz-kaczmarek")]
    [InlineData("Jürgen Groß", "jurgen-gross")]
    public void Is_built_from_the_name(string name, string expected)
    {
        Slug.FromName(FullName.Create(name)).Value.Should().Be(expected);
    }

    /// <summary>A collision suffix is appended from 2 upwards.</summary>
    [Fact]
    public void Collision_suffix_starts_at_two()
    {
        var slug = Slug.FromName(FullName.Create("Arash Tehrani"));

        slug.WithSuffix(2).Value.Should().Be("arash-tehrani-2");
        slug.WithSuffix(3).Value.Should().Be("arash-tehrani-3");
    }

    /// <summary>Suffixes below 2 are a programming error.</summary>
    [Fact]
    public void Rejects_suffixes_below_two()
    {
        var suffix = () => Slug.FromName(FullName.Create("Arash Tehrani")).WithSuffix(1);

        suffix.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Stored slugs are re-validated when loaded.</summary>
    /// <param name="value">The stored slug.</param>
    [Theory]
    [InlineData("Arash-Tehrani")]
    [InlineData("arash tehrani")]
    [InlineData("-arash")]
    [InlineData("")]
    public void Rejects_malformed_stored_slugs(string value)
    {
        var create = () => Slug.Create(value);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.slug_invalid");
    }
}
