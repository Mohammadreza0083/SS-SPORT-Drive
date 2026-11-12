using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Email;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Email;

/// <summary>
/// The email transports (L-4): Resend and Brevo over their HTTP APIs, Google SMTP, and the development pickup folder.
/// Each sends one message or throws; choosing and failing over is the dispatcher's job.
/// </summary>
public sealed class EmailTransportTests : IDisposable
{
    /// <summary>The address.</summary>
    private static readonly EmailAddress _to = EmailAddress.Create("marco@lube.it");

    /// <summary>The message every test sends.</summary>
    private static readonly EmailMessage _message = new(_to, "Your SS-SPORT Drive code", "Your code is 042917.", "<p>Your code is 042917.</p>");

    /// <summary>A throwaway pickup folder.</summary>
    private readonly string _pickup = Path.Combine(Path.GetTempPath(), $"ssd-pickup-{Guid.NewGuid():N}");

    /// <summary>Deletes the pickup folder.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_pickup))
        {
            Directory.Delete(_pickup, recursive: true);
        }
    }

    /// <summary>Resend gets the message with its bearer key and the SS-SPORT sender.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Resend_sends_the_message()
    {
        var http = new RecordingHandler(HttpStatusCode.OK);
        var transport = new ResendTransport(Options.Create(Providers()), new HttpClient(http));

        transport.Name.Should().Be("Resend");
        transport.IsConfigured.Should().BeTrue();
        await transport.SendAsync(_message, TestContext.Current.CancellationToken);

        var call = http.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("https://api.resend.com/emails");
        call.Authorization.Should().Be("Bearer re_test");
        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("from").GetString().Should().Be("SS-SPORT <info@ss-sport.co>");
        body.RootElement.GetProperty("to").EnumerateArray().Select(item => item.GetString()).Should().Equal("marco@lube.it");
        body.RootElement.GetProperty("subject").GetString().Should().Be("Your SS-SPORT Drive code");
        body.RootElement.GetProperty("text").GetString().Should().Be("Your code is 042917.");
        body.RootElement.GetProperty("html").GetString().Should().Be("<p>Your code is 042917.</p>");
    }

    /// <summary>A refusal from Resend throws, so the dispatcher can try the next provider.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_refusal_throws()
    {
        var transport = new ResendTransport(Options.Create(Providers()), new HttpClient(new RecordingHandler(HttpStatusCode.UnprocessableEntity)));

        var act = () => transport.SendAsync(_message, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<EmailTransportException>()).Which.Message.Should().Contain("422").And.NotContain("marco");
    }

    /// <summary>Brevo gets the message with its api-key header.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Brevo_sends_the_message()
    {
        var http = new RecordingHandler(HttpStatusCode.Created);
        var transport = new BrevoTransport(Options.Create(Providers()), new HttpClient(http));

        transport.Name.Should().Be("Brevo");
        await transport.SendAsync(_message, TestContext.Current.CancellationToken);

        var call = http.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("https://api.brevo.com/v3/smtp/email");
        call.ApiKey.Should().Be("xkeysib-test");
        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("sender").GetProperty("email").GetString().Should().Be("info@ss-sport.co");
        body.RootElement.GetProperty("to")[0].GetProperty("email").GetString().Should().Be("marco@lube.it");
        body.RootElement.GetProperty("textContent").GetString().Should().Be("Your code is 042917.");
        body.RootElement.GetProperty("htmlContent").GetString().Should().Be("<p>Your code is 042917.</p>");
    }

    /// <summary>A provider without a key is not configured.</summary>
    [Fact]
    public void A_provider_without_a_key_is_off()
    {
        var options = Options.Create(new EmailOptions());

        new ResendTransport(options, new HttpClient()).IsConfigured.Should().BeFalse();
        new BrevoTransport(options, new HttpClient()).IsConfigured.Should().BeFalse();
        new GoogleSmtpTransport(options).IsConfigured.Should().BeFalse();
        new PickupDirectoryTransport(options).IsConfigured.Should().BeFalse();
    }

    /// <summary>Google SMTP is on once its app password is set, and sends from info@ss-sport.co by default.</summary>
    [Fact]
    public void Google_smtp_needs_its_app_password()
    {
        var options = new EmailOptions { GoogleSmtp = new SmtpOptions { AppPassword = "abcd efgh ijkl mnop" } };
        var transport = new GoogleSmtpTransport(Options.Create(options));

        transport.Name.Should().Be("GoogleSmtp");
        transport.IsConfigured.Should().BeTrue();
        options.GoogleSmtp.Host.Should().Be("smtp.gmail.com");
        options.GoogleSmtp.Port.Should().Be(587);
        options.GoogleSmtp.UserName.Should().Be("info@ss-sport.co");
        options.GoogleSmtp.SenderEmail.Should().Be("info@ss-sport.co");
    }

    /// <summary>The pickup folder gets an <c>.eml</c> file instead of a real mail.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_pickup_directory_gets_an_eml_file()
    {
        var transport = new PickupDirectoryTransport(Options.Create(new EmailOptions { PickupDirectory = _pickup }));

        transport.Name.Should().Be("PickupDirectory");
        transport.IsConfigured.Should().BeTrue();
        await transport.SendAsync(_message, TestContext.Current.CancellationToken);

        var file = Directory.GetFiles(_pickup, "*.eml").Should().ContainSingle().Subject;
        var mail = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        mail.Should().Contain("To: marco@lube.it").And.Contain("Subject: Your SS-SPORT Drive code").And.Contain("Your code is 042917.");
    }

    /// <summary>Resend and Brevo keys for the tests.</summary>
    /// <returns>The options.</returns>
    private static EmailOptions Providers() => new()
    {
        Resend = new EmailProviderOptions { ApiKey = "re_test", SenderName = "SS-SPORT", SenderEmail = "info@ss-sport.co" },
        Brevo = new EmailProviderOptions { ApiKey = "xkeysib-test", SenderName = "SS-SPORT", SenderEmail = "info@ss-sport.co" },
    };

    /// <summary>One recorded HTTP call.</summary>
    /// <param name="Url">The URL.</param>
    /// <param name="Authorization">The Authorization header.</param>
    /// <param name="ApiKey">The api-key header.</param>
    /// <param name="Body">The body.</param>
    private sealed record Call(string Url, string? Authorization, string? ApiKey, string Body);

    /// <summary>Records calls and answers with the given statuses in order.</summary>
    /// <param name="statuses">The answers.</param>
    private sealed class RecordingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        /// <summary>Gets the calls.</summary>
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
