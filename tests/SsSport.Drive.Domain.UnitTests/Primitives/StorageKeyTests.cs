using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Primitives;

/// <summary>A storage key is a relative object key in R2 / RustFS.</summary>
public sealed class StorageKeyTests
{
    /// <summary>Relative keys are accepted and trimmed.</summary>
    [Fact]
    public void Accepts_relative_keys()
    {
        StorageKey.Create(" hls/0192/720/index.m3u8 ").Value.Should().Be("hls/0192/720/index.m3u8");
    }

    /// <summary>Keys from the Drive backup may contain spaces and Unicode, e.g. player folder names.</summary>
    [Fact]
    public void Accepts_backup_paths_with_spaces()
    {
        StorageKey.Create("Men Volleyball/SS-SPORT Ali Hoshmand／OH/Full matches/game 1.mp4").Value
            .Should().Contain("SS-SPORT Ali Hoshmand");
    }

    /// <summary>Empty, absolute, traversing, URL and backslash keys are rejected.</summary>
    /// <param name="input">The raw key.</param>
    [Theory]
    [InlineData("")]
    [InlineData("/hls/a.m3u8")]
    [InlineData("hls/../secrets")]
    [InlineData("https://cdn.example.com/a.m3u8")]
    [InlineData("hls\\a.m3u8")]
    public void Rejects_invalid_keys(string input)
    {
        var create = () => StorageKey.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("storage.key_invalid");
    }
}
