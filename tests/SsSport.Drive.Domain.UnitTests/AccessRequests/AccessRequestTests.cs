using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.AccessRequests;

/// <summary>
/// A visitor's request to see a locked player (P-3, P-7, P-8): who asked (a verified email), for which player, what
/// they wrote about themselves, and how the email was proven. It starts pending; P-4 adds approve and decline.
/// </summary>
public sealed class AccessRequestTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The player asked for.</summary>
    private static readonly Guid _playerId = Guid.Parse("0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e");

    /// <summary>The visitor's email.</summary>
    private static readonly EmailAddress _email = EmailAddress.Create("j.demir@aurora-volley.example");

    /// <summary>A new request is pending, keeps what was asked, and raises the event the admins hear (P-4).</summary>
    [Fact]
    public void A_new_request_is_pending_and_announced()
    {
        var request = AccessRequest.Submit(_playerId, _email, "  Aurora Volley, head scout. Looking for an opposite for 2027.  ", AccessRequestSource.EmailCode, _now);

        request.PlayerId.Should().Be(_playerId);
        request.Email.Should().Be(_email);
        request.About.Should().Be("Aurora Volley, head scout. Looking for an opposite for 2027.");
        request.Source.Should().Be(AccessRequestSource.EmailCode);
        request.Status.Should().Be(AccessRequestStatus.Pending);
        request.CreatedAt.Should().Be(_now);
        request.DomainEvents.Should().ContainSingle().Which.Should().Be(new AccessRequested(request.Id));
    }

    /// <summary>A signed-in viewer's request records that the email came from their session, not a code.</summary>
    [Fact]
    public void A_signed_in_request_records_its_source()
    {
        var request = AccessRequest.Submit(_playerId, _email, "Club scout.", AccessRequestSource.SignedIn, _now);

        request.Source.Should().Be(AccessRequestSource.SignedIn);
    }

    /// <summary>The description is 1 to 1000 characters after trimming (decisions §11, P-3 UI).</summary>
    /// <param name="about">The description.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_description_is_refused(string? about)
    {
        var act = () => AccessRequest.Submit(_playerId, _email, about, AccessRequestSource.EmailCode, _now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("access_request.about_invalid");
    }

    /// <summary>1000 characters is fine; 1001 is not.</summary>
    [Fact]
    public void The_description_is_at_most_1000_characters()
    {
        AccessRequest.Submit(_playerId, _email, new string('a', 1000), AccessRequestSource.EmailCode, _now).About.Should().HaveLength(1000);

        var act = () => AccessRequest.Submit(_playerId, _email, new string('a', 1001), AccessRequestSource.EmailCode, _now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("access_request.about_invalid");
        AccessRequest.MaxAboutLength.Should().Be(1000);
    }

    /// <summary>A request needs a player and an email.</summary>
    [Fact]
    public void A_request_needs_a_player_and_an_email()
    {
        var noPlayer = () => AccessRequest.Submit(Guid.Empty, _email, "Scout.", AccessRequestSource.EmailCode, _now);
        var noEmail = () => AccessRequest.Submit(_playerId, null!, "Scout.", AccessRequestSource.EmailCode, _now);

        noPlayer.Should().Throw<ArgumentException>();
        noEmail.Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- P-4: the admin's decision

    /// <summary>The admin who decides.</summary>
    private static readonly Guid _adminId = Guid.Parse("0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27");

    /// <summary>The personal link an approval created.</summary>
    private static readonly Guid _linkId = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c");

    /// <summary>Approving records who, when and which personal link was made for the requester (P-4).</summary>
    [Fact]
    public void Approving_records_the_admin_the_time_and_the_link()
    {
        var request = AccessRequest.Submit(_playerId, _email, "Scout.", AccessRequestSource.EmailCode, _now);

        var decided = request.Approve(_adminId, _linkId, _now.AddHours(2));

        decided.Should().BeTrue();
        request.Status.Should().Be(AccessRequestStatus.Approved);
        request.DecidedByUserId.Should().Be(_adminId);
        request.DecidedAt.Should().Be(_now.AddHours(2));
        request.ShareLinkId.Should().Be(_linkId);
    }

    /// <summary>Declining records who and when, and no link.</summary>
    [Fact]
    public void Declining_records_the_admin_and_the_time()
    {
        var request = AccessRequest.Submit(_playerId, _email, "Scout.", AccessRequestSource.EmailCode, _now);

        var decided = request.Decline(_adminId, _now.AddHours(1));

        decided.Should().BeTrue();
        request.Status.Should().Be(AccessRequestStatus.Declined);
        request.DecidedByUserId.Should().Be(_adminId);
        request.DecidedAt.Should().Be(_now.AddHours(1));
        request.ShareLinkId.Should().BeNull();
    }

    /// <summary>A new request has no decision yet.</summary>
    [Fact]
    public void A_new_request_has_no_decision()
    {
        var request = AccessRequest.Submit(_playerId, _email, "Scout.", AccessRequestSource.EmailCode, _now);

        request.DecidedByUserId.Should().BeNull();
        request.DecidedAt.Should().BeNull();
        request.ShareLinkId.Should().BeNull();
    }

    /// <summary>The first decision is final: a second approve or decline returns false and changes nothing (two admins pressing at once).</summary>
    [Fact]
    public void The_first_decision_is_final()
    {
        var approved = AccessRequest.Submit(_playerId, _email, "Scout.", AccessRequestSource.EmailCode, _now);
        approved.Approve(_adminId, _linkId, _now);

        approved.Decline(Guid.NewGuid(), _now.AddMinutes(1)).Should().BeFalse();
        approved.Approve(Guid.NewGuid(), Guid.NewGuid(), _now.AddMinutes(1)).Should().BeFalse();
        approved.Status.Should().Be(AccessRequestStatus.Approved);
        approved.DecidedByUserId.Should().Be(_adminId);
        approved.ShareLinkId.Should().Be(_linkId);

        var declined = AccessRequest.Submit(_playerId, _email, "Scout.", AccessRequestSource.EmailCode, _now);
        declined.Decline(_adminId, _now);

        declined.Approve(Guid.NewGuid(), _linkId, _now.AddMinutes(1)).Should().BeFalse();
        declined.Status.Should().Be(AccessRequestStatus.Declined);
        declined.ShareLinkId.Should().BeNull();
    }

    /// <summary>A decision needs an admin, and an approval needs its link.</summary>
    [Fact]
    public void A_decision_needs_an_admin_and_an_approval_needs_a_link()
    {
        var request = AccessRequest.Submit(_playerId, _email, "Scout.", AccessRequestSource.EmailCode, _now);

        var noAdmin = () => request.Approve(Guid.Empty, _linkId, _now);
        var noLink = () => request.Approve(_adminId, Guid.Empty, _now);
        var noDecliner = () => request.Decline(Guid.Empty, _now);

        noAdmin.Should().Throw<ArgumentException>();
        noLink.Should().Throw<ArgumentException>();
        noDecliner.Should().Throw<ArgumentException>();
        request.Status.Should().Be(AccessRequestStatus.Pending);
    }
}
