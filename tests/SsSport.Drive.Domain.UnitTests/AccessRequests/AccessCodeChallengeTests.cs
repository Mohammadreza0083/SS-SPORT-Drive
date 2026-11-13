using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.AccessRequests;

/// <summary>
/// The email code a visitor types before asking for access (P-8, decisions §7 "Anti-spam"): the same rules as the
/// link gate's code (6 digits, 10 minutes, 5 tries, 60 s before another), but tied to no link.
/// </summary>
public sealed class AccessCodeChallengeTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The visitor's email; any ordinary address is fine (owner, 2026-11-12).</summary>
    private static readonly EmailAddress _email = EmailAddress.Create("j.demir@gmail.com");

    /// <summary>Asking records who asked and raises the event the sending job listens to; no code exists yet.</summary>
    [Fact]
    public void Asking_for_a_code_raises_the_event_without_a_code()
    {
        var challenge = AccessCodeChallenge.Request(_email, _now);

        challenge.Email.Should().Be(_email);
        challenge.RequestedAt.Should().Be(_now);
        challenge.CodeHash.Should().BeNull();
        challenge.ResendAt.Should().Be(_now.AddSeconds(60));
        challenge.AttemptsLeft.Should().Be(5);
        challenge.DomainEvents.Should().ContainSingle().Which.Should().Be(new AccessCodeRequested(challenge.Id));
    }

    /// <summary>The limits are the gate's own, so both flows behave the same for a visitor.</summary>
    [Fact]
    public void The_limits_match_the_link_gate()
    {
        AccessCodeChallenge.CodeLength.Should().Be(EmailChallenge.CodeLength);
        AccessCodeChallenge.MaxAttempts.Should().Be(EmailChallenge.MaxAttempts);
        AccessCodeChallenge.CodeLifetime.Should().Be(EmailChallenge.CodeLifetime);
        AccessCodeChallenge.ResendAfter.Should().Be(EmailChallenge.ResendAfter);
    }

    /// <summary>Issuing stores the hash and starts the 10 minutes.</summary>
    [Fact]
    public void Issuing_a_code_starts_ten_minutes()
    {
        var challenge = AccessCodeChallenge.Request(_email, _now);

        challenge.IssueCode("hash-1", _now.AddSeconds(2)).Should().BeTrue();

        challenge.CodeHash.Should().Be("hash-1");
        challenge.ExpiresAt.Should().Be(_now.AddSeconds(2).AddMinutes(10));
    }

    /// <summary>A job that runs too late, or after the code was used, sends nothing.</summary>
    [Fact]
    public void A_stale_or_used_challenge_is_not_issued()
    {
        var late = AccessCodeChallenge.Request(_email, _now);
        late.IssueCode("hash", _now.AddMinutes(10)).Should().BeFalse();
        late.CodeHash.Should().BeNull();

        var used = Issued();
        used.Check(codeMatches: true, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Verified);
        used.IssueCode("hash-2", _now.AddMinutes(2)).Should().BeFalse();
        used.CodeHash.Should().Be("hash-1");
    }

    /// <summary>The right code verifies once; typing it again is "used".</summary>
    [Fact]
    public void The_right_code_verifies_once()
    {
        var challenge = Issued();

        challenge.Check(codeMatches: true, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Verified);

        challenge.VerifiedAt.Should().Be(_now.AddMinutes(1));
        challenge.Check(codeMatches: true, _now.AddMinutes(2)).Should().Be(EmailCodeCheck.Used);
    }

    /// <summary>Each wrong code uses a try; the fifth spends the code, and even the right one is refused after that.</summary>
    [Fact]
    public void Five_wrong_codes_spend_the_code()
    {
        var challenge = Issued();

        for (var i = 1; i <= 4; i++)
        {
            challenge.Check(codeMatches: false, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Wrong);
            challenge.AttemptsLeft.Should().Be(5 - i);
        }

        challenge.Check(codeMatches: false, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Exhausted);
        challenge.Check(codeMatches: true, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Exhausted);
        challenge.AttemptsLeft.Should().Be(0);
    }

    /// <summary>A code typed after its 10 minutes, or one never sent, is expired and uses no try.</summary>
    [Fact]
    public void An_old_or_unsent_code_is_expired()
    {
        var challenge = Issued();
        challenge.Check(codeMatches: true, _now.AddMinutes(11)).Should().Be(EmailCodeCheck.Expired);
        challenge.AttemptsLeft.Should().Be(5);

        AccessCodeChallenge.Request(_email, _now).Check(codeMatches: true, _now).Should().Be(EmailCodeCheck.Expired);
    }

    /// <summary>A challenge with its code sent at <see cref="_now"/>.</summary>
    /// <returns>The challenge.</returns>
    private static AccessCodeChallenge Issued()
    {
        var challenge = AccessCodeChallenge.Request(_email, _now);
        challenge.IssueCode("hash-1", _now);
        return challenge;
    }
}
