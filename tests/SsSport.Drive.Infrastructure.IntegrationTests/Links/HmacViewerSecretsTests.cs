using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Links;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Links;

/// <summary>
/// The gate's secrets (L-2): random device and session tokens stored only as keyed hashes, and 6-digit codes stored as
/// a salted keyed hash compared in constant time (decisions §1b).
/// </summary>
public sealed class HmacViewerSecretsTests
{
    /// <summary>A valid key: base64 of 32 bytes.</summary>
    private const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>Another valid key.</summary>
    private const string OtherKey = "HyAhIiMkJSYnKCkqKywtLi8wMTIzNDU2Nzg5Ojs8PT4=";

    /// <summary>Tokens are 32 random bytes in base64url, never the same twice.</summary>
    [Fact]
    public void Tokens_are_long_and_random()
    {
        var secrets = Secrets(Key);

        var tokens = Enumerable.Range(0, 50).Select(_ => secrets.NewToken()).ToList();

        tokens.Should().OnlyHaveUniqueItems();
        tokens.Should().AllSatisfy(token => token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$"));
    }

    /// <summary>A token's hash is stable for one key, differs for another, and does not contain the token.</summary>
    [Fact]
    public void Token_hashes_are_keyed()
    {
        var token = Secrets(Key).NewToken();

        var hash = Secrets(Key).HashToken(token);

        hash.Should().Be(Secrets(Key).HashToken(token));
        hash.Should().NotBe(Secrets(OtherKey).HashToken(token));
        hash.Should().NotContain(token);
        hash.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
    }

    /// <summary>Codes are six digits.</summary>
    [Fact]
    public void Codes_are_six_digits()
    {
        var secrets = Secrets(Key);

        Enumerable.Range(0, 100).Select(_ => secrets.NewCode()).Should().AllSatisfy(code => code.Should().MatchRegex("^[0-9]{6}$"));
    }

    /// <summary>A code's hash is salted (two hashes of one code differ) and only the right code, with the right key, matches.</summary>
    [Fact]
    public void Code_hashes_are_salted_and_checked()
    {
        var secrets = Secrets(Key);

        var first = secrets.HashCode("042917");
        var second = secrets.HashCode("042917");

        first.Should().NotBe(second);
        first.Should().NotContain("042917");
        secrets.CodeMatches("042917", first).Should().BeTrue();
        secrets.CodeMatches("042917", second).Should().BeTrue();
        secrets.CodeMatches("042918", first).Should().BeFalse();
        Secrets(OtherKey).CodeMatches("042917", first).Should().BeFalse();
    }

    /// <summary>Missing or malformed stored hashes never match.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dot")]
    [InlineData("!!.!!")]
    public void Broken_hashes_never_match(string? stored) =>
        Secrets(Key).CodeMatches("042917", stored).Should().BeFalse();

    /// <summary>Without a valid key nothing is hashed.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("c2hvcnQ=")]
    public void A_missing_key_refuses_to_hash(string key)
    {
        var act = () => Secrets(key).HashToken("token");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Viewer__TokenKey*");
    }

    /// <summary>The secrets with a key.</summary>
    /// <param name="key">The base64 key.</param>
    /// <returns>The secrets.</returns>
    private static HmacViewerSecrets Secrets(string key) => new(Options.Create(new ViewerOptions { TokenKey = key }));
}
