using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Links;

/// <summary>
/// The email code behind the gate (L-2): asked for, then issued by the job that sends it, valid 10 minutes and spent
/// after 5 wrong tries or one right one.
/// </summary>
public sealed class EmailChallengeTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The link the code opens.</summary>
    private static readonly Guid _linkId = Guid.Parse("0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27");

    /// <summary>The email the code goes to.</summary>
    private static readonly EmailAddress _email = EmailAddress.Create("marco@lube.it");

    /// <summary>Asking for a code records who asked and raises the event the sending job listens to; no code exists yet.</summary>
    [Fact]
    public void Asking_for_a_code_raises_the_event_without_a_code()
    {
        var challenge = EmailChallenge.Request(_linkId, _email, isForward: true, _now);

        challenge.ShareLinkId.Should().Be(_linkId);
        challenge.Email.Should().Be(_email);
        challenge.IsForward.Should().BeTrue();
        challenge.RequestedAt.Should().Be(_now);
        challenge.CodeHash.Should().BeNull();
        challenge.ResendAt.Should().Be(_now.AddSeconds(60));
        challenge.AttemptsLeft.Should().Be(5);
        challenge.DomainEvents.Should().ContainSingle().Which.Should().Be(new EmailCodeRequested(challenge.Id));
    }

    /// <summary>Issuing stores the hash and starts the 10 minutes.</summary>
    [Fact]
    public void Issuing_a_code_starts_ten_minutes()
    {
        var challenge = EmailChallenge.Request(_linkId, _email, isForward: false, _now);

        challenge.IssueCode("hash-1", _now.AddSeconds(2)).Should().BeTrue();

        challenge.CodeHash.Should().Be("hash-1");
        challenge.ExpiresAt.Should().Be(_now.AddSeconds(2).AddMinutes(10));
    }

    /// <summary>A job that runs too late, or after the code was used, sends nothing.</summary>
    [Fact]
    public void A_stale_or_used_challenge_is_not_issued()
    {
        var late = EmailChallenge.Request(_linkId, _email, isForward: false, _now);
        late.IssueCode("hash", _now.AddMinutes(10)).Should().BeFalse();
        late.CodeHash.Should().BeNull();

        var used = Issued();
        used.Check(codeMatches: true, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Verified);
        used.IssueCode("hash-2", _now.AddMinutes(2)).Should().BeFalse();
        used.CodeHash.Should().Be("hash-1");
    }

    /// <summary>The right code verifies once; using it again is refused.</summary>
    [Fact]
    public void The_right_code_verifies_once()
    {
        var challenge = Issued();

        challenge.Check(codeMatches: true, _now.AddMinutes(3)).Should().Be(EmailCodeCheck.Verified);
        challenge.VerifiedAt.Should().Be(_now.AddMinutes(3));
        challenge.Check(codeMatches: true, _now.AddMinutes(4)).Should().Be(EmailCodeCheck.Used);
    }

    /// <summary>Wrong tries count down; the fifth spends the code, and even the right code no longer opens.</summary>
    [Fact]
    public void Five_wrong_tries_spend_the_code()
    {
        var challenge = Issued();

        for (var attempt = 1; attempt < 5; attempt++)
        {
            challenge.Check(codeMatches: false, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Wrong);
            challenge.AttemptsLeft.Should().Be(5 - attempt);
        }

        challenge.Check(codeMatches: false, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Exhausted);
        challenge.AttemptsLeft.Should().Be(0);
        challenge.Check(codeMatches: true, _now.AddMinutes(1)).Should().Be(EmailCodeCheck.Exhausted);
        challenge.VerifiedAt.Should().BeNull();
    }

    /// <summary>After ten minutes, or before the code was sent, the code does not open and no try is counted.</summary>
    [Fact]
    public void An_expired_or_unsent_code_does_not_open()
    {
        var expired = Issued();
        expired.Check(codeMatches: true, _now.AddMinutes(10)).Should().Be(EmailCodeCheck.Expired);
        expired.AttemptsLeft.Should().Be(5);

        var unsent = EmailChallenge.Request(_linkId, _email, isForward: false, _now);
        unsent.Check(codeMatches: false, _now.AddSeconds(5)).Should().Be(EmailCodeCheck.Expired);
        unsent.AttemptsLeft.Should().Be(5);
    }

    /// <summary>A challenge whose code was issued at <see cref="_now"/>.</summary>
    /// <returns>The challenge.</returns>
    private static EmailChallenge Issued()
    {
        var challenge = EmailChallenge.Request(_linkId, _email, isForward: false, _now);
        challenge.IssueCode("hash-1", _now);
        return challenge;
    }
}
