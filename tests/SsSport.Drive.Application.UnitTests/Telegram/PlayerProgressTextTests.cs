using SsSport.Drive.Application.Telegram.Progress;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>
/// The one progress message per player (T-6a) names the player first and lists the steps done so far, so the
/// analyst sees at a glance what is missing and whether an admin has to act.
/// </summary>
public sealed class PlayerProgressTextTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A player just created in the Mini App: links verified, one folder, the photo still to add.</summary>
    [Fact]
    public void New_player_without_a_photo()
    {
        var text = PlayerProgressText.Render(FilledPlayer(), folderCount: 1);

        text.Should().Be(string.Join('\n',
            "⏳ Arash Tehrani",
            "✅ Links verified",
            "✅ 1 Drive folder linked",
            "⬜ Photo: add it in the Mini App",
            "⬜ Waiting for the details above"));
    }

    /// <summary>With the photo and a second folder, the player only waits for an admin.</summary>
    [Fact]
    public void Complete_player_waits_for_an_admin()
    {
        var player = FilledPlayer();
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo-1.webp"));

        var text = PlayerProgressText.Render(player, folderCount: 2);

        text.Should().Be(string.Join('\n',
            "⏳ Arash Tehrani",
            "✅ Links verified",
            "✅ 2 Drive folders linked",
            "✅ Photo added",
            "⏳ Waiting for an admin to publish"));
    }

    /// <summary>When the owner is an admin (T-11), a complete player is ready to publish from the button, not waiting for someone else.</summary>
    [Fact]
    public void Complete_player_of_an_admin_is_ready_to_publish()
    {
        var player = FilledPlayer();
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo-1.webp"));

        var text = PlayerProgressText.Render(player, folderCount: 1, ownerCanPublish: true);

        text.Should().EndWith("\n⏳ Ready to publish: press Publish below");
    }

    /// <summary>An admin's incomplete player still waits for its details: there is nothing to publish yet.</summary>
    [Fact]
    public void Incomplete_player_of_an_admin_still_waits_for_details()
    {
        var text = PlayerProgressText.Render(FilledPlayer(), folderCount: 1, ownerCanPublish: true);

        text.Should().EndWith("\n⬜ Waiting for the details above");
    }

    /// <summary>Other missing details are listed by name, so the analyst knows what to fill in.</summary>
    [Fact]
    public void Lists_other_missing_details()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));

        var text = PlayerProgressText.Render(player, folderCount: 1);

        text.Should().Contain("⬜ Still missing: Nationality, Positions, Volleybox");
        text.Should().NotContain("Links verified", "the Volleybox link is not there yet");
    }

    /// <summary>Once an admin publishes, the message says so (transcoding steps follow in T-6b).</summary>
    [Fact]
    public void Published_player()
    {
        var player = FilledPlayer();
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo-1.webp"));
        player.Publish(_now);

        var text = PlayerProgressText.Render(player, folderCount: 1);

        text.Should().StartWith("✅ Arash Tehrani");
        text.Should().EndWith("✅ Published by an admin");
    }

    /// <summary>A player whose name is not known yet still gets a first line.</summary>
    [Fact]
    public void Unnamed_player()
    {
        PlayerProgressText.Render(Player.Create(PlayerCategory.Women, _now), folderCount: 0).Should().StartWith("⏳ New player");
    }

    /// <summary>Builds a player as the Mini App creates it: everything but the photo.</summary>
    /// <returns>The player.</returns>
    private static Player FilledPlayer()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter]));
        player.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/arash-tehrani-p48213"));
        return player;
    }
}
