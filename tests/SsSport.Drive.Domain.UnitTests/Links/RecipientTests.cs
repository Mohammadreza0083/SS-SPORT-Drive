using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Links;

/// <summary>
/// A recipient (L-1): the club, scout or agent a personal link is made for: a name, an optional organisation and
/// country, and one to five email addresses that pass the email gate.
/// </summary>
public sealed class RecipientTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A recipient keeps its trimmed details and its emails in order, without repeats.</summary>
    [Fact]
    public void A_recipient_keeps_its_details_and_unique_emails()
    {
        var recipient = Recipient.Create(
            "  Marco Rossi ",
            " Lube Volley ",
            Nationality.Create("IT"),
            [EmailAddress.Create("marco@lube.it"), EmailAddress.Create("scouting@lube.it"), EmailAddress.Create("MARCO@lube.it")],
            _now);

        recipient.Name.Should().Be("Marco Rossi");
        recipient.Organisation.Should().Be("Lube Volley");
        recipient.Country!.Code.Should().Be("IT");
        recipient.Emails.Select(email => email.Value).Should().Equal("marco@lube.it", "scouting@lube.it");
        recipient.CreatedAt.Should().Be(_now);
    }

    /// <summary>The organisation and the country may be left out; a blank organisation is no organisation.</summary>
    [Fact]
    public void Organisation_and_country_are_optional()
    {
        var recipient = Recipient.Create("Marco Rossi", "  ", null, [EmailAddress.Create("marco@lube.it")], _now);

        recipient.Organisation.Should().BeNull();
        recipient.Country.Should().BeNull();
    }

    /// <summary>A recipient needs a name of at most 120 characters.</summary>
    /// <param name="name">The name.</param>
    /// <param name="code">The rule broken.</param>
    [Theory]
    [InlineData(null, "recipient.name_required")]
    [InlineData("   ", "recipient.name_required")]
    [InlineData("a121", "recipient.name_too_long")]
    public void A_recipient_needs_a_short_name(string? name, string code)
    {
        var raw = name == "a121" ? new string('a', 121) : name;

        var create = () => Recipient.Create(raw, null, null, [EmailAddress.Create("marco@lube.it")], _now);

        create.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }

    /// <summary>The organisation is at most 120 characters.</summary>
    [Fact]
    public void The_organisation_is_short()
    {
        var create = () => Recipient.Create("Marco Rossi", new string('o', 121), null, [EmailAddress.Create("marco@lube.it")], _now);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("recipient.organisation_too_long");
    }

    /// <summary>A recipient needs at least one email and at most five.</summary>
    /// <param name="count">How many different emails.</param>
    /// <param name="code">The rule broken.</param>
    [Theory]
    [InlineData(0, "recipient.email_required")]
    [InlineData(6, "recipient.too_many_emails")]
    public void A_recipient_has_one_to_five_emails(int count, string code)
    {
        var emails = Enumerable.Range(1, count).Select(n => EmailAddress.Create($"scout{n}@club.com")).ToList();

        var create = () => Recipient.Create("Marco Rossi", null, null, emails, _now);

        create.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }

    /// <summary>Whether an email belongs to the recipient ignores case.</summary>
    [Fact]
    public void Knows_its_own_emails()
    {
        var recipient = Recipient.Create("Marco Rossi", null, null, [EmailAddress.Create("marco@lube.it")], _now);

        recipient.HasEmail(EmailAddress.Create("Marco@Lube.it")).Should().BeTrue();
        recipient.HasEmail(EmailAddress.Create("someone@lube.it")).Should().BeFalse();
    }
}
