using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Domain.UnitTests.Assets;

/// <summary>
/// Tags come from the subfolder name first (every spelling found in the real archive), then from file-name keywords;
/// anything else is untagged. Titles come from a match folder when there is one, otherwise from the file name.
/// </summary>
public sealed class AssetTaggerTests
{
    /// <summary>Every full-match subfolder spelling from the archive is recognised.</summary>
    /// <param name="folder">The subfolder name.</param>
    [Theory]
    [InlineData("Full matches")]
    [InlineData("FULL MATCHES ")]
    [InlineData("Full Match")]
    [InlineData("FULLMATCH")]
    [InlineData("FULL Matchs")]
    [InlineData("Ali Hoshmand full games")]
    public void Full_match_folders_tag_full_match(string folder)
    {
        AssetTagger.Classify([folder], "clip.mp4").Tags.Should().Be(AssetTags.FullMatch);
    }

    /// <summary>Every highlight subfolder spelling from the archive is recognised.</summary>
    /// <param name="folder">The subfolder name.</param>
    [Theory]
    [InlineData("Highlights")]
    [InlineData("Highlight")]
    [InlineData("HIGHLIGHTS ")]
    [InlineData("Highlights NT")]
    [InlineData("Highlights 2025／2026")]
    [InlineData("Ivan Valeev highlights")]
    public void Highlight_folders_tag_highlight(string folder)
    {
        AssetTagger.Classify([folder], "clip.mp4").Tags.Should().Be(AssetTags.Highlight);
    }

    /// <summary>The STATS folder tags stats.</summary>
    [Fact]
    public void Stats_folder_tags_stats()
    {
        AssetTagger.Classify(["STATS"], "season.png").Tags.Should().Be(AssetTags.Stats);
    }

    /// <summary>The subfolder wins over keywords in the file name.</summary>
    [Fact]
    public void Subfolder_wins_over_file_name()
    {
        AssetTagger.Classify(["Highlights"], "full match vs zenit.mp4").Tags.Should().Be(AssetTags.Highlight);
    }

    /// <summary>A match folder nested inside a full-match folder keeps the full-match tag and names the video.</summary>
    [Fact]
    public void Nested_match_folder_keeps_tag_and_gives_the_title()
    {
        var result = AssetTagger.Classify(["Full matches", "LEMESOS VS AEL"], "GX010245.MP4");

        result.Tags.Should().Be(AssetTags.FullMatch);
        result.Title.Should().Be("LEMESOS VS AEL");
    }

    /// <summary>A competition folder inside a full-match folder is recognised the same way.</summary>
    [Fact]
    public void Competition_folder_inside_full_matches_keeps_the_tag()
    {
        AssetTagger.Classify(["Full matches", "Russian Superleague 25-26"], "round 3.mp4").Tags.Should().Be(AssetTags.FullMatch);
    }

    /// <summary>Without a recognised subfolder, file-name keywords decide; several can apply at once.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="expected">The expected tags.</param>
    [Theory]
    [InlineData("best HL 2025.mp4", AssetTags.Highlight)]
    [InlineData("Highlights_vs_Zenit.mp4", AssetTags.Highlight)]
    [InlineData("spikes.mp4", AssetTags.Spike)]
    [InlineData("attack-zone4.mov", AssetTags.Spike)]
    [InlineData("Serve receive.mp4", AssetTags.Serve | AssetTags.Reception)]
    [InlineData("blocks.mp4", AssetTags.Block)]
    [InlineData("reception.mp4", AssetTags.Reception)]
    [InlineData("passing drills.mp4", AssetTags.Reception)]
    [InlineData("defence.mp4", AssetTags.Defence)]
    [InlineData("digs.mp4", AssetTags.Defence)]
    [InlineData("full game.mp4", AssetTags.FullMatch)]
    [InlineData("match 2.mp4", AssetTags.FullMatch)]
    [InlineData("spike and block.mp4", AssetTags.Spike | AssetTags.Block)]
    public void File_name_keywords_are_the_fallback(string fileName, AssetTags expected)
    {
        AssetTagger.Classify([], fileName).Tags.Should().Be(expected);
    }

    /// <summary>Keywords only match whole words or word starts, not letters inside other words.</summary>
    /// <param name="fileName">The file name.</param>
    [Theory]
    [InlineData("InShot_20250101.mp4")]
    [InlineData("IMG_4412.MOV")]
    [InlineData("undefeated.mp4")]
    [InlineData("shlomo.mp4")]
    public void Unrelated_names_are_untagged(string fileName)
    {
        AssetTagger.Classify([], fileName).Tags.Should().Be(AssetTags.Untagged);
    }

    /// <summary>An unknown subfolder falls through to the file name.</summary>
    [Fact]
    public void Unknown_folder_falls_back_to_the_file_name()
    {
        AssetTagger.Classify(["Misc"], "blocks.mp4").Tags.Should().Be(AssetTags.Block);
    }

    /// <summary>Without a match folder, the title is the file name without extension, with separators turned into spaces.</summary>
    [Fact]
    public void Title_comes_from_the_file_name()
    {
        AssetTagger.Classify(["Highlights"], "best_of__2025-season.mp4").Title.Should().Be("best of 2025-season");
    }
}
