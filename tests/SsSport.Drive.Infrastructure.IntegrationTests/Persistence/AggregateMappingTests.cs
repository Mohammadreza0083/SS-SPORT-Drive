using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Every aggregate survives a save and a reload in a fresh context with all its values intact, unique indexes reject
/// duplicates, and concurrent edits are detected. Values are unique per test, so tests can share one database.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class AggregateMappingTests(AppDbContextFixture fixture) : IClassFixture<AppDbContextFixture>
{
    /// <summary>A fixed time, in UTC as PostgreSQL timestamptz requires.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A valid md5.</summary>
    private const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>A fully filled-in, published player round-trips.</summary>
    [Fact]
    public async Task Player_round_trips()
    {
        var player = CompletePlayer("Arash Tehrani", 48213);
        var hero = Guid.CreateVersion7(_now);
        var analyst = Guid.CreateVersion7(_now);
        player.Publish(_now.AddHours(1));
        player.SetHero(hero);
        player.AssignAnalyst(analyst);
        player.SetQrCode(StorageKey.Create($"qr/{player.Id}.png"));

        await SaveAsync(player);
        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<Player>().SingleAsync(p => p.Id == player.Id, TestContext.Current.CancellationToken);

        loaded.Category.Should().Be(PlayerCategory.Men);
        loaded.Name!.Value.Should().Be("Arash Tehrani");
        loaded.Slug!.Value.Should().Be("arash-tehrani");
        loaded.Nationality!.Code.Should().Be("IR");
        loaded.Positions!.Items.Should().Equal(Position.OutsideHitter, Position.Opposite);
        loaded.Volleybox!.PlayerId.Should().Be(48213);
        loaded.Volleybox.Url.Should().Be("https://volleybox.net/arash-tehrani-p48213");
        loaded.Photo!.StorageKey.Should().Be("players/p1/photo.webp");
        loaded.Status.Should().Be(PlayerStatus.Published);
        loaded.PublishedAt.Should().Be(_now.AddHours(1));
        loaded.HeroAssetId.Should().Be(hero);
        loaded.AnalystId.Should().Be(analyst);
        loaded.QrCode!.Value.Should().Be($"qr/{player.Id}.png");
        loaded.CreatedAt.Should().Be(_now);
        loaded.DomainEvents.Should().BeEmpty();
    }

    /// <summary>A waiting player with only its category round-trips with every optional value empty.</summary>
    [Fact]
    public async Task Incomplete_player_round_trips()
    {
        var player = Player.Create(PlayerCategory.Women, _now);

        await SaveAsync(player);
        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<Player>().SingleAsync(p => p.Id == player.Id, TestContext.Current.CancellationToken);

        loaded.Status.Should().Be(PlayerStatus.Waiting);
        loaded.Name.Should().BeNull();
        loaded.Volleybox.Should().BeNull();
        loaded.Positions.Should().BeNull();
        loaded.MissingDetails().Should().HaveCount(5);
    }

    /// <summary>Two players cannot share a slug or a Volleybox id.</summary>
    [Fact]
    public async Task Slug_and_volleybox_id_are_unique()
    {
        await SaveAsync(CompletePlayer("Kirill Vlasov", 555001));

        var sameSlug = () => SaveAsync(CompletePlayer("Kirill Vlasov", 555002));
        var sameVolleybox = () => SaveAsync(CompletePlayer("Kirill Vlasov Two", 555001));

        await sameSlug.Should().ThrowAsync<DbUpdateException>();
        await sameVolleybox.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>A Drive folder with lost access round-trips; Drive folder ids are unique.</summary>
    [Fact]
    public async Task Drive_folder_round_trips_and_is_unique()
    {
        var playerId = Guid.CreateVersion7(_now);
        var folder = DriveFolder.Link(playerId, "drive-folder-rt", "SS-SPORT Ali Hoshmand／OH", _now);
        folder.MarkSynced(_now.AddMinutes(5));
        folder.MarkAccessLost(_now.AddMinutes(15));

        await SaveAsync(folder);
        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<DriveFolder>().SingleAsync(f => f.Id == folder.Id, TestContext.Current.CancellationToken);

        loaded.PlayerId.Should().Be(playerId);
        loaded.Name.Should().Be("SS-SPORT Ali Hoshmand／OH");
        loaded.LastSyncedAt.Should().Be(_now.AddMinutes(5));
        loaded.Access.Should().Be(FolderAccess.Lost);
        loaded.AccessLostAt.Should().Be(_now.AddMinutes(15));

        var duplicate = () => SaveAsync(DriveFolder.Link(playerId, "drive-folder-rt", "copy", _now));
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>A ready video with its renditions round-trips, and later changes to the renditions are saved.</summary>
    [Fact]
    public async Task Asset_with_renditions_round_trips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var asset = Asset.Add(Guid.CreateVersion7(_now), null, AssetSource.Archive, "drive-file-rt", AssetFile.Create("game.MP4", Md5, 123456789), "LEMESOS VS AEL", AssetTags.FullMatch | AssetTags.Highlight, 2, _now);
        asset.StartCopying();
        asset.AttachOriginal(StorageKey.Create("Men Volleyball/SS-SPORT Ali Hoshmand／OH/Full matches/game.MP4"));
        asset.RecordVideoInfo(720, 5400.5);
        asset.MarkRenditionReady(RenditionQuality.P360, StorageKey.Create($"hls/{asset.Id}/360/index.m3u8"));
        await SaveAsync(asset);

        await using (var editor = fixture.CreateContext())
        {
            var tracked = await editor.Set<Asset>().Include(a => a.Renditions).SingleAsync(a => a.Id == asset.Id, cancellationToken);
            tracked.MarkRenditionReady(RenditionQuality.P720, StorageKey.Create($"hls/{asset.Id}/720/index.m3u8"));
            tracked.MarkRemovedFromSource(_now.AddDays(1));
            await editor.SaveChangesAsync(cancellationToken);
        }

        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<Asset>().Include(a => a.Renditions).SingleAsync(a => a.Id == asset.Id, cancellationToken);

        loaded.Kind.Should().Be(AssetKind.Video);
        loaded.File.OriginalName.Should().Be("game.MP4");
        loaded.File.Md5.Should().Be(Md5);
        loaded.File.SizeBytes.Should().Be(123456789);
        loaded.Source.Should().Be(AssetSource.Archive);
        loaded.Title.Should().Be("LEMESOS VS AEL");
        loaded.Tags.Should().Be(AssetTags.FullMatch | AssetTags.Highlight);
        loaded.SortOrder.Should().Be(2);
        loaded.OriginalKey!.Value.Should().StartWith("Men Volleyball/");
        loaded.DurationSeconds.Should().Be(5400.5);
        loaded.State.Should().Be(ProcessingState.Ready);
        loaded.Visibility.Should().Be(AssetVisibility.Hidden);
        loaded.RemovedFromSourceAt.Should().Be(_now.AddDays(1));
        loaded.Renditions.Select(r => (r.Quality, r.Status)).Should().Equal(
            (RenditionQuality.P360, RenditionStatus.Ready),
            (RenditionQuality.P720, RenditionStatus.Ready));
    }

    /// <summary>Replacing an asset's content deletes its old renditions.</summary>
    [Fact]
    public async Task Replacing_content_removes_old_renditions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var asset = Asset.Add(Guid.CreateVersion7(_now), null, AssetSource.Drive, "drive-file-replace", AssetFile.Create("a.mp4", Md5, 10), "A", AssetTags.Untagged, 0, _now);
        asset.StartCopying();
        asset.AttachOriginal(StorageKey.Create("originals/a.mp4"));
        asset.RecordVideoInfo(1080, 60);
        await SaveAsync(asset);

        await using (var editor = fixture.CreateContext())
        {
            var tracked = await editor.Set<Asset>().Include(a => a.Renditions).SingleAsync(a => a.Id == asset.Id, cancellationToken);
            tracked.ReplaceContent(AssetFile.Create("a.mp4", "e4d909c290d0fb1ca068ffaddf22cbd0", 20));
            await editor.SaveChangesAsync(cancellationToken);
        }

        await using var reader = fixture.CreateContext();
        var renditions = await reader.Set<Rendition>().CountAsync(r => EF.Property<Guid>(r, "AssetId") == asset.Id, cancellationToken);
        renditions.Should().Be(0);
    }

    /// <summary>A user round-trips with role and email; Telegram ids are unique.</summary>
    [Fact]
    public async Task User_round_trips_and_telegram_id_is_unique()
    {
        var admin = User.CreateFirstAdmin(777001, "Owner", EmailAddress.Create("owner@ss-sport.co"), _now);
        admin.RecordActivity(_now.AddHours(2));

        await SaveAsync(admin);
        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<User>().SingleAsync(u => u.Id == admin.Id, TestContext.Current.CancellationToken);

        loaded.TelegramId.Should().Be(777001);
        loaded.Role.Should().Be(Role.Admin);
        loaded.Status.Should().Be(UserStatus.Active);
        loaded.Email!.Value.Should().Be("owner@ss-sport.co");
        loaded.LastActiveAt.Should().Be(_now.AddHours(2));

        var duplicate = () => SaveAsync(User.RequestAccess(777001, null, "Copy", _now));
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>An audit entry round-trips with its JSON data unchanged.</summary>
    [Fact]
    public async Task Audit_entry_round_trips()
    {
        var entry = AuditEntry.Record(Guid.CreateVersion7(_now), "user.role_changed", "User", "u-1", """{"from":"Analyst","to":"Admin"}""", _now);

        await SaveAsync(entry);
        await using var reader = fixture.CreateContext();
        var loaded = await reader.Set<AuditEntry>().SingleAsync(e => e.Id == entry.Id, TestContext.Current.CancellationToken);

        loaded.Action.Should().Be("user.role_changed");
        loaded.TargetType.Should().Be("User");
        loaded.TargetId.Should().Be("u-1");
        loaded.Data.Should().Be("""{"from":"Analyst","to":"Admin"}""");
        loaded.At.Should().Be(_now);
    }

    /// <summary>When two editors change the same player, the second save fails instead of overwriting the first.</summary>
    [Fact]
    public async Task Concurrent_edits_are_detected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var player = Player.Create(PlayerCategory.Men, _now);
        await SaveAsync(player);

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var a = await first.Set<Player>().SingleAsync(p => p.Id == player.Id, cancellationToken);
        var b = await second.Set<Player>().SingleAsync(p => p.Id == player.Id, cancellationToken);

        a.ChangeNationality(Nationality.Create("IR"));
        await first.SaveChangesAsync(cancellationToken);
        b.ChangeNationality(Nationality.Create("RU"));
        var save = () => second.SaveChangesAsync(cancellationToken);

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    /// <summary>Builds a complete player.</summary>
    /// <param name="name">The name.</param>
    /// <param name="volleyboxId">The Volleybox id.</param>
    /// <returns>The player.</returns>
    private static Player CompletePlayer(string name, int volleyboxId)
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var fullName = FullName.Create(name);
        player.Rename(fullName, Slug.FromName(fullName));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter, Position.Opposite]));
        player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/{fullName.Value.ToLowerInvariant().Replace(' ', '-')}-p{volleyboxId}"));
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo.webp"));
        return player;
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
