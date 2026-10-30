using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>
/// The Player aggregate starts with only its category (details may be missing: the waiting list, D-5)
/// and records every change as a domain event.
/// </summary>
public sealed class PlayerTests
{
    /// <summary>A fixed creation time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A new player has an id, its category, its creation time and no details yet.</summary>
    [Fact]
    public void A_new_player_has_only_its_category()
    {
        var player = Player.Create(PlayerCategory.Women, _now);

        player.Id.Should().NotBeEmpty();
        player.Category.Should().Be(PlayerCategory.Women);
        player.CreatedAt.Should().Be(_now);
        player.Name.Should().BeNull();
        player.Slug.Should().BeNull();
        player.Nationality.Should().BeNull();
        player.Positions.Should().BeNull();
        player.Volleybox.Should().BeNull();
        player.DomainEvents.Should().ContainSingle().Which.Should().Be(new PlayerCreated(player.Id));
    }

    /// <summary>New players get time-ordered ids, so database inserts stay sequential.</summary>
    [Fact]
    public void Ids_are_version_7_guids()
    {
        Player.Create(PlayerCategory.Men, _now).Id.Version.Should().Be(7);
    }

    /// <summary>Renaming stores the name and the slug chosen for it, and raises an event.</summary>
    [Fact]
    public void Rename_sets_name_and_slug()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create("arash tehrani");

        player.Rename(name, Slug.FromName(name).WithSuffix(2));

        player.Name!.Value.Should().Be("Arash Tehrani");
        player.Slug!.Value.Should().Be("arash-tehrani-2");
        player.DomainEvents.Should().Contain(new PlayerDetailsChanged(player.Id));
    }

    /// <summary>Details can be set one by one, in any order.</summary>
    [Fact]
    public void Details_can_be_filled_in_any_order()
    {
        var player = Player.Create(PlayerCategory.Men, _now);

        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter, Position.Opposite]));
        player.ChangeNationality(Nationality.Create("ru"));
        player.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/kirill-vlasov-p555"));

        player.Positions!.Primary.Should().Be(Position.OutsideHitter);
        player.Nationality!.Code.Should().Be("RU");
        player.Volleybox!.PlayerId.Should().Be(555);
    }

    /// <summary>Setting a detail to the value it already has changes nothing and raises no event.</summary>
    [Fact]
    public void Unchanged_details_raise_no_event()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        player.ChangeNationality(Nationality.Create("IR"));
        player.ClearDomainEvents();

        player.ChangeNationality(Nationality.Create("ir"));

        player.DomainEvents.Should().BeEmpty();
    }

    /// <summary>The hero video can be chosen and cleared; choosing the same one again raises nothing.</summary>
    [Fact]
    public void Hero_can_be_set_and_cleared()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var video = FileOf(player.Id, "highlights.mp4");
        player.ClearDomainEvents();

        player.SetHero(video);
        player.SetHero(video);

        player.HeroAssetId.Should().Be(video.Id);
        player.DomainEvents.Should().ContainSingle().Which.Should().Be(new PlayerDetailsChanged(player.Id));

        player.ClearHero();

        player.HeroAssetId.Should().BeNull();
    }

    /// <summary>Only a visible video of the same player can be the hero (owner, 2026-10-31).</summary>
    /// <param name="case">What is wrong with the file.</param>
    /// <param name="code">The rule it breaks.</param>
    [Theory]
    [InlineData("document", "asset.hero_not_video")]
    [InlineData("hidden", "asset.hero_hidden")]
    [InlineData("other_player", "asset.hero_other_player")]
    public void Hero_must_be_a_visible_video_of_the_player(string @case, string code)
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var file = @case switch
        {
            "document" => FileOf(player.Id, "stats.pdf"),
            "other_player" => FileOf(Guid.CreateVersion7(_now), "highlights.mp4"),
            _ => FileOf(player.Id, "highlights.mp4"),
        };
        if (@case == "hidden")
        {
            file.Hide();
        }

        var choose = () => player.SetHero(file);

        choose.Should().Throw<DomainException>().Which.Code.Should().Be(code);
        player.HeroAssetId.Should().BeNull();
    }

    /// <summary>Builds a queued file of a player.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fileName">The file name; its extension gives the kind.</param>
    /// <returns>The asset.</returns>
    private static Asset FileOf(Guid playerId, string fileName) =>
        Asset.Add(playerId, null, AssetSource.Drive, null, AssetFile.Create(fileName, "9e107d9d372bb6826bd81d3542a419d6", 1024), fileName, AssetTags.Untagged, 0, _now);

    /// <summary>A player starts without an analyst (archive players) and can be assigned or reassigned to one.</summary>
    [Fact]
    public void Analyst_can_be_assigned_and_reassigned()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var analyst = Guid.CreateVersion7(_now);
        var admin = Guid.CreateVersion7(_now.AddMinutes(1));
        player.ClearDomainEvents();

        player.AnalystId.Should().BeNull();
        player.AssignAnalyst(analyst);
        player.AssignAnalyst(analyst);
        player.AssignAnalyst(admin);

        player.AnalystId.Should().Be(admin);
        player.DomainEvents.Should().HaveCount(2).And.AllBeEquivalentTo(new PlayerDetailsChanged(player.Id));
    }

    /// <summary>The QR code image key is stored on the player (I-8); the same key again changes nothing.</summary>
    [Fact]
    public void Qr_code_key_is_stored()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        player.ClearDomainEvents();

        player.SetQrCode(SsSport.Drive.Domain.Primitives.StorageKey.Create("qr/p1.png"));
        player.SetQrCode(SsSport.Drive.Domain.Primitives.StorageKey.Create("qr/p1.png"));

        player.QrCode!.Value.Should().Be("qr/p1.png");
        player.DomainEvents.Should().BeEmpty();
    }

    /// <summary>The category can be corrected (for example a folder moved between Men and Women).</summary>
    [Fact]
    public void Category_can_be_corrected()
    {
        var player = Player.Create(PlayerCategory.Men, _now);

        player.ChangeCategory(PlayerCategory.Women);

        player.Category.Should().Be(PlayerCategory.Women);
        player.DomainEvents.Should().Contain(new PlayerDetailsChanged(player.Id));
    }
}
