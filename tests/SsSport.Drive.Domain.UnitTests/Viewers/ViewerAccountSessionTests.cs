using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Viewers;

namespace SsSport.Drive.Domain.UnitTests.Viewers;

/// <summary>
/// A signed-in viewer's session on the public site (decisions §7a: one day). P-8 reads it (one-click access request,
/// the email in the nav) and ends it (sign out); P-5 starts it from the magic link. Only a keyed hash of its token is kept.
/// </summary>
public sealed class ViewerAccountSessionTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The viewer's email.</summary>
    private static readonly EmailAddress _email = EmailAddress.Create("j.demir@aurora-volley.example");

    /// <summary>A session lasts one day.</summary>
    [Fact]
    public void A_session_lasts_one_day()
    {
        var session = ViewerAccountSession.Start("hash-1", _email, _now);

        ViewerAccountSession.Lifetime.Should().Be(TimeSpan.FromDays(1));
        session.TokenHash.Should().Be("hash-1");
        session.Email.Should().Be(_email);
        session.StartedAt.Should().Be(_now);
        session.ExpiresAt.Should().Be(_now.AddDays(1));
        session.SignedOutAt.Should().BeNull();
        session.IsLiveAt(_now.AddHours(23)).Should().BeTrue();
        session.IsLiveAt(_now.AddDays(1)).Should().BeFalse();
    }

    /// <summary>Signing out ends the session at once; doing it twice keeps the first time.</summary>
    [Fact]
    public void Signing_out_ends_the_session()
    {
        var session = ViewerAccountSession.Start("hash-1", _email, _now);

        session.SignOut(_now.AddMinutes(5));
        session.SignOut(_now.AddMinutes(9));

        session.SignedOutAt.Should().Be(_now.AddMinutes(5));
        session.IsLiveAt(_now.AddMinutes(6)).Should().BeFalse();
    }

    /// <summary>A session needs a token hash and an email.</summary>
    [Fact]
    public void A_session_needs_a_hash_and_an_email()
    {
        var noHash = () => ViewerAccountSession.Start(" ", _email, _now);
        var noEmail = () => ViewerAccountSession.Start("hash-1", null!, _now);

        noHash.Should().Throw<ArgumentException>();
        noEmail.Should().Throw<ArgumentNullException>();
    }
}
