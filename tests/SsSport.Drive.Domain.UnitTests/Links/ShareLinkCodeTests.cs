using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Links;

/// <summary>
/// The code in <c>/r/{code}</c> (L-1): six random, URL-safe characters, without look-alikes (0/O, 1/l/I), so it cannot be
/// guessed and reads clearly when someone types it.
/// </summary>
public sealed class ShareLinkCodeTests
{
    /// <summary>A generated code has six characters from the alphabet.</summary>
    [Fact]
    public void A_generated_code_has_six_characters_from_the_alphabet()
    {
        var code = ShareLinkCode.Generate();

        code.Value.Should().HaveLength(ShareLinkCode.Length);
        code.Value.ToCharArray().Should().OnlyContain(character => ShareLinkCode.Alphabet.Contains(character));
    }

    /// <summary>The alphabet is URL-safe and leaves out look-alike characters.</summary>
    [Fact]
    public void The_alphabet_is_url_safe_without_look_alikes()
    {
        ShareLinkCode.Alphabet.Should().MatchRegex("^[A-Za-z2-9]+$");
        ShareLinkCode.Alphabet.Should().NotContainAny("0", "O", "1", "l", "I");
        ShareLinkCode.Alphabet.Distinct().Should().HaveCount(ShareLinkCode.Alphabet.Length);
    }

    /// <summary>Generated codes are random: a thousand of them do not repeat.</summary>
    [Fact]
    public void Generated_codes_do_not_repeat()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => ShareLinkCode.Generate().Value).ToList();

        codes.Should().OnlyHaveUniqueItems();
    }

    /// <summary>A code read back is kept exactly; codes are case-sensitive.</summary>
    [Fact]
    public void A_valid_code_is_kept_exactly()
    {
        ShareLinkCode.Create("Ab3xYz").Value.Should().Be("Ab3xYz");
        ShareLinkCode.Create("Ab3xYz").Should().NotBe(ShareLinkCode.Create("ab3xyz"));
    }

    /// <summary>A code of the wrong length or with other characters is refused.</summary>
    /// <param name="raw">The code.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ab3xY")]
    [InlineData("Ab3xYzz")]
    [InlineData("Ab3x-z")]
    [InlineData("Ab3x0z")]
    public void An_invalid_code_is_refused(string? raw)
    {
        var create = () => ShareLinkCode.Create(raw);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("share_link.code_invalid");
    }
}
