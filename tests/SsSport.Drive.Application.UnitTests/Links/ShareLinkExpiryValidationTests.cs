using SsSport.Drive.Application.Links;

namespace SsSport.Drive.Application.UnitTests.Links;

/// <summary>
/// Every share link expires (owner, 2026-11-13): creating a link or changing its expiry accepts only 7, 30 or 90 days,
/// never "no expiry". The validators refuse it with 400 before the handler runs.
/// </summary>
public sealed class ShareLinkExpiryValidationTests
{
    /// <summary>Creating a link needs one of the expiry choices.</summary>
    /// <param name="days">The requested days.</param>
    /// <param name="valid">Whether it is accepted.</param>
    [Theory]
    [InlineData(7, true)]
    [InlineData(30, true)]
    [InlineData(90, true)]
    [InlineData(null, false)]
    [InlineData(45, false)]
    public void Creating_needs_an_expiry(int? days, bool valid)
    {
        var command = new CreateShareLinkCommand(Guid.CreateVersion7(), null, [Guid.CreateVersion7()], null, days, null, null, null, null);

        var result = new CreateShareLinkCommandValidator().Validate(command);

        result.Errors.Any(error => error.PropertyName == "expiresInDays").Should().Be(!valid);
    }

    /// <summary>Changing the expiry needs one of the expiry choices; it cannot be removed.</summary>
    /// <param name="days">The requested days.</param>
    /// <param name="valid">Whether it is accepted.</param>
    [Theory]
    [InlineData(7, true)]
    [InlineData(90, true)]
    [InlineData(null, false)]
    [InlineData(1, false)]
    public void Changing_needs_an_expiry(int? days, bool valid)
    {
        var result = new ChangeShareLinkExpiryCommandValidator().Validate(new ChangeShareLinkExpiryCommand(Guid.CreateVersion7(), days));

        result.Errors.Any(error => error.PropertyName == "expiresInDays").Should().Be(!valid);
    }
}
