using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Domain.UnitTests.Archive;

/// <summary>
/// M-2: the player name and positions suggested from an archive folder name (decisions §4a): every prefix spelling
/// (<c>SS-SPORT </c>, <c>SS-SPORT - </c>, <c>SS-Sport </c>, the typo <c>SS-SPRT </c>), the full-width slash <c>／</c> before the
/// position, extra spaces, <c>Setter</c> / <c>Libero</c>, and two positions joined by <c>-</c> with the first one primary.
/// </summary>
public sealed class ArchiveFolderNameTests
{
    /// <summary>The name is the folder name without its prefix and position suffix, spaces collapsed.</summary>
    /// <param name="folderName">The Drive folder name.</param>
    /// <param name="expected">The suggested name.</param>
    [Theory]
    [InlineData("SS-SPORT Ali Hoshmand", "Ali Hoshmand")]
    [InlineData("SS-SPORT Vitaliy Papazov／ OPP", "Vitaliy Papazov")]
    [InlineData("SS-SPORT - Ivan Valeev／OH-OPP", "Ivan Valeev")]
    [InlineData("SS-Sport  Ivan  Valeev ／ Setter", "Ivan Valeev")]
    [InlineData("SS-SPRT Reza Karimi／Libero", "Reza Karimi")]
    [InlineData("ss-sport Mohammad Taher／MB", "Mohammad Taher")]
    [InlineData("SS-SPORT Amir Ghafour/OH", "Amir Ghafour")]
    [InlineData("  SS-SPORT   Aref Moradi  ", "Aref Moradi")]
    public void Suggests_the_name(string folderName, string expected)
    {
        ArchiveFolderName.Parse(folderName).Name.Should().Be(expected);
    }

    /// <summary>The position suffix becomes one or two positions, the first one primary.</summary>
    /// <param name="folderName">The Drive folder name.</param>
    /// <param name="expected">The position codes.</param>
    [Theory]
    [InlineData("SS-SPORT Vitaliy Papazov／ OPP", new[] { "OPP" })]
    [InlineData("SS-SPORT - Ivan Valeev／OH-OPP", new[] { "OH", "OPP" })]
    [InlineData("SS-Sport Ivan Valeev ／ Setter", new[] { "S" })]
    [InlineData("SS-SPRT Reza Karimi／Libero", new[] { "L" })]
    [InlineData("SS-SPORT Mohammad Taher／MB", new[] { "MB" })]
    [InlineData("SS-SPORT Amir Ghafour／OH-OH", new[] { "OH" })]
    [InlineData("SS-SPORT Amir Ghafour／OH-OPP-MB", new[] { "OH", "OPP" })]
    public void Suggests_the_positions(string folderName, string[] expected)
    {
        ArchiveFolderName.Parse(folderName).Positions.Select(position => position.Code()).Should().Equal(expected);
    }

    /// <summary>A folder without a position, or with one nobody knows, suggests no position (the player waits for one).</summary>
    /// <param name="folderName">The Drive folder name.</param>
    [Theory]
    [InlineData("SS-SPORT Aref Moradi")]
    [InlineData("SS-SPORT Aref Moradi／Coach")]
    [InlineData("SS-SPORT Aref Moradi／")]
    public void Suggests_no_position_when_there_is_none(string folderName)
    {
        ArchiveFolderName.Parse(folderName).Positions.Should().BeEmpty();
    }

    /// <summary>Only folders with the prefix are player folders; the name is still suggested without it.</summary>
    [Fact]
    public void Tells_whether_the_prefix_was_there()
    {
        ArchiveFolderName.Parse("SS-SPORT Ali Hoshmand").HasPrefix.Should().BeTrue();
        ArchiveFolderName.Parse("SS-SPRT Ali Hoshmand").HasPrefix.Should().BeTrue();

        var plain = ArchiveFolderName.Parse("Ali Hoshmand／OH");
        plain.HasPrefix.Should().BeFalse();
        plain.Name.Should().Be("Ali Hoshmand");
        plain.Positions.Should().Equal(Position.OutsideHitter);
    }

    /// <summary>A name the player rules refuse (another script, digits, too long) is not suggested.</summary>
    /// <param name="folderName">The Drive folder name.</param>
    [Theory]
    [InlineData("SS-SPORT علی هوشمند")]
    [InlineData("SS-SPORT Team 2025")]
    [InlineData("SS-SPORT ")]
    [InlineData("SS-SPORT ／OH")]
    public void Suggests_no_name_the_rules_refuse(string folderName)
    {
        ArchiveFolderName.Parse(folderName).Name.Should().BeNull();
    }

    /// <summary>The suggested name follows the player name rules: Title Cased.</summary>
    [Fact]
    public void Title_cases_the_name()
    {
        ArchiveFolderName.Parse("SS-SPORT jean-luc o'neil／OH").Name.Should().Be("Jean-Luc O'Neil");
    }

    /// <summary>The category folders on top of the archive give men's or women's volleyball.</summary>
    /// <param name="folderName">The top-level folder name.</param>
    /// <param name="expected">The category.</param>
    [Theory]
    [InlineData("Men Volleyball", PlayerCategory.Men)]
    [InlineData("men volleyball ", PlayerCategory.Men)]
    [InlineData("Women Volleyball", PlayerCategory.Women)]
    [InlineData("WOMEN VOLLEYBALL", PlayerCategory.Women)]
    public void Reads_the_category_folders(string folderName, PlayerCategory expected)
    {
        ArchiveFolderName.TryReadCategory(folderName, out var category).Should().BeTrue();
        category.Should().Be(expected);
    }

    /// <summary>Other top-level folders are not categories.</summary>
    /// <param name="folderName">The top-level folder name.</param>
    [Theory]
    [InlineData("Beach Volleyball")]
    [InlineData("Old")]
    [InlineData("")]
    public void Ignores_other_top_level_folders(string folderName)
    {
        ArchiveFolderName.TryReadCategory(folderName, out _).Should().BeFalse();
    }
}
