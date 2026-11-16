using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Email;

namespace SsSport.Drive.Api.IntegrationTests.Emails;

/// <summary>
/// Found 2026-11-16: locally the API reads the developer's <c>.env</c>, which holds real email credentials. A test host
/// that did not swap the transports for recording ones sent real email (test links to <c>marco@lube.it</c>) through the
/// owner's Gmail. No host this test process starts may have a real provider configured.
/// </summary>
public sealed class NoRealEmailTests
{
    /// <summary>A plain test host has Resend, Brevo and Google SMTP all unconfigured, whatever <c>.env</c> holds.</summary>
    [Fact]
    public void A_test_host_has_no_real_email_provider()
    {
        using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=unused"));

        var options = api.Services.GetRequiredService<IOptions<EmailOptions>>().Value;

        options.Resend.IsConfigured.Should().BeFalse();
        options.Brevo.IsConfigured.Should().BeFalse();
        options.GoogleSmtp.IsConfigured.Should().BeFalse();
    }
}
