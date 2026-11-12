using Microsoft.Extensions.Logging.Abstractions;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Email;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Email;

/// <summary>
/// Choosing the provider (decisions §8, L-4): codes and sign-in links go Resend first, then Brevo; every other email goes
/// through Google SMTP. In development the pickup folder takes everything. A provider that is off is skipped; when every
/// provider fails, the dispatcher throws so the queue retries.
/// </summary>
public sealed class EmailDispatcherTests
{
    /// <summary>The message.</summary>
    private static readonly EmailMessage _message = new(EmailAddress.Create("marco@lube.it"), "Subject", "Text", "<p>Html</p>");

    /// <summary>A code goes to Resend when it works.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_code_goes_to_resend()
    {
        var (resend, brevo, smtp) = (new FakeTransport("Resend"), new FakeTransport("Brevo"), new FakeTransport("GoogleSmtp"));

        var provider = await Dispatcher(smtp, brevo, resend).SendAsync(EmailKind.Code, _message, TestContext.Current.CancellationToken);

        provider.Should().Be("Resend");
        resend.Sent.Should().ContainSingle().Which.Should().Be(_message);
        brevo.Sent.Should().BeEmpty();
        smtp.Sent.Should().BeEmpty();
    }

    /// <summary>Brevo takes over when Resend throws.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Brevo_takes_over_when_resend_fails()
    {
        var (resend, brevo) = (new FakeTransport("Resend") { Fails = true }, new FakeTransport("Brevo"));

        var provider = await Dispatcher(resend, brevo).SendAsync(EmailKind.Code, _message, TestContext.Current.CancellationToken);

        provider.Should().Be("Brevo");
        resend.Tries.Should().Be(1);
        brevo.Sent.Should().ContainSingle();
    }

    /// <summary>A provider without a key is skipped.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_provider_that_is_off_is_skipped()
    {
        var (resend, brevo) = (new FakeTransport("Resend") { IsConfigured = false }, new FakeTransport("Brevo"));

        var provider = await Dispatcher(resend, brevo).SendAsync(EmailKind.Code, _message, TestContext.Current.CancellationToken);

        provider.Should().Be("Brevo");
        resend.Tries.Should().Be(0);
    }

    /// <summary>Every other email goes through Google SMTP, never the code providers.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_general_email_goes_through_google_smtp()
    {
        var (resend, brevo, smtp) = (new FakeTransport("Resend"), new FakeTransport("Brevo"), new FakeTransport("GoogleSmtp"));

        var provider = await Dispatcher(resend, brevo, smtp).SendAsync(EmailKind.General, _message, TestContext.Current.CancellationToken);

        provider.Should().Be("GoogleSmtp");
        smtp.Sent.Should().ContainSingle();
        resend.Tries.Should().Be(0);
        brevo.Tries.Should().Be(0);
    }

    /// <summary>A general email does not fall back to Resend or Brevo when SMTP fails; it throws for a retry.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_general_email_does_not_fall_back_to_code_providers()
    {
        var (resend, smtp) = (new FakeTransport("Resend"), new FakeTransport("GoogleSmtp") { Fails = true });

        var act = () => Dispatcher(resend, smtp).SendAsync(EmailKind.General, _message, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        resend.Tries.Should().Be(0);
    }

    /// <summary>When every provider fails, the dispatcher throws, so the queue retries.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Fails_when_every_provider_fails()
    {
        var (resend, brevo) = (new FakeTransport("Resend") { Fails = true }, new FakeTransport("Brevo") { Fails = true });

        var act = () => Dispatcher(resend, brevo).SendAsync(EmailKind.Code, _message, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        resend.Tries.Should().Be(1);
        brevo.Tries.Should().Be(1);
    }

    /// <summary>With no provider on, the dispatcher says which settings to fill in.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Fails_without_any_way_to_send()
    {
        var act = () => Dispatcher(new FakeTransport("Resend") { IsConfigured = false }).SendAsync(EmailKind.Code, _message, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Email__*");
    }

    /// <summary>The pickup folder, when set, takes every email of every kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(EmailKind.Code)]
    [InlineData(EmailKind.General)]
    public async Task The_pickup_folder_takes_everything(EmailKind kind)
    {
        var (resend, smtp, pickup) = (new FakeTransport("Resend"), new FakeTransport("GoogleSmtp"), new FakeTransport("PickupDirectory"));

        var provider = await Dispatcher(resend, smtp, pickup).SendAsync(kind, _message, TestContext.Current.CancellationToken);

        provider.Should().Be("PickupDirectory");
        pickup.Sent.Should().ContainSingle();
        resend.Tries.Should().Be(0);
        smtp.Tries.Should().Be(0);
    }

    /// <summary>Cancellation is not a provider failure: it stops at once.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Cancellation_is_not_a_failover()
    {
        var (resend, brevo) = (new FakeTransport("Resend") { Cancels = true }, new FakeTransport("Brevo"));

        var act = () => Dispatcher(resend, brevo).SendAsync(EmailKind.Code, _message, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        brevo.Tries.Should().Be(0);
    }

    /// <summary>Builds the dispatcher over the transports (in any registration order).</summary>
    /// <param name="transports">The transports.</param>
    /// <returns>The dispatcher.</returns>
    private static EmailDispatcher Dispatcher(params IEmailTransport[] transports) =>
        new(transports, NullLogger<EmailDispatcher>.Instance);

    /// <summary>A transport that records, fails or cancels on demand.</summary>
    /// <param name="name">The provider name.</param>
    private sealed class FakeTransport(string name) : IEmailTransport
    {
        /// <inheritdoc />
        public string Name { get; } = name;

        /// <inheritdoc />
        public bool IsConfigured { get; init; } = true;

        /// <summary>Gets a value indicating whether every send throws.</summary>
        public bool Fails { get; init; }

        /// <summary>Gets a value indicating whether every send is cancelled.</summary>
        public bool Cancels { get; init; }

        /// <summary>Gets how many sends were tried.</summary>
        public int Tries { get; private set; }

        /// <summary>Gets the messages sent.</summary>
        public List<EmailMessage> Sent { get; } = [];

        /// <inheritdoc />
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Tries++;
            if (Cancels)
            {
                throw new OperationCanceledException();
            }

            if (Fails)
            {
                throw new HttpRequestException("refused");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
