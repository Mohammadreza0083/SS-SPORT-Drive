using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Assets;

/// <summary>File facts (name, md5, size) and the kind detected from the extension.</summary>
public sealed class AssetFileTests
{
    /// <summary>A valid md5.</summary>
    private const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>Kinds come from the extension, case-insensitively; stat screenshots are documents.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="expected">The kind.</param>
    [Theory]
    [InlineData("match.mp4", AssetKind.Video)]
    [InlineData("MATCH.MP4", AssetKind.Video)]
    [InlineData("clip.MOV", AssetKind.Video)]
    [InlineData("clip.m4v", AssetKind.Video)]
    [InlineData("report.pdf", AssetKind.Document)]
    [InlineData("stats.PNG", AssetKind.Document)]
    [InlineData("stats.jpg", AssetKind.Document)]
    [InlineData("stats.jpeg", AssetKind.Document)]
    [InlineData("&match.dvw", AssetKind.Scout)]
    public void Kind_comes_from_the_extension(string name, AssetKind expected)
    {
        AssetFile.Create(name, Md5, 10).Kind.Should().Be(expected);
    }

    /// <summary>Unsupported files and folder markers without an extension are rejected.</summary>
    /// <param name="name">The file name.</param>
    [Theory]
    [InlineData("notes.docx")]
    [InlineData("archive.zip")]
    [InlineData("Full matches")]
    public void Unsupported_files_are_rejected(string name)
    {
        var create = () => AssetFile.Create(name, Md5, 10);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("asset.unsupported_file_type");
    }

    /// <summary>The md5 is stored lower-case.</summary>
    [Fact]
    public void Md5_is_normalised()
    {
        AssetFile.Create("a.mp4", Md5.ToUpperInvariant(), 10).Md5.Should().Be(Md5);
    }

    /// <summary>Malformed md5 values are rejected.</summary>
    /// <param name="md5">The checksum.</param>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz107d9d372bb6826bd81d3542a419d6")]
    public void Malformed_md5_is_rejected(string md5)
    {
        var create = () => AssetFile.Create("a.mp4", md5, 10);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("asset.md5_invalid");
    }

    /// <summary>Empty files (zero-byte folder markers) and negative sizes are rejected.</summary>
    /// <param name="size">The size in bytes.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Empty_files_are_rejected(long size)
    {
        var create = () => AssetFile.Create("a.mp4", Md5, size);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("asset.file_empty");
    }

    /// <summary>The name is trimmed and must not be blank.</summary>
    [Fact]
    public void Blank_names_are_rejected()
    {
        var create = () => AssetFile.Create("  ", Md5, 10);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("asset.unsupported_file_type");
    }
}
