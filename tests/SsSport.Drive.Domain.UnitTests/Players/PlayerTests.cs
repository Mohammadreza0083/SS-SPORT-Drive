using SsSport.Drive.Domain.Players;

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
