using SsSport.Drive.Application.Telegram.Progress;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>
/// T-6b: the progress message adds the build of the player's files under the T-6a lines (Copy, Transcode &amp;
/// thumbnails, Pages, failed files, a progress bar with the time left) and, once the player is published and every
/// file is done, a result card with the counts and the player's public page.
/// </summary>
public sealed class PlayerProgressBuildTextTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The player's public page.</summary>
    private static readonly Uri _page = new("https://drive.ss-sport.co/players/arash-tehrani");

    /// <summary>The T-6a lines of a complete player waiting for an admin.</summary>
    private static readonly string[] _waitingLines =
    [
        "⏳ Arash Tehrani",
        "✅ Links verified",
        "✅ 1 Drive folder linked",
        "✅ Photo added",
        "⏳ Waiting for an admin to publish",
    ];

    /// <summary>Without files the text is exactly the T-6a text.</summary>
    [Fact]
    public void Without_files_the_text_is_unchanged()
    {
        var player = CompletePlayer();

        PlayerProgressText.Render(player, 1, BuildProgress.None, null, _page).Should().Be(PlayerProgressText.Render(player, 1));
    }

    /// <summary>While building: one line per step, the failed files, then the bar with the time left.</summary>
    [Fact]
    public void Shows_the_build_steps_and_the_bar()
    {
        var build = new BuildProgress(6, 4, false, 3, 1, false, 3, 1, false, 1, 63);

        var text = PlayerProgressText.Render(CompletePlayer(), 1, build, TimeSpan.FromMinutes(12), _page);

        text.Should().Be(string.Join('\n', [.. _waitingLines,
            "⏳ Copy: 4/6 files",
            "⏳ Transcode & thumbnails: 1/3 videos",
            "⏳ Pages: 1/3 documents",
            "⚠️ 1 file failed; an admin will retry it",
            "▓▓▓▓▓▓░░░░ 63% · about 12 min left"]));
    }

    /// <summary>A step that has nothing left shows ✅; kinds the player has none of are left out.</summary>
    [Fact]
    public void Done_steps_are_ticked_and_missing_kinds_left_out()
    {
        var build = new BuildProgress(2, 2, true, 2, 1, false, 0, 0, true, 0, 82);

        var text = PlayerProgressText.Render(CompletePlayer(), 1, build, TimeSpan.FromMinutes(3), _page);

        text.Should().Contain("✅ Copy: 2/2 files\n⏳ Transcode & thumbnails: 1/2 videos\n▓▓▓▓▓▓▓▓░░ 82% · about 3 min left");
        text.Should().NotContain("Pages");
    }

    /// <summary>Before anything is done the time left is not known yet.</summary>
    [Fact]
    public void Unknown_time_left()
    {
        var build = new BuildProgress(1, 0, false, 1, 0, false, 0, 0, true, 0, 0);

        PlayerProgressText.Render(CompletePlayer(), 1, build, null, _page).Should().EndWith("░░░░░░░░░░ 0% · estimating the time left");
    }

    /// <summary>The time left is rounded up to whole minutes, and shown in hours past an hour.</summary>
    /// <param name="minutes">The time left in minutes.</param>
    /// <param name="expected">The expected wording.</param>
    [Theory]
    [InlineData(0.5, "about 1 min left")]
    [InlineData(12.2, "about 13 min left")]
    [InlineData(60, "about 1 h left")]
    [InlineData(65, "about 1 h 5 min left")]
    [InlineData(120, "about 2 h left")]
    public void Time_left_wording(double minutes, string expected)
    {
        var build = new BuildProgress(2, 1, false, 0, 0, true, 2, 1, false, 0, 50);

        PlayerProgressText.Render(CompletePlayer(), 1, build, TimeSpan.FromMinutes(minutes), _page).Should().EndWith($"▓▓▓▓▓░░░░░ 50% · {expected}");
    }

    /// <summary>Several failed files are counted in the plural.</summary>
    [Fact]
    public void Several_failed_files()
    {
        var build = new BuildProgress(3, 1, true, 3, 1, true, 0, 0, true, 2, 100);

        PlayerProgressText.Render(CompletePlayer(), 1, build, null, _page).Should().Contain("⚠️ 2 files failed; an admin will retry them");
    }

    /// <summary>Finished but not published: the steps are ticked, no bar and no result card yet.</summary>
    [Fact]
    public void Finished_but_waiting_for_an_admin()
    {
        var build = new BuildProgress(3, 3, true, 2, 2, true, 1, 1, true, 0, 100);

        var text = PlayerProgressText.Render(CompletePlayer(), 1, build, null, _page);

        text.Should().Be(string.Join('\n', [.. _waitingLines,
            "✅ Copy: 3/3 files",
            "✅ Transcode & thumbnails: 2/2 videos",
            "✅ Pages: 1/1 documents"]));
    }

    /// <summary>Published and finished: the result card closes the message, with the counts and the public page.</summary>
    [Fact]
    public void Published_and_finished_shows_the_result_card()
    {
        var player = CompletePlayer();
        player.Publish(_now);
        var build = new BuildProgress(3, 3, true, 2, 2, true, 1, 1, true, 0, 100);

        var text = PlayerProgressText.Render(player, 1, build, null, _page);

        text.Should().Be(string.Join('\n',
            "✅ Arash Tehrani",
            "✅ Links verified",
            "✅ 1 Drive folder linked",
            "✅ Photo added",
            "✅ Published by an admin",
            "✅ Copy: 3/3 files",
            "✅ Transcode & thumbnails: 2/2 videos",
            "✅ Pages: 1/1 documents",
            "🎉 Live: 2 videos · 1 document",
            "🔗 https://drive.ss-sport.co/players/arash-tehrani"));
    }

    /// <summary>The card counts in the singular and leaves out kinds with nothing ready; no page, no link line.</summary>
    [Fact]
    public void Result_card_without_a_page()
    {
        var player = CompletePlayer();
        player.Publish(_now);
        var build = new BuildProgress(1, 1, true, 1, 1, true, 0, 0, true, 0, 100);

        var text = PlayerProgressText.Render(player, 1, build, null, null);

        text.Should().EndWith("✅ Transcode & thumbnails: 1/1 videos\n🎉 Live: 1 video");
    }

    /// <summary>No card while files are still building, and none when nothing became ready.</summary>
    [Fact]
    public void No_result_card_while_building_or_without_ready_files()
    {
        var player = CompletePlayer();
        player.Publish(_now);
        var building = new BuildProgress(2, 1, false, 2, 1, false, 0, 0, true, 0, 50);
        var allFailed = new BuildProgress(1, 0, true, 1, 0, true, 0, 0, true, 1, 100);

        PlayerProgressText.Render(player, 1, building, TimeSpan.FromMinutes(4), _page).Should().NotContain("🎉").And.EndWith("50% · about 4 min left");
        PlayerProgressText.Render(player, 1, allFailed, null, _page).Should().NotContain("🎉").And.EndWith("⚠️ 1 file failed; an admin will retry it");
    }

    /// <summary>Builds a complete player (photo included) as the Mini App leaves it.</summary>
    /// <returns>The player.</returns>
    private static Player CompletePlayer()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter]));
        player.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/arash-tehrani-p48213"));
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo-1.webp"));
        return player;
    }
}
