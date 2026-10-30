using System.Text.Json;
using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Edge;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Edge;

/// <summary>
/// HLS edge tokens (V-5a): the API signs a 30-minute token for one video's HLS folder, bound to the viewer's session
/// cookie, in exactly the format the Cloudflare Worker verifies. The shared vectors in
/// <c>contracts/hls-token-vectors.json</c> were produced by an independent implementation (Python's <c>hmac</c>).
/// </summary>
public sealed class HmacHlsTokenIssuerTests
{
    /// <summary>The shared vectors, read once.</summary>
    private static readonly JsonElement _vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hls-token-vectors.json"))).RootElement;

    /// <summary>The vectors' issue case.</summary>
    private static JsonElement Issue => _vectors.GetProperty("issue");

    /// <summary>The token issued for the vectors' video, session and expiry is exactly the vectors' token.</summary>
    [Fact]
    public void Produces_the_shared_vector_token()
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(Issue.GetProperty("expiresAtUnix").GetInt64());
        var issuer = Issuer(expiresAt - TimeSpan.FromMinutes(30));

        var access = issuer.Issue(Issue.GetProperty("pathPrefix").GetString()!, Issue.GetProperty("sessionCookie").GetString()!);

        access.Token.Should().Be(Issue.GetProperty("token").GetString());
        access.ExpiresAt.Should().Be(expiresAt);
    }

    /// <summary>The token lives 30 minutes and points at the video's master playlist through the edge.</summary>
    [Fact]
    public void Expires_after_30_minutes_and_links_the_master_playlist()
    {
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        var access = Issuer(now).Issue("players/a/videos/b/hls", "vs_session");

        access.ExpiresAt.Should().Be(now.AddMinutes(30));
        access.PlaylistUrl.Should().Be($"/hls/{access.Token}/players/a/videos/b/hls/master.m3u8");
    }

    /// <summary>Different viewers get different tokens for the same video.</summary>
    [Fact]
    public void Binds_the_token_to_the_session()
    {
        var issuer = Issuer(DateTimeOffset.UnixEpoch.AddYears(56));

        issuer.Issue("players/a/videos/b/hls", "vs_one").Token
            .Should().NotBe(issuer.Issue("players/a/videos/b/hls", "vs_two").Token);
    }

    /// <summary>The session cookie itself never appears in the token, only a short hash of it.</summary>
    [Fact]
    public void Never_puts_the_session_cookie_in_the_token()
    {
        var access = Issuer(DateTimeOffset.UnixEpoch.AddYears(56)).Issue("players/a/videos/b/hls", "vs_secret_cookie_value");

        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(access.Token.Split('.')[0]));
        payload.Should().NotContain("vs_secret_cookie_value");
    }

    /// <summary>Folder paths that could escape the video's folder, or are not plain object keys, are refused.</summary>
    /// <param name="prefix">The refused path prefix.</param>
    [Theory]
    [InlineData("")]
    [InlineData("/players/a/videos/b/hls")]
    [InlineData("players/a/videos/b/hls/")]
    [InlineData("players/../other/hls")]
    [InlineData("players/a/videos/b/hls?x=1")]
    [InlineData("players/a b/hls")]
    public void Refuses_unsafe_path_prefixes(string prefix)
    {
        var act = () => Issuer(DateTimeOffset.UnixEpoch.AddYears(56)).Issue(prefix, "vs_session");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A token is never issued without a viewer session.</summary>
    [Fact]
    public void Refuses_an_empty_session()
    {
        var act = () => Issuer(DateTimeOffset.UnixEpoch.AddYears(56)).Issue("players/a/videos/b/hls", "");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>The signing key must be base64 of at least 32 bytes.</summary>
    /// <param name="key">The configured key.</param>
    /// <param name="valid">Whether it is accepted.</param>
    [Theory]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", true)]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==", false)]
    [InlineData("not base64!", false)]
    [InlineData("", false)]
    public void Checks_the_signing_key(string key, bool valid)
    {
        EdgeOptions.IsValidSigningKey(key).Should().Be(valid);
    }

    /// <summary>
    /// Builds the issuer with the vectors' key and a clock fixed at <paramref name="now"/>.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The issuer.</returns>
    private static HmacHlsTokenIssuer Issuer(DateTimeOffset now) =>
        new(Options.Create(new EdgeOptions { HlsSigningKey = _vectors.GetProperty("signingKeyBase64").GetString()! }), new FixedTimeProvider(now));

    /// <summary>
    /// Decodes base64url without padding.
    /// </summary>
    /// <param name="value">The encoded text.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    /// <summary>A clock that always reads the same time.</summary>
    /// <param name="now">The time it reads.</param>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;
    }
}
