using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The columns ingest adds survive a save and a reload: an asset's poster, thumbnail and sprite keys (I-4), and a
/// player's cut-out photo (I-7) and card (I-5). A new migration adds them; the snapshot stays in step.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class IngestMappingTests(AppDbContextFixture fixture) : IClassFixture<AppDbContextFixture>
{
    /// <summary>A fixed time, in UTC as PostgreSQL timestamptz requires.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A valid md5.</summary>
    private const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>A processed video's preview keys round-trip.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Video_preview_keys_round_trip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var playerId = Guid.CreateVersion7(_now);
        var asset = Asset.Add(playerId, null, AssetSource.Drive, "drive-ingest-1", AssetFile.Create("game.mp4", Md5, 1000), "Game", AssetTags.FullMatch, 0, _now);
        asset.AttachOriginal(StorageKey.Create($"originals/{Md5}.mp4"));
        var root = $"players/{playerId}/videos/{asset.Id}/v1";
        asset.AttachVideoPreviews(StorageKey.Create($"{root}/poster.webp"), StorageKey.Create($"{root}/thumb.webp"), StorageKey.Create($"{root}/sprite.webp"));
        await SaveAsync(asset);

        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<Asset>().SingleAsync(a => a.Id == asset.Id, cancellationToken);

        loaded.PosterKey!.Value.Should().Be($"{root}/poster.webp");
        loaded.ThumbnailKey!.Value.Should().Be($"{root}/thumb.webp");
        loaded.SpriteKey!.Value.Should().Be($"{root}/sprite.webp");
    }

    /// <summary>A player's cut-out and card keys round-trip.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Player_cutout_and_card_round_trip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var player = Player.Create(PlayerCategory.Women, _now);
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo-1761750000000.webp"));
        player.SetPhotoCutout(StorageKey.Create($"players/{player.Id}/photo-1761750000000-cutout.webp"));
        player.SetCard(StorageKey.Create($"players/{player.Id}/card-1762000000000.png"));
        await SaveAsync(player);

        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<Player>().SingleAsync(p => p.Id == player.Id, cancellationToken);

        loaded.PhotoCutout!.Value.Should().EndWith("-cutout.webp");
        loaded.Card!.Value.Should().EndWith(".png");
    }

    /// <summary>Adds an aggregate and saves it in its own context.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="aggregate">The aggregate.</param>
    /// <returns>A task that completes when it is saved.</returns>
    private async Task SaveAsync<T>(T aggregate)
        where T : class
    {
        await using AppDbContext context = fixture.CreateContext();
        context.Set<T>().Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
