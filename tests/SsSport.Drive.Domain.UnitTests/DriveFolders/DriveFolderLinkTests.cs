using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.DriveFolders;

/// <summary>A pasted Google Drive folder link yields the folder id; file links and other sites are refused (T-4a).</summary>
public sealed class DriveFolderLinkTests
{
    /// <summary>The usual shapes of a folder link, and a bare id.</summary>
    /// <param name="input">The pasted text.</param>
    [Theory]
    [InlineData("https://drive.google.com/drive/folders/1AbC-dEf_GhIjKlMnOp")]
    [InlineData("https://drive.google.com/drive/u/0/folders/1AbC-dEf_GhIjKlMnOp?usp=sharing")]
    [InlineData("drive.google.com/drive/folders/1AbC-dEf_GhIjKlMnOp/")]
    [InlineData("https://drive.google.com/open?id=1AbC-dEf_GhIjKlMnOp")]
    [InlineData("  1AbC-dEf_GhIjKlMnOp  ")]
    public void Parses_folder_links(string input)
    {
        DriveFolderLink.Create(input).FolderId.Should().Be("1AbC-dEf_GhIjKlMnOp");
    }

    /// <summary>File links, other sites, short ids and empty text are refused with one code.</summary>
    /// <param name="input">The pasted text.</param>
    [Theory]
    [InlineData("https://drive.google.com/file/d/1AbC-dEf_GhIjKlMnOp/view")]
    [InlineData("https://example.com/drive/folders/1AbC-dEf_GhIjKlMnOp")]
    [InlineData("https://drive.google.com/drive/folders/")]
    [InlineData("short")]
    [InlineData("has spaces in it")]
    [InlineData("")]
    [InlineData(null)]
    public void Refuses_other_links(string? input)
    {
        var create = () => DriveFolderLink.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("drive_folder.link_invalid");
    }
}
