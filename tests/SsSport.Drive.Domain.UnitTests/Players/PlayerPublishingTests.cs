using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>
/// The waiting list (D-5): a player stays waiting until name, nationality, positions, Volleybox and photo are all set,
/// and only then can be published.
/// </summary>
public sealed class PlayerPublishingTests
{
    /// <summary>A fixed creation time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A later time for publishing.</summary>
    private static readonly DateTimeOffset _later = _now.AddHours(2);

    /// <summary>A new player waits, is incomplete and misses every detail.</summary>
    [Fact]
    public void A_new_player_is_waiting_and_misses_every_detail()
    {
        var player = Player.Create(PlayerCategory.Men, _now);

        player.Status.Should().Be(PlayerStatus.Waiting);
        player.IsPublished.Should().BeFalse();
        player.PublishedAt.Should().BeNull();
        player.Photo.Should().BeNull();
        player.IsComplete.Should().BeFalse();
        player.MissingDetails().Should().Equal(
            PlayerDetail.Name, PlayerDetail.Nationality, PlayerDetail.Positions, PlayerDetail.Volleybox, PlayerDetail.Photo);
    }

    /// <summary>Each filled detail drops out of the missing list.</summary>
    [Fact]
    public void Filled_details_are_no_longer_missing()
    {
        var player = Player.Create(PlayerCategory.Women, _now);

        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo.webp"));

        player.MissingDetails().Should().Equal(PlayerDetail.Name, PlayerDetail.Positions, PlayerDetail.Volleybox);
        player.IsComplete.Should().BeFalse();
    }

    /// <summary>A player with every detail is complete.</summary>
    [Fact]
    public void A_player_with_every_detail_is_complete()
    {
        var player = CompletePlayer();

        player.MissingDetails().Should().BeEmpty();
        player.IsComplete.Should().BeTrue();
    }

    /// <summary>Setting the photo stores it and raises a details-changed event; the same photo again raises nothing.</summary>
    [Fact]
    public void Changing_the_photo_raises_an_event_once()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        player.ClearDomainEvents();

        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo.webp"));
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo.webp"));

        player.Photo!.StorageKey.Should().Be("players/p1/photo.webp");
        player.DomainEvents.Should().ContainSingle().Which.Should().Be(new PlayerDetailsChanged(player.Id));
    }

    /// <summary>Publishing an incomplete player fails, names what is missing and changes nothing.</summary>
    [Fact]
    public void An_incomplete_player_cannot_be_published()
    {
        var player = CompletePlayerExceptVolleybox();
        player.ClearDomainEvents();

        var publish = () => player.Publish(_later);

        var error = publish.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("player.incomplete");
        error.Message.Should().Contain("Volleybox");
        player.Status.Should().Be(PlayerStatus.Waiting);
        player.PublishedAt.Should().BeNull();
        player.DomainEvents.Should().BeEmpty();
    }

    /// <summary>Publishing a complete player marks it published, records the time and raises PlayerPublished.</summary>
    [Fact]
    public void A_complete_player_can_be_published()
    {
        var player = CompletePlayer();
        player.ClearDomainEvents();

        player.Publish(_later);

        player.Status.Should().Be(PlayerStatus.Published);
        player.IsPublished.Should().BeTrue();
        player.PublishedAt.Should().Be(_later);
        player.DomainEvents.Should().ContainSingle().Which.Should().Be(new PlayerPublished(player.Id));
    }

    /// <summary>Publishing twice keeps the first time and raises no second event.</summary>
    [Fact]
    public void Publishing_again_changes_nothing()
    {
        var player = CompletePlayer();
        player.Publish(_later);
        player.ClearDomainEvents();

        player.Publish(_later.AddDays(1));

        player.PublishedAt.Should().Be(_later);
        player.DomainEvents.Should().BeEmpty();
    }

    /// <summary>Builds a player with every detail except Volleybox.</summary>
    /// <returns>The player.</returns>
    private static Player CompletePlayerExceptVolleybox()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Setter]));
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo.webp"));
        return player;
    }

    /// <summary>Builds a player with every detail.</summary>
    /// <returns>The player.</returns>
    private static Player CompletePlayer()
    {
        var player = CompletePlayerExceptVolleybox();
        player.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/arash-tehrani-p48213"));
        return player;
    }
}
