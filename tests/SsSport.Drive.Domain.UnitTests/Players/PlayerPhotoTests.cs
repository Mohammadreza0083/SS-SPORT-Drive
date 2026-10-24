using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>A player photo is a reference to an object in storage (R2), not the image itself.</summary>
public sealed class PlayerPhotoTests
{
    /// <summary>Relative object keys are accepted and trimmed.</summary>
    /// <param name="input">The raw key.</param>
    /// <param name="expected">The stored key.</param>
    [Theory]
    [InlineData("players/0192f1c2/photo.webp", "players/0192f1c2/photo.webp")]
    [InlineData("  photos/arash-tehrani.png ", "photos/arash-tehrani.png")]
    public void Accepts_object_keys(string input, string expected)
    {
        PlayerPhoto.Create(input).StorageKey.Should().Be(expected);
    }

    /// <summary>Empty, absolute, traversing, whitespace-containing or overlong keys are rejected.</summary>
    /// <param name="input">The raw key.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/players/photo.webp")]
    [InlineData("players/../secrets.json")]
    [InlineData("players/my photo.webp")]
    [InlineData("players\\photo.webp")]
    [InlineData("https://cdn.example.com/photo.webp")]
    public void Rejects_invalid_keys(string input)
    {
        var create = () => PlayerPhoto.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.photo_key_invalid");
    }

    /// <summary>Keys longer than 512 characters are rejected.</summary>
    [Fact]
    public void Rejects_overlong_keys()
    {
        var create = () => PlayerPhoto.Create("players/" + new string('a', 512));

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.photo_key_invalid");
    }
}
