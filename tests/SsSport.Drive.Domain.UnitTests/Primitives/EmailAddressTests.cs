using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Primitives;

/// <summary>Email addresses are trimmed, lower-cased and never printed in full by accident.</summary>
public sealed class EmailAddressTests
{
    /// <summary>Valid addresses are normalised.</summary>
    [Fact]
    public void Valid_addresses_are_normalised()
    {
        EmailAddress.Create("  Scout@Example-Club.COM ").Value.Should().Be("scout@example-club.com");
    }

    /// <summary>Malformed addresses are rejected.</summary>
    /// <param name="input">The raw address.</param>
    [Theory]
    [InlineData("")]
    [InlineData("scout")]
    [InlineData("scout@")]
    [InlineData("@club.com")]
    [InlineData("scout@club")]
    [InlineData("sc out@club.com")]
    [InlineData("a@b@club.com")]
    public void Malformed_addresses_are_rejected(string input)
    {
        var create = () => EmailAddress.Create(input);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("email.invalid");
    }

    /// <summary>Addresses longer than 254 characters are rejected.</summary>
    [Fact]
    public void Overlong_addresses_are_rejected()
    {
        var create = () => EmailAddress.Create(new string('a', 250) + "@club.com");

        create.Should().Throw<DomainException>().Which.Code.Should().Be("email.invalid");
    }

    /// <summary>ToString masks the local part, so an address dropped into a log is not exposed.</summary>
    [Fact]
    public void ToString_is_masked()
    {
        EmailAddress.Create("scout@club.com").ToString().Should().Be("s***@club.com");
    }
}
