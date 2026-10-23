using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>The Volleybox player ID is parsed from the player's URL (never scraped); the URL is normalised.</summary>
public sealed class VolleyboxProfileTests
{
    /// <summary>Player URLs in their usual shapes yield the id and a clean https URL.</summary>
    /// <param name="input">The pasted URL.</param>
    /// <param name="expectedId">The player id.</param>
    /// <param name="expectedUrl">The normalised URL.</param>
    [Theory]
    [InlineData("https://volleybox.net/arash-tehrani-p48213", 48213, "https://volleybox.net/arash-tehrani-p48213")]
    [InlineData("http://www.volleybox.net/arash-tehrani-p48213/clubs", 48213, "https://volleybox.net/arash-tehrani-p48213")]
    [InlineData("volleybox.net/ivan-valeev-p1234?tab=stats", 1234, "https://volleybox.net/ivan-valeev-p1234")]
    [InlineData("https://women.volleybox.net/lara-jovovic-p77/", 77, "https://women.volleybox.net/lara-jovovic-p77")]
    [InlineData("  HTTPS://VolleyBox.net/Paulina-Perez-P905  ", 905, "https://volleybox.net/paulina-perez-p905")]
    public void Parses_player_urls(string input, int expectedId, string expectedUrl)
    {
        var profile = VolleyboxProfile.Create(input);

        profile.PlayerId.Should().Be(expectedId);
        profile.Url.Should().Be(expectedUrl);
    }

    /// <summary>Team and club pages are recognised and rejected with a specific code.</summary>
    /// <param name="input">The pasted URL.</param>
    [Theory]
    [InlineData("https://volleybox.net/sepahan-t1234")]
    [InlineData("https://volleybox.net/sepahan-t1234/players")]
    public void Rejects_team_pages(string input)
    {
        var create = () => VolleyboxProfile.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.volleybox_not_a_player");
    }

    /// <summary>Other sites, empty input and URLs without a player id are rejected.</summary>
    /// <param name="input">The pasted URL.</param>
    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/arash-tehrani-p48213")]
    [InlineData("https://volleybox.net/")]
    [InlineData("https://volleybox.net/arash-tehrani")]
    [InlineData("https://volleybox.net.evil.com/arash-tehrani-p48213")]
    [InlineData("not a url")]
    public void Rejects_other_urls(string input)
    {
        var create = () => VolleyboxProfile.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("player.volleybox_url_invalid");
    }
}
