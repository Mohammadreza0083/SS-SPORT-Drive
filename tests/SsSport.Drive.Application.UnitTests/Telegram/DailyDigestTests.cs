using SsSport.Drive.Application.Telegram.Digest;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>
/// The admins' daily digest (L-7, owner-approved 2026-11-12), sent at 09:00 Tehran to every active admin in the bot. It
/// covers yesterday in Tehran: watching (links opened, viewers, plays, minutes, the top three players), what is waiting
/// on the admins (forwards, the waiting list, access requests once P-4 exists) and emails that failed after every retry.
/// It is sent even on a day with no activity.
/// </summary>
public sealed class DailyDigestTests
{
    /// <summary>Tehran: UTC+03:30 all year (no daylight saving since 2022). A fixed zone, because hosts with invariant globalization cannot look IANA ids up.</summary>
    private static readonly TimeZoneInfo _tehran = TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromMinutes(210), "Tehran", "Tehran");

    /// <summary>Wednesday 11 November 2026.</summary>
    private static readonly DateOnly _day = new(2026, 11, 11);

    /// <summary>At 09:00 Tehran on 12 November the digest covers 11 November, midnight to midnight Tehran time.</summary>
    [Fact]
    public void The_window_is_yesterday_in_tehran()
    {
        var window = DigestWindow.Yesterday(new DateTimeOffset(2026, 11, 12, 5, 30, 0, TimeSpan.Zero), _tehran);

        window.Day.Should().Be(_day);
        window.Start.Should().Be(new DateTimeOffset(2026, 11, 10, 20, 30, 0, TimeSpan.Zero));
        window.End.Should().Be(new DateTimeOffset(2026, 11, 11, 20, 30, 0, TimeSpan.Zero));
    }

    /// <summary>Just after midnight UTC it is already morning in Tehran; "yesterday" follows Tehran's calendar, not UTC's.</summary>
    [Fact]
    public void Yesterday_follows_the_tehran_calendar()
    {
        var window = DigestWindow.Yesterday(new DateTimeOffset(2026, 11, 11, 21, 0, 0, TimeSpan.Zero), _tehran);

        window.Day.Should().Be(_day, "21:00 UTC on the 11th is 00:30 on the 12th in Tehran");
    }

    /// <summary>A full day: every line, in order.</summary>
    [Fact]
    public void A_busy_day()
    {
        var facts = new DigestFacts
        {
            Day = _day,
            LinksOpened = 4,
            Viewers = 3,
            Plays = 12,
            WatchMinutes = 87,
            TopPlayers = [new DigestTopPlayer("Arash Tehrani", 40), new DigestTopPlayer("Sara Moradi", 30), new DigestTopPlayer("Reza Karimi", 17)],
            PendingForwards = 2,
            WaitingPlayers = 5,
            OpenAccessRequests = 3,
            FailedEmails = 2,
        };

        DailyDigestText.Render(facts).Split('\n').Should().Equal(
            "📊 Daily digest · Wed 11 Nov 2026",
            "Yesterday: 4 links opened · 3 viewers · 12 plays · 87 min watched",
            "Top players: Arash Tehrani 40 min · Sara Moradi 30 min · Reza Karimi 17 min",
            "Waiting on you: 2 forwards to keep or block · 5 players on the waiting list · 3 access requests",
            "⚠️ 2 emails failed after every retry");
    }

    /// <summary>Counts of one are singular.</summary>
    [Fact]
    public void Ones_are_singular()
    {
        var facts = new DigestFacts
        {
            Day = _day,
            LinksOpened = 1,
            Viewers = 1,
            Plays = 1,
            WatchMinutes = 1,
            TopPlayers = [new DigestTopPlayer("Arash Tehrani", 1)],
            PendingForwards = 1,
            WaitingPlayers = 1,
            OpenAccessRequests = 1,
            FailedEmails = 1,
        };

        DailyDigestText.Render(facts).Split('\n').Should().Equal(
            "📊 Daily digest · Wed 11 Nov 2026",
            "Yesterday: 1 link opened · 1 viewer · 1 play · 1 min watched",
            "Top players: Arash Tehrani 1 min",
            "Waiting on you: 1 forward to keep or block · 1 player on the waiting list · 1 access request",
            "⚠️ 1 email failed after every retry");
    }

    /// <summary>A quiet day is still sent (owner): no activity, nothing waiting, no failures.</summary>
    [Fact]
    public void A_quiet_day_is_still_sent()
    {
        var facts = new DigestFacts { Day = _day };

        DailyDigestText.Render(facts).Split('\n').Should().Equal(
            "📊 Daily digest · Wed 11 Nov 2026",
            "Yesterday: no activity.",
            "Nothing is waiting on you.");
    }

    /// <summary>
    /// Lines with nothing to say are left out: no top players without plays, only the waiting items that are above zero,
    /// and no access requests part until P-4 counts them (<see langword="null"/>).
    /// </summary>
    [Fact]
    public void Empty_parts_are_left_out()
    {
        var facts = new DigestFacts { Day = _day, LinksOpened = 2, Viewers = 2, WaitingPlayers = 3, OpenAccessRequests = null };

        DailyDigestText.Render(facts).Split('\n').Should().Equal(
            "📊 Daily digest · Wed 11 Nov 2026",
            "Yesterday: 2 links opened · 2 viewers · 0 plays · 0 min watched",
            "Waiting on you: 3 players on the waiting list");
    }

    /// <summary>At most three top players are shown, in the order given (most minutes first).</summary>
    [Fact]
    public void At_most_three_top_players()
    {
        var facts = new DigestFacts
        {
            Day = _day,
            Plays = 9,
            WatchMinutes = 50,
            TopPlayers = [new("A B", 20), new("C D", 15), new("E F", 10), new("G H", 5)],
        };

        DailyDigestText.Render(facts).Split('\n')[2].Should().Be("Top players: A B 20 min · C D 15 min · E F 10 min");
    }

    /// <summary>Activity is any link opened, viewer, play or minute; waiting items alone are not activity.</summary>
    [Fact]
    public void Activity_is_watching_not_waiting()
    {
        new DigestFacts { Day = _day, PendingForwards = 4 }.HasActivity.Should().BeFalse();
        new DigestFacts { Day = _day, LinksOpened = 1 }.HasActivity.Should().BeTrue();
        new DigestFacts { Day = _day, Plays = 1 }.HasActivity.Should().BeTrue();
    }
}
