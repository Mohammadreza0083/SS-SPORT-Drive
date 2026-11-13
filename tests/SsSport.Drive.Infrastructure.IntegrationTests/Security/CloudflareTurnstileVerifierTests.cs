using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Security;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Security;

/// <summary>
/// Checking a Cloudflare Turnstile token on the server (P-8, decisions §6 "Anti-spam"): the token, the secret key and
/// the visitor's address are posted to Cloudflare's <c>siteverify</c>; only <c>"success": true</c> passes. A missing
/// token, a Cloudflare error or an unreachable Cloudflare all fail closed. The secret is never logged.
/// </summary>
public sealed class CloudflareTurnstileVerifierTests
{
    /// <summary>The secret key used in the tests.</summary>
    private const string Secret = "0x4AAAAAAAtest-secret";

    /// <summary>A passing token is posted as a form with the secret and the address, and passes.</summary>
    [Fact]
    public async Task A_passing_token_passes()
    {
        var http = new RecordingHandler(HttpStatusCode.OK, """{ "success": true, "error-codes": [] }""");

        var passed = await Verifier(http).VerifyAsync("token-1", "203.0.113.9", TestContext.Current.CancellationToken);

        passed.Should().BeTrue();
        var call = http.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("https://challenges.cloudflare.com/turnstile/v0/siteverify");
        call.ContentType.Should().Be("application/x-www-form-urlencoded");
        call.Body.Should().Contain("secret=0x4AAAAAAAtest-secret").And.Contain("response=token-1").And.Contain("remoteip=203.0.113.9");
    }

    /// <summary>A token Cloudflare rejects fails.</summary>
    [Fact]
    public async Task A_rejected_token_fails()
    {
        var http = new RecordingHandler(HttpStatusCode.OK, """{ "success": false, "error-codes": ["invalid-input-response"] }""");

        (await Verifier(http).VerifyAsync("token-1", null, TestContext.Current.CancellationToken)).Should().BeFalse();
        http.Calls.Single().Body.Should().NotContain("remoteip");
    }

    /// <summary>No token fails at once, without calling Cloudflare.</summary>
    /// <param name="token">The token.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task No_token_fails_without_a_call(string? token)
    {
        var http = new RecordingHandler(HttpStatusCode.OK, """{ "success": true }""");

        (await Verifier(http).VerifyAsync(token, "203.0.113.9", TestContext.Current.CancellationToken)).Should().BeFalse();
        http.Calls.Should().BeEmpty();
    }

    /// <summary>A token longer than Cloudflare's 2048-character limit fails without a call.</summary>
    [Fact]
    public async Task An_oversized_token_fails_without_a_call()
    {
        var http = new RecordingHandler(HttpStatusCode.OK, """{ "success": true }""");

        (await Verifier(http).VerifyAsync(new string('a', 2049), null, TestContext.Current.CancellationToken)).Should().BeFalse();
        http.Calls.Should().BeEmpty();
    }

    /// <summary>A Cloudflare error, a body that is not JSON, or no answer at all fails closed.</summary>
    [Fact]
    public async Task Cloudflare_trouble_fails_closed()
    {
        (await Verifier(new RecordingHandler(HttpStatusCode.InternalServerError, "oops")).VerifyAsync("t", null, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await Verifier(new RecordingHandler(HttpStatusCode.OK, "<html>")).VerifyAsync("t", null, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await Verifier(new RecordingHandler(throws: true)).VerifyAsync("t", null, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    /// <summary>Without a secret key every check is refused and Cloudflare is never called (fail closed).</summary>
    [Fact]
    public async Task No_secret_fails_closed_without_a_call()
    {
        var http = new RecordingHandler(HttpStatusCode.OK, """{ "success": true }""");
        var verifier = new CloudflareTurnstileVerifier(new HttpClient(http), Options.Create(new TurnstileOptions()), NullLogger<CloudflareTurnstileVerifier>.Instance);

        (await verifier.VerifyAsync("token-1", "203.0.113.9", TestContext.Current.CancellationToken)).Should().BeFalse();
        http.Calls.Should().BeEmpty();
    }

    /// <summary>Builds the verifier over a stub handler.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>The verifier.</returns>
    private static CloudflareTurnstileVerifier Verifier(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new TurnstileOptions { SecretKey = Secret }), NullLogger<CloudflareTurnstileVerifier>.Instance);

    /// <summary>Answers every request with a fixed reply (or an exception) and records what was sent.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>The status to answer.</summary>
        private readonly HttpStatusCode _status;

        /// <summary>The body to answer.</summary>
        private readonly string _body;

        /// <summary>Whether to fail as if Cloudflare were unreachable.</summary>
        private readonly bool _throws;

        /// <summary>Initializes a new instance of the <see cref="RecordingHandler"/> class.</summary>
        /// <param name="status">The status to answer.</param>
        /// <param name="body">The body to answer.</param>
        /// <param name="throws">Whether to throw instead.</param>
        public RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "{}", bool throws = false)
        {
            _status = status;
            _body = body;
            _throws = throws;
        }

        /// <summary>Gets the requests, in order.</summary>
        public List<(string Url, string? ContentType, string Body)> Calls { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.RequestUri!.ToString(), request.Content?.Headers.ContentType?.MediaType, body));
            if (_throws)
            {
                throw new HttpRequestException("unreachable");
            }

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
