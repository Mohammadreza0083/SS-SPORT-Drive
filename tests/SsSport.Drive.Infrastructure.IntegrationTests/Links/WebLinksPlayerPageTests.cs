using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Web;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Links;

/// <summary>T-6b: the result card links to the player's public page, <c>/players/{slug}</c> on the web app.</summary>
public sealed class WebLinksPlayerPageTests
{
    /// <summary>The page sits under the public address; a trailing slash on the setting is ignored.</summary>
    [Fact]
    public void Builds_the_public_player_page()
    {
        var links = new WebLinks(Options.Create(new WebOptions { PublicBaseUrl = "https://drive.ss-sport.co/" }));

        links.PlayerPage("arash-tehrani").Should().Be(new Uri("https://drive.ss-sport.co/players/arash-tehrani"));
    }

    /// <summary>The slug is escaped, and a blank slug is refused.</summary>
    [Fact]
    public void Escapes_and_checks_the_slug()
    {
        var links = new WebLinks(Options.Create(new WebOptions { PublicBaseUrl = "https://drive.ss-sport.co" }));

        links.PlayerPage("a b").AbsoluteUri.Should().Be("https://drive.ss-sport.co/players/a%20b");
        links.Invoking(l => l.PlayerPage(" ")).Should().Throw<ArgumentException>();
    }

    /// <summary>Without a public address the page cannot be built, as for every other link.</summary>
    [Fact]
    public void Needs_the_public_address()
    {
        var links = new WebLinks(Options.Create(new WebOptions()));

        links.Invoking(l => l.PlayerPage("arash-tehrani")).Should().Throw<InvalidOperationException>();
    }
}
