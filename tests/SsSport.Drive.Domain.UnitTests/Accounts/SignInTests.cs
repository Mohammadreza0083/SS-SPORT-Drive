using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Accounts;

/// <summary>
/// Email magic-link sign-in (A-1 admins, P-5 viewers; decisions §7a). A sign-in link is issued, armed with a token hash
/// inside the email job (so the token never sits in the request or a job argument), works once for 15 minutes, and
/// starts a session: 30 days for an admin, 7 days for a viewer. Viewers get an account on their first sign-in.
/// </summary>
public sealed class SignInTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The address.</summary>
    private static readonly EmailAddress _email = EmailAddress.Create("Owner@SS-Sport.co");

    /// <summary>An issued link has no token yet and cannot be redeemed.</summary>
    [Fact]
    public void An_issued_link_waits_for_its_token()
    {
        var link = SignInLink.Issue(SignInAudience.Admin, _email, _now);

        link.Audience.Should().Be(SignInAudience.Admin);
        link.Email.Value.Should().Be("owner@ss-sport.co");
        link.TokenHash.Should().BeNull();
        link.CreatedAt.Should().Be(_now);
        link.UsedAt.Should().BeNull();
        link.Redeem(_now).Should().BeFalse();
        SignInLink.Lifetime.Should().Be(TimeSpan.FromMinutes(15));
    }

    /// <summary>Arming stores the token hash and starts the 15 minutes.</summary>
    [Fact]
    public void Arming_starts_the_fifteen_minutes()
    {
        var link = SignInLink.Issue(SignInAudience.Viewer, _email, _now);

        link.Arm("hash-1", _now.AddSeconds(5)).Should().BeTrue();

        link.TokenHash.Should().Be("hash-1");
        link.ExpiresAt.Should().Be(_now.AddSeconds(5) + SignInLink.Lifetime);
    }

    /// <summary>A link armed too late (its job ran after 15 minutes) is not armed.</summary>
    [Fact]
    public void A_stale_link_is_not_armed()
    {
        var link = SignInLink.Issue(SignInAudience.Viewer, _email, _now);

        link.Arm("hash-1", _now + SignInLink.Lifetime).Should().BeFalse();

        link.TokenHash.Should().BeNull();
    }

    /// <summary>A retried email job re-arms with a new hash; only the newest token works.</summary>
    [Fact]
    public void Re_arming_replaces_the_token()
    {
        var link = SignInLink.Issue(SignInAudience.Viewer, _email, _now);
        link.Arm("hash-1", _now);

        link.Arm("hash-2", _now.AddMinutes(1)).Should().BeTrue();

        link.TokenHash.Should().Be("hash-2");
    }

    /// <summary>An armed link works once.</summary>
    [Fact]
    public void A_link_works_once()
    {
        var link = SignInLink.Issue(SignInAudience.Admin, _email, _now);
        link.Arm("hash-1", _now);

        link.Redeem(_now.AddMinutes(3)).Should().BeTrue();
        link.UsedAt.Should().Be(_now.AddMinutes(3));

        link.Redeem(_now.AddMinutes(4)).Should().BeFalse();
        link.Arm("hash-2", _now.AddMinutes(4)).Should().BeFalse();
    }

    /// <summary>A link stops working 15 minutes after it was armed.</summary>
    [Fact]
    public void A_link_expires()
    {
        var link = SignInLink.Issue(SignInAudience.Admin, _email, _now);
        link.Arm("hash-1", _now);

        link.Redeem(_now + SignInLink.Lifetime).Should().BeFalse();

        link.UsedAt.Should().BeNull();
    }

    /// <summary>The token hash is required.</summary>
    [Fact]
    public void Arming_needs_a_hash()
    {
        var link = SignInLink.Issue(SignInAudience.Admin, _email, _now);

        ((Action)(() => link.Arm(" ", _now))).Should().Throw<ArgumentException>();
    }

    /// <summary>An admin session lasts 30 days, a viewer session 7 days (owner, 2026-11-13).</summary>
    /// <param name="audience">Who signs in.</param>
    /// <param name="days">The lifetime in days.</param>
    [Theory]
    [InlineData(SignInAudience.Admin, 30)]
    [InlineData(SignInAudience.Viewer, 7)]
    public void Sessions_last_by_audience(SignInAudience audience, int days)
    {
        var subject = Guid.CreateVersion7();

        var session = AccountSession.Start(audience, subject, _email, "session-hash", _now);

        session.Audience.Should().Be(audience);
        session.SubjectId.Should().Be(subject);
        session.Email.Value.Should().Be("owner@ss-sport.co");
        session.TokenHash.Should().Be("session-hash");
        session.CreatedAt.Should().Be(_now);
        session.ExpiresAt.Should().Be(_now.AddDays(days));
        AccountSession.LifetimeOf(audience).Should().Be(TimeSpan.FromDays(days));
        session.IsActiveAt(_now.AddDays(days).AddTicks(-1)).Should().BeTrue();
        session.IsActiveAt(_now.AddDays(days)).Should().BeFalse();
    }

    /// <summary>Signing out ends the session at once; a second sign-out changes nothing.</summary>
    [Fact]
    public void Sign_out_ends_the_session()
    {
        var session = AccountSession.Start(SignInAudience.Admin, Guid.CreateVersion7(), _email, "session-hash", _now);

        session.End(_now.AddHours(1));
        session.End(_now.AddHours(2));

        session.EndedAt.Should().Be(_now.AddHours(1));
        session.IsActiveAt(_now.AddHours(1)).Should().BeFalse();
    }

    /// <summary>A session needs a token hash and a subject.</summary>
    [Fact]
    public void A_session_needs_a_hash_and_a_subject()
    {
        ((Action)(() => AccountSession.Start(SignInAudience.Viewer, Guid.CreateVersion7(), _email, "", _now))).Should().Throw<ArgumentException>();
        ((Action)(() => AccountSession.Start(SignInAudience.Viewer, Guid.Empty, _email, "hash", _now))).Should().Throw<ArgumentException>();
    }

    /// <summary>A viewer account is made on the first sign-in and remembers the latest one.</summary>
    [Fact]
    public void A_viewer_account_remembers_sign_ins()
    {
        var account = ViewerAccount.Register(EmailAddress.Create("Jan@Aurora.nl"), _now);

        account.Email.Value.Should().Be("jan@aurora.nl");
        account.CreatedAt.Should().Be(_now);
        account.LastSignedInAt.Should().Be(_now);

        account.SignedIn(_now.AddDays(3));

        account.LastSignedInAt.Should().Be(_now.AddDays(3));
        account.CreatedAt.Should().Be(_now);
    }
}
