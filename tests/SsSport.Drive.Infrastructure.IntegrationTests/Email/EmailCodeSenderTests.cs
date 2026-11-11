using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Email;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Email;

/// <summary>
/// Sending gate codes (L-2, decisions §8): Resend first, Brevo when Resend fails; in development without keys the mail
/// is written to a pickup directory instead.
/// </summary>
public sealed class EmailCodeSenderTests : IDisposable
{
    /// <summary>The address the code goes to.</summary>
    private static readonly EmailAddress _to = EmailAddress.Create("marco@lube.it");

    /// <summary>A throwaway pickup directory.</summary>
    private readonly string _pickup = Path.Combine(Path.GetTempPath(), $"ssd-pickup-{Guid.NewGuid():N}");

    /// <summary>Removes the pickup directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_pickup))
        {
            Directory.Delete(_pickup, recursive: true);
        }
    }

    /// <summary>Resend is tried first, with the API key as a bearer token, the sender, the address and the code.</summary>
    [Fact]
    public async Task Resend_sends_the_code()
    {
        var http = new RecordingHandler(HttpStatusCode.OK);

        await Sender(BothProviders(), http).SendAsync(_to, "042917", TestContext.Current.CancellationToken);

        var call = http.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("https://api.resend.com/emails");
        call.Authorization.Should().Be("Bearer re_test");
        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("from").GetString().Should().Be("SS-SPORT <info@ss-sport.co>");
        body.RootElement.GetProperty("to").EnumerateArray().Select(item => item.GetString()).Should().Equal("marco@lube.it");
        body.RootElement.GetProperty("subject").GetString().Should().NotContain("042917");
        body.RootElement.GetProperty("text").GetString().Should().Contain("042917").And.Contain("10 minutes");
        body.RootElement.GetProperty("html").GetString().Should().Contain("042917");
    }

    /// <summary>When Resend fails, Brevo sends the same code with its own API key header.</summary>
    [Fact]
    public async Task Brevo_takes_over_when_resend_fails()
    {
        var http = new RecordingHandler(HttpStatusCode.InternalServerError, HttpStatusCode.Created);

        await Sender(BothProviders(), http).SendAsync(_to, "042917", TestContext.Current.CancellationToken);

        http.Calls.Should().HaveCount(2);
        var brevo = http.Calls[1];
        brevo.Url.Should().Be("https://api.brevo.com/v3/smtp/email");
        brevo.ApiKey.Should().Be("xkeysib-test");
        using var body = JsonDocument.Parse(brevo.Body);
        body.RootElement.GetProperty("sender").GetProperty("email").GetString().Should().Be("info@ss-sport.co");
        body.RootElement.GetProperty("to")[0].GetProperty("email").GetString().Should().Be("marco@lube.it");
        body.RootElement.GetProperty("textContent").GetString().Should().Contain("042917");
    }

    /// <summary>When every provider fails the job fails, so Hangfire retries it.</summary>
    [Fact]
    public async Task Fails_when_every_provider_fails()
    {
        var http = new RecordingHandler(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway);

        var act = () => Sender(BothProviders(), http).SendAsync(_to, "042917", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        http.Calls.Should().HaveCount(2);
    }

    /// <summary>A provider without a key is skipped.</summary>
    [Fact]
    public async Task Skips_a_provider_without_a_key()
    {
        var http = new RecordingHandler(HttpStatusCode.Created);
        var options = BothProviders() with { Resend = new EmailProviderOptions() };

        await Sender(options, http).SendAsync(_to, "042917", TestContext.Current.CancellationToken);

        http.Calls.Should().ContainSingle().Which.Url.Should().Contain("brevo");
    }

    /// <summary>With a pickup directory the mail is written as an .eml file and nothing is sent.</summary>
    [Fact]
    public async Task The_pickup_directory_gets_an_eml_file()
    {
        var http = new RecordingHandler();

        await Sender(new EmailOptions { PickupDirectory = _pickup }, http).SendAsync(_to, "042917", TestContext.Current.CancellationToken);

        http.Calls.Should().BeEmpty();
        var file = Directory.GetFiles(_pickup, "*.eml").Should().ContainSingle().Subject;
        var mail = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        mail.Should().Contain("To: marco@lube.it").And.Contain("042917");
    }

    /// <summary>With no provider and no pickup directory there is no way to send, and the job fails.</summary>
    [Fact]
    public async Task Fails_without_any_way_to_send()
    {
        var act = () => Sender(new EmailOptions(), new RecordingHandler()).SendAsync(_to, "042917", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Email__*");
    }

    /// <summary>Both providers configured.</summary>
    /// <returns>The options.</returns>
    private static EmailOptions BothProviders() => new()
    {
        Resend = new EmailProviderOptions { ApiKey = "re_test", SenderName = "SS-SPORT", SenderEmail = "info@ss-sport.co" },
        Brevo = new EmailProviderOptions { ApiKey = "xkeysib-test", SenderName = "SS-SPORT", SenderEmail = "info@ss-sport.co" },
    };

    /// <summary>A sender over a recording handler.</summary>
    /// <param name="options">The email settings.</param>
    /// <param name="http">The handler.</param>
    /// <returns>The sender.</returns>
    private static EmailCodeSender Sender(EmailOptions options, RecordingHandler http) =>
        new(Options.Create(options), new HttpClient(http), NullLogger<EmailCodeSender>.Instance);

    /// <summary>One request the handler saw.</summary>
    /// <param name="Url">The URL.</param>
    /// <param name="Authorization">The Authorization header, or <see langword="null"/>.</param>
    /// <param name="ApiKey">The <c>api-key</c> header, or <see langword="null"/>.</param>
    /// <param name="Body">The body.</param>
    private sealed record Call(string Url, string? Authorization, string? ApiKey, string Body);

    /// <summary>Answers each request with the next status and records it.</summary>
    /// <param name="statuses">The statuses, in order.</param>
    private sealed class RecordingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        /// <summary>Gets the requests seen.</summary>
        public List<Call> Calls { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new Call(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null,
                body));
            return new HttpResponseMessage(statuses[Calls.Count - 1]) { Content = new StringContent("{}") };
        }
    }
}
