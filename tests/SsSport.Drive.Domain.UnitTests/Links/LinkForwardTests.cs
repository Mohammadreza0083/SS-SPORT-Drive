using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Links;

/// <summary>
/// A forwarded link (L-3): another email verified on a link waits for an admin, who keeps it (it opens) or blocks it (it
/// never opens again). The first decision is final.
/// </summary>
public sealed class LinkForwardTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The link.</summary>
    private static readonly Guid _linkId = Guid.CreateVersion7();

    /// <summary>The admin who decides.</summary>
    private static readonly Guid _adminId = Guid.CreateVersion7();

    /// <summary>A new forward waits for an admin and raises the alert event.</summary>
    [Fact]
    public void A_new_forward_waits_and_alerts()
    {
        var forward = LinkForward.Record(_linkId, EmailAddress.Create("Scout@Other.com"), _now);

        forward.Status.Should().Be(ForwardStatus.Pending);
        forward.ShareLinkId.Should().Be(_linkId);
        forward.Email.Value.Should().Be("scout@other.com");
        forward.FirstVerifiedAt.Should().Be(_now);
        forward.DecidedAt.Should().BeNull();
        forward.DecidedByUserId.Should().BeNull();
        forward.OpensLink.Should().BeFalse();
        forward.DomainEvents.Should().ContainSingle().Which.Should().Be(new LinkForwarded(forward.Id));
    }

    /// <summary>Keep opens the link for the email and records who decided.</summary>
    [Fact]
    public void Keep_opens_the_link()
    {
        var forward = LinkForward.Record(_linkId, EmailAddress.Create("scout@other.com"), _now);

        forward.Decide(keep: true, _adminId, _now.AddMinutes(5)).Should().BeTrue();

        forward.Status.Should().Be(ForwardStatus.Kept);
        forward.OpensLink.Should().BeTrue();
        forward.DecidedAt.Should().Be(_now.AddMinutes(5));
        forward.DecidedByUserId.Should().Be(_adminId);
    }

    /// <summary>Block keeps the link closed to the email.</summary>
    [Fact]
    public void Block_closes_the_link()
    {
        var forward = LinkForward.Record(_linkId, EmailAddress.Create("scout@other.com"), _now);

        forward.Decide(keep: false, _adminId, _now.AddMinutes(5)).Should().BeTrue();

        forward.Status.Should().Be(ForwardStatus.Blocked);
        forward.OpensLink.Should().BeFalse();
    }

    /// <summary>The first decision is final: a second one changes nothing.</summary>
    [Fact]
    public void The_first_decision_is_final()
    {
        var forward = LinkForward.Record(_linkId, EmailAddress.Create("scout@other.com"), _now);
        forward.Decide(keep: true, _adminId, _now.AddMinutes(5));

        forward.Decide(keep: false, Guid.CreateVersion7(), _now.AddMinutes(9)).Should().BeFalse();

        forward.Status.Should().Be(ForwardStatus.Kept);
        forward.DecidedByUserId.Should().Be(_adminId);
        forward.DecidedAt.Should().Be(_now.AddMinutes(5));
    }
}
