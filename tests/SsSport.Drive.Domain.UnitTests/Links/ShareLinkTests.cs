using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Links;

/// <summary>
/// A personal share link (L-1): made for one recipient and one published player (Personal) or several (Roster), with an
/// optional note and expiry. It can be paused and resumed, its expiry changed, and revoked for good.
/// </summary>
public sealed class ShareLinkTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The admin who creates the links.</summary>
    private static readonly Guid _adminId = Guid.Parse("0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27");

    /// <summary>The recipient.</summary>
    private readonly Recipient _recipient = Recipient.Create("Marco Rossi", "Lube Volley", null, [EmailAddress.Create("marco@lube.it")], _now);

    /// <summary>A link to one player is Personal, active, open, and carries what was chosen.</summary>
    [Fact]
    public void A_link_to_one_player_is_personal_and_open()
    {
        var player = PublishedPlayer("Arash Tehrani");

        var link = ShareLink.Create(
            ShareLinkCode.Create("Ab3xYz"), _recipient, [player], " Watch the second set. ", _now.AddDays(30), ShareLinkSettings.Default, _adminId, _now);

        link.Code.Value.Should().Be("Ab3xYz");
        link.Type.Should().Be(ShareLinkType.Personal);
        link.RecipientId.Should().Be(_recipient.Id);
        link.PlayerIds.Should().Equal(player.Id);
        link.Note.Should().Be("Watch the second set.");
        link.ExpiresAt.Should().Be(_now.AddDays(30));
        link.Status.Should().Be(ShareLinkStatus.Active);
        link.CreatedByUserId.Should().Be(_adminId);
        link.CreatedAt.Should().Be(_now);
        link.AccessAt(_now).Should().Be(ShareLinkAccess.Open);
        link.DomainEvents.Should().ContainSingle().Which.Should().Be(new ShareLinkCreated(link.Id));
    }

    /// <summary>The default settings: verify new devices, allow forwarding to any email, no open notifications.</summary>
    [Fact]
    public void Default_settings_follow_the_brief()
    {
        var link = NewLink();

        link.VerifyOnNewDevice.Should().BeTrue();
        link.AllowForwarding.Should().BeTrue();
        link.ForwardingScope.Should().Be(ForwardingScope.AnyEmail);
        link.NotifyOnOpen.Should().BeFalse();
    }

    /// <summary>Chosen settings are kept.</summary>
    [Fact]
    public void Chosen_settings_are_kept()
    {
        var settings = new ShareLinkSettings(VerifyOnNewDevice: false, AllowForwarding: false, ForwardingScope.SameDomain, NotifyOnOpen: true);

        var link = ShareLink.Create(ShareLinkCode.Generate(), _recipient, [PublishedPlayer("Arash Tehrani")], null, null, settings, _adminId, _now);

        link.VerifyOnNewDevice.Should().BeFalse();
        link.AllowForwarding.Should().BeFalse();
        link.ForwardingScope.Should().Be(ForwardingScope.SameDomain);
        link.NotifyOnOpen.Should().BeTrue();
    }

    /// <summary>A link to several players is a Roster, in the order chosen, without repeats; a blank note is no note.</summary>
    [Fact]
    public void A_link_to_several_players_is_a_roster()
    {
        var first = PublishedPlayer("Arash Tehrani");
        var second = PublishedPlayer("Kian Moradi");

        var link = ShareLink.Create(ShareLinkCode.Generate(), _recipient, [first, second, first], "  ", null, ShareLinkSettings.Default, _adminId, _now);

        link.Type.Should().Be(ShareLinkType.Roster);
        link.PlayerIds.Should().Equal(first.Id, second.Id);
        link.Note.Should().BeNull();
        link.ExpiresAt.Should().BeNull();
    }

    /// <summary>Links are only made for published players (decisions §4).</summary>
    [Fact]
    public void A_waiting_player_cannot_be_shared()
    {
        var waiting = Player.Create(PlayerCategory.Men, _now);

        var create = () => ShareLink.Create(ShareLinkCode.Generate(), _recipient, [PublishedPlayer("Arash Tehrani"), waiting], null, null, ShareLinkSettings.Default, _adminId, _now);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("share_link.player_not_published");
    }

    /// <summary>A link needs one to twenty players.</summary>
    /// <param name="count">How many players.</param>
    /// <param name="code">The rule broken.</param>
    [Theory]
    [InlineData(0, "share_link.no_players")]
    [InlineData(21, "share_link.too_many_players")]
    public void A_link_has_one_to_twenty_players(int count, string code)
    {
        var players = Enumerable.Range(0, count).Select(n => PublishedPlayer($"Player {(char)('A' + n)}")).ToList();

        var create = () => ShareLink.Create(ShareLinkCode.Generate(), _recipient, players, null, null, ShareLinkSettings.Default, _adminId, _now);

        create.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }

    /// <summary>The note is at most 500 characters.</summary>
    [Fact]
    public void The_note_is_short()
    {
        var create = () => ShareLink.Create(ShareLinkCode.Generate(), _recipient, [PublishedPlayer("Arash Tehrani")], new string('n', 501), null, ShareLinkSettings.Default, _adminId, _now);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("share_link.note_too_long");
    }

    /// <summary>An expiry must lie in the future.</summary>
    [Fact]
    public void The_expiry_lies_in_the_future()
    {
        var create = () => ShareLink.Create(ShareLinkCode.Generate(), _recipient, [PublishedPlayer("Arash Tehrani")], null, _now, ShareLinkSettings.Default, _adminId, _now);

        create.Should().Throw<DomainException>().Which.Code.Should().Be("share_link.expiry_in_past");
    }

    /// <summary>After its expiry a link no longer opens.</summary>
    [Fact]
    public void An_expired_link_does_not_open()
    {
        var link = ShareLink.Create(ShareLinkCode.Generate(), _recipient, [PublishedPlayer("Arash Tehrani")], null, _now.AddDays(7), ShareLinkSettings.Default, _adminId, _now);

        link.AccessAt(_now.AddDays(7).AddSeconds(-1)).Should().Be(ShareLinkAccess.Open);
        link.AccessAt(_now.AddDays(7)).Should().Be(ShareLinkAccess.Expired);
    }

    /// <summary>A paused link does not open until it is resumed; pausing twice changes nothing.</summary>
    [Fact]
    public void Pausing_and_resuming()
    {
        var link = NewLink();

        link.Pause().Should().BeTrue();
        link.Pause().Should().BeFalse();
        link.Status.Should().Be(ShareLinkStatus.Paused);
        link.AccessAt(_now).Should().Be(ShareLinkAccess.Paused);

        link.Resume().Should().BeTrue();
        link.Resume().Should().BeFalse();
        link.Status.Should().Be(ShareLinkStatus.Active);
        link.AccessAt(_now).Should().Be(ShareLinkAccess.Open);
    }

    /// <summary>A revoked link never opens again, and cannot be paused, resumed or extended; revoking twice changes nothing.</summary>
    [Fact]
    public void Revoking_is_final()
    {
        var link = NewLink();
        link.Pause();

        link.Revoke(_now.AddHours(1)).Should().BeTrue();
        link.Revoke(_now.AddHours(2)).Should().BeFalse();

        link.Status.Should().Be(ShareLinkStatus.Revoked);
        link.RevokedAt.Should().Be(_now.AddHours(1));
        link.AccessAt(_now.AddHours(3)).Should().Be(ShareLinkAccess.Revoked);
        foreach (var change in new Action[] { () => link.Pause(), () => link.Resume(), () => link.ChangeExpiry(null, _now) })
        {
            change.Should().Throw<DomainException>().Which.Code.Should().Be("share_link.revoked");
        }
    }

    /// <summary>The expiry can be moved later or removed, which opens an expired link again; a past expiry is refused.</summary>
    [Fact]
    public void Changing_the_expiry()
    {
        var link = ShareLink.Create(ShareLinkCode.Generate(), _recipient, [PublishedPlayer("Arash Tehrani")], null, _now.AddDays(7), ShareLinkSettings.Default, _adminId, _now);
        var later = _now.AddDays(10);

        link.ChangeExpiry(later.AddDays(30), later).Should().BeTrue();
        link.AccessAt(later).Should().Be(ShareLinkAccess.Open);
        link.ChangeExpiry(later.AddDays(30), later).Should().BeFalse();

        link.ChangeExpiry(null, later).Should().BeTrue();
        link.ExpiresAt.Should().BeNull();

        var past = () => link.ChangeExpiry(later.AddMinutes(-1), later);
        past.Should().Throw<DomainException>().Which.Code.Should().Be("share_link.expiry_in_past");
    }

    /// <summary>Builds an active link to one published player, without expiry.</summary>
    /// <returns>The link.</returns>
    private ShareLink NewLink() =>
        ShareLink.Create(ShareLinkCode.Generate(), _recipient, [PublishedPlayer("Arash Tehrani")], null, null, ShareLinkSettings.Default, _adminId, _now);

    /// <summary>Builds a complete, published player.</summary>
    /// <param name="fullName">The player's name.</param>
    /// <returns>The player.</returns>
    private static Player PublishedPlayer(string fullName)
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create(fullName);
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Setter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/{Slug.FromName(name)}-p{Math.Abs(fullName.GetHashCode(StringComparison.Ordinal)) % 90000 + 10000}"));
        player.Publish(_now);
        return player;
    }
}
