using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Players;

/// <summary>
/// The images ingest derives from a player: the cut-out photo (I-7) and the player card (I-5). Both are derived output,
/// like the QR code, so storing them raises no event. A new photo drops the old cut-out and announces itself with
/// <see cref="PlayerPhotoChanged"/>, so the cut-out is made again.
/// </summary>
public sealed class PlayerImagesTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The cut-out is stored without an event.</summary>
    [Fact]
    public void The_cutout_is_stored_without_an_event()
    {
        var player = PlayerWithPhoto(out var photoKey);
        player.ClearDomainEvents();

        player.SetPhotoCutout(StorageKey.Create(photoKey.Replace(".webp", "-cutout.webp", StringComparison.Ordinal)));

        player.PhotoCutout!.Value.Should().EndWith("-cutout.webp");
        player.DomainEvents.Should().BeEmpty();
    }

    /// <summary>A new photo drops the cut-out of the old one and raises <see cref="PlayerPhotoChanged"/> next to the details event.</summary>
    [Fact]
    public void A_new_photo_drops_the_old_cutout()
    {
        var player = PlayerWithPhoto(out var photoKey);
        player.SetPhotoCutout(StorageKey.Create(photoKey.Replace(".webp", "-cutout.webp", StringComparison.Ordinal)));
        player.ClearDomainEvents();

        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo-1762000000000.webp"));

        player.PhotoCutout.Should().BeNull();
        player.DomainEvents.Should().ContainEquivalentOf(new PlayerPhotoChanged(player.Id));
        player.DomainEvents.Should().ContainEquivalentOf(new PlayerDetailsChanged(player.Id));
    }

    /// <summary>The same photo again changes nothing and raises nothing.</summary>
    [Fact]
    public void The_same_photo_again_keeps_the_cutout()
    {
        var player = PlayerWithPhoto(out var photoKey);
        player.SetPhotoCutout(StorageKey.Create(photoKey.Replace(".webp", "-cutout.webp", StringComparison.Ordinal)));
        player.ClearDomainEvents();

        player.ChangePhoto(PlayerPhoto.Create(photoKey));

        player.PhotoCutout.Should().NotBeNull();
        player.DomainEvents.Should().BeEmpty();
    }

    /// <summary>The player card is stored without an event.</summary>
    [Fact]
    public void The_card_is_stored_without_an_event()
    {
        var player = PlayerWithPhoto(out _);
        player.ClearDomainEvents();

        player.SetCard(StorageKey.Create($"players/{player.Id}/card-1762000000000.png"));

        player.Card!.Value.Should().EndWith(".png");
        player.DomainEvents.Should().BeEmpty();
    }

    /// <summary>Builds a player with a versioned photo, as the Mini App stores it.</summary>
    /// <param name="photoKey">The photo key.</param>
    /// <returns>The player.</returns>
    private static Player PlayerWithPhoto(out string photoKey)
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        photoKey = $"players/{player.Id}/photo-1761750000000.webp";
        player.ChangePhoto(PlayerPhoto.Create(photoKey));
        return player;
    }
}
