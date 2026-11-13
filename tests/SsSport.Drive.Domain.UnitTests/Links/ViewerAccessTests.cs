using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Links;

/// <summary>
/// Who gets through the gate (L-2): which emails a link admits, the device remembered for 90 days and the one-day
/// viewer session.
/// </summary>
public sealed class ViewerAccessTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The recipient, with two emails.</summary>
    private readonly Recipient _recipient = Recipient.Create(
        "Marco Rossi", "Lube Volley", null, [EmailAddress.Create("marco@lube.it"), EmailAddress.Create("m.rossi@gmail.com")], _now);

    /// <summary>Any of the recipient's emails is the recipient, whatever the forwarding switches say.</summary>
    [Fact]
    public void The_recipients_emails_are_admitted_as_the_recipient()
    {
        var link = Link(new ShareLinkSettings(VerifyOnNewDevice: true, AllowForwarding: false, ForwardingScope.AnyEmail, NotifyOnOpen: false));

        link.Admit(_recipient, EmailAddress.Create("M.Rossi@gmail.com")).Should().Be(EmailAdmission.Recipient);
    }

    /// <summary>With forwarding on for any email, another email is a forward.</summary>
    [Fact]
    public void Any_email_is_a_forward_when_forwarding_is_open()
    {
        var link = Link(ShareLinkSettings.Default);

        link.Admit(_recipient, EmailAddress.Create("scout@other.com")).Should().Be(EmailAdmission.Forward);
    }

    /// <summary>Same-domain forwarding admits only the domains of the recipient's emails.</summary>
    [Fact]
    public void Same_domain_forwarding_checks_the_domain()
    {
        var link = Link(new ShareLinkSettings(VerifyOnNewDevice: true, AllowForwarding: true, ForwardingScope.SameDomain, NotifyOnOpen: false));

        link.Admit(_recipient, EmailAddress.Create("coach@lube.it")).Should().Be(EmailAdmission.Forward);
        link.Admit(_recipient, EmailAddress.Create("coach@sub.lube.it")).Should().Be(EmailAdmission.Refused);
        link.Admit(_recipient, EmailAddress.Create("coach@other.com")).Should().Be(EmailAdmission.Refused);
    }

    /// <summary>With forwarding off, only the recipient gets in.</summary>
    [Fact]
    public void No_forwarding_refuses_other_emails()
    {
        var link = Link(new ShareLinkSettings(VerifyOnNewDevice: true, AllowForwarding: false, ForwardingScope.AnyEmail, NotifyOnOpen: false));

        link.Admit(_recipient, EmailAddress.Create("scout@other.com")).Should().Be(EmailAdmission.Refused);
    }

    /// <summary>Another recipient cannot be used to judge a link.</summary>
    [Fact]
    public void The_recipient_must_be_the_links()
    {
        var link = Link(ShareLinkSettings.Default);
        var stranger = Recipient.Create("Someone Else", null, null, [EmailAddress.Create("x@y.com")], _now);

        var act = () => link.Admit(stranger, EmailAddress.Create("x@y.com"));

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A trusted device is remembered for 90 days for one link, then is not.</summary>
    [Fact]
    public void A_device_is_trusted_for_ninety_days()
    {
        var linkId = Guid.CreateVersion7();
        var device = TrustedDevice.Trust("device-hash", linkId, EmailAddress.Create("marco@lube.it"), isForward: false, _now);

        device.TokenHash.Should().Be("device-hash");
        device.ShareLinkId.Should().Be(linkId);
        device.Email.Value.Should().Be("marco@lube.it");
        device.IsForward.Should().BeFalse();
        device.ExpiresAt.Should().Be(_now.AddDays(90));
        device.IsTrustedAt(_now.AddDays(90).AddSeconds(-1)).Should().BeTrue();
        device.IsTrustedAt(_now.AddDays(90)).Should().BeFalse();
    }

    /// <summary>Verifying again on the same device moves it to the new email and starts the 90 days again.</summary>
    [Fact]
    public void Verifying_again_renews_the_device()
    {
        var device = TrustedDevice.Trust("device-hash", Guid.CreateVersion7(), EmailAddress.Create("marco@lube.it"), isForward: false, _now);

        device.Retrust(EmailAddress.Create("scout@other.com"), isForward: true, _now.AddDays(10));
        device.Seen(_now.AddDays(11));

        device.Email.Value.Should().Be("scout@other.com");
        device.IsForward.Should().BeTrue();
        device.ExpiresAt.Should().Be(_now.AddDays(100));
        device.LastSeenAt.Should().Be(_now.AddDays(11));
    }

    /// <summary>A session lasts seven days (owner, 2026-11-13) and remembers the link, the email and the device.</summary>
    [Fact]
    public void A_session_lasts_seven_days()
    {
        var linkId = Guid.CreateVersion7();
        var deviceId = Guid.CreateVersion7();

        var session = ViewerSession.Start("session-hash", linkId, EmailAddress.Create("marco@lube.it"), isForward: false, deviceId, _now);

        session.TokenHash.Should().Be("session-hash");
        session.ShareLinkId.Should().Be(linkId);
        session.Email.Value.Should().Be("marco@lube.it");
        session.DeviceId.Should().Be(deviceId);
        session.ExpiresAt.Should().Be(_now.AddDays(7));
        session.IsActiveAt(_now.AddDays(7).AddMinutes(-1)).Should().BeTrue();
        session.IsActiveAt(_now.AddDays(7)).Should().BeFalse();
    }

    /// <summary>Blank token hashes are a programming error.</summary>
    [Fact]
    public void Token_hashes_are_required()
    {
        var email = EmailAddress.Create("marco@lube.it");

        ((Action)(() => TrustedDevice.Trust(" ", Guid.CreateVersion7(), email, isForward: false, _now))).Should().Throw<ArgumentException>();
        ((Action)(() => ViewerSession.Start("", Guid.CreateVersion7(), email, isForward: false, null, _now))).Should().Throw<ArgumentException>();
    }

    /// <summary>A link for the recipient with the given switches.</summary>
    /// <param name="settings">The switches.</param>
    /// <returns>The link.</returns>
    private ShareLink Link(ShareLinkSettings settings)
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Opposite]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/arash-tehrani-p70001"));
        player.Publish(_now);
        return ShareLink.Create(ShareLinkCode.Create("Ab3xYz"), _recipient, [player], null, _now.AddDays(30), settings, null, _now);
    }
}
