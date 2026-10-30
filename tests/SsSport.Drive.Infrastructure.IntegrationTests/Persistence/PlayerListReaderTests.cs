using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The player list read model (T-7): one SQL query reads a page of players with their file and folder state, filtered
/// by analyst, search and status, newest first. Each test owns its analyst, so tests sharing the database never see
/// each other's players.
/// </summary>
/// <param name="database">The migrated PostgreSQL database.</param>
public sealed class PlayerListReaderTests(AppDbContextFixture database) : IClassFixture<AppDbContextFixture>
{
    /// <summary>The analyst who owns this test's players.</summary>
    private readonly Guid _analyst = Guid.CreateVersion7();

    /// <summary>A Latin-letter token unique to this test, used in player names so searches only see this test's players.</summary>
    private readonly string _token = string.Concat(Enumerable.Range(0, 8).Select(_ => (char)('a' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(26))));

    /// <summary>The time the players are created at; each player is a minute newer than the last.</summary>
    private DateTimeOffset _now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Volleybox ids handed out to complete players.</summary>
    private static int _volleybox = 700_000;

    /// <summary>An analyst sees only their own players, newest first, with the counts of each.</summary>
    [Fact]
    public async Task Lists_the_analysts_players_newest_first()
    {
        var older = await SeedPlayerAsync("Older");
        var newer = await SeedPlayerAsync("Newer");
        await SeedPlayerAsync("Someone Else", analystId: Guid.CreateVersion7());

        var page = await ReadAsync(new PlayerListCriteria(_analyst, null, PlayerListFilter.All));

        page.TotalCount.Should().Be(2);
        page.Items.Select(item => item.Id).Should().Equal(newer, older);
        var item = page.Items[0];
        item.Name.Should().Be($"Newer {Title(_token)}");
        item.Category.Should().Be("Men");
        item.NationalityCode.Should().Be("IR");
        item.NationalityName.Should().Be("Iran");
        item.Positions.Should().Equal("OH", "OPP");
        item.HasPhoto.Should().BeTrue();
        item.Status.Should().Be("Waiting");
        item.IsComplete.Should().BeTrue();
        item.FileCount.Should().Be(0);
        item.NeedsAttention.Should().BeFalse();
    }

    /// <summary>Without an analyst (an admin) every player is listed.</summary>
    [Fact]
    public async Task Without_an_analyst_every_player_is_listed()
    {
        await SeedPlayerAsync("Mine");
        await SeedPlayerAsync("Theirs", analystId: Guid.CreateVersion7());
        await SeedPlayerAsync("Archive", analystId: null);

        var page = await ReadAsync(new PlayerListCriteria(null, _token, PlayerListFilter.All));

        page.TotalCount.Should().Be(3);
    }

    /// <summary>The search matches part of the name in any case, or the exact Volleybox id.</summary>
    [Fact]
    public async Task Search_matches_name_or_volleybox_id()
    {
        var arash = await SeedPlayerAsync("Arash");
        var kian = await SeedPlayerAsync("Kian");
        var volleyboxId = _volleybox;

        var byName = await ReadAsync(new PlayerListCriteria(_analyst, $"ARASH {_token[..3]}", PlayerListFilter.All));
        var byId = await ReadAsync(new PlayerListCriteria(_analyst, volleyboxId.ToString(System.Globalization.CultureInfo.InvariantCulture), PlayerListFilter.All));
        var wildcard = await ReadAsync(new PlayerListCriteria(_analyst, "%", PlayerListFilter.All));

        byName.Items.Select(item => item.Id).Should().Equal(arash);
        byId.Items.Select(item => item.Id).Should().Equal(kian);
        wildcard.Items.Should().BeEmpty("% is searched as a character, not a wildcard");
    }

    /// <summary>Live is a published player.</summary>
    [Fact]
    public async Task Live_filter_shows_published_players()
    {
        var published = await SeedPlayerAsync("Published", publish: true);
        await SeedPlayerAsync("Waiting");

        var page = await ReadAsync(new PlayerListCriteria(_analyst, null, PlayerListFilter.Live));

        page.Items.Select(item => item.Id).Should().Equal(published);
        page.Items[0].Status.Should().Be("Published");
    }

    /// <summary>Processing is a player with a file queued, copying or processing; the counts say how many.</summary>
    [Fact]
    public async Task Processing_filter_shows_players_with_files_in_progress()
    {
        var busy = await SeedPlayerAsync("Busy");
        await SeedAssetAsync(busy, "queued.mp4");
        await SeedAssetAsync(busy, "copying.mp4", asset => asset.StartCopying());
        await SeedAssetAsync(busy, "ready.pdf", asset => MakeReady(asset, 3));
        var done = await SeedPlayerAsync("Done");
        await SeedAssetAsync(done, "report.pdf", asset => MakeReady(asset, 1));

        var page = await ReadAsync(new PlayerListCriteria(_analyst, null, PlayerListFilter.Processing));

        page.Items.Select(item => item.Id).Should().Equal(busy);
        page.Items[0].FileCount.Should().Be(3);
        page.Items[0].ProcessingCount.Should().Be(2);
        page.Items[0].FailedCount.Should().Be(0);
    }

    /// <summary>Needs attention is a player with missing details, a failed file or a folder that lost access.</summary>
    [Fact]
    public async Task Needs_attention_filter_shows_players_to_fix()
    {
        var incomplete = await SeedPlayerAsync("Incomplete", complete: false);
        var failed = await SeedPlayerAsync("Failed");
        await SeedAssetAsync(failed, "broken.mp4", asset => asset.Fail("ffmpeg exited with code 1"));
        var lost = await SeedPlayerAsync("Lost");
        await SeedFolderAsync(lost, accessLost: true);
        var fine = await SeedPlayerAsync("Fine");
        await SeedFolderAsync(fine, accessLost: false);

        var page = await ReadAsync(new PlayerListCriteria(_analyst, null, PlayerListFilter.NeedsAttention));

        page.Items.Select(item => item.Id).Should().BeEquivalentTo([incomplete, failed, lost]);
        page.Items.Should().AllSatisfy(item => item.NeedsAttention.Should().BeTrue());
        page.Items.Single(item => item.Id == incomplete).IsComplete.Should().BeFalse();
        page.Items.Single(item => item.Id == failed).FailedCount.Should().Be(1);
        page.Items.Single(item => item.Id == lost).HasLostFolder.Should().BeTrue();
    }

    /// <summary>Pages are cut in SQL, in order, with the total on every page.</summary>
    [Fact]
    public async Task Pages_are_stable()
    {
        var ids = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            ids.Add(await SeedPlayerAsync($"Player {(char)('A' + index)}"));
        }

        var first = await ReadAsync(new PlayerListCriteria(_analyst, null, PlayerListFilter.All), new PageRequest(1, 2));
        var second = await ReadAsync(new PlayerListCriteria(_analyst, null, PlayerListFilter.All), new PageRequest(2, 2));
        var third = await ReadAsync(new PlayerListCriteria(_analyst, null, PlayerListFilter.All), new PageRequest(3, 2));

        first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Id).Should().Equal(Enumerable.Reverse(ids));
        new[] { first, second, third }.Should().AllSatisfy(page =>
        {
            page.TotalCount.Should().Be(5);
            page.TotalPages.Should().Be(3);
        });
    }

    /// <summary>Takes a document through processing, as ingest will.</summary>
    /// <param name="asset">The queued document.</param>
    /// <param name="pages">Its page count.</param>
    private static void MakeReady(Asset asset, int pages)
    {
        asset.AttachOriginal(StorageKey.Create($"originals/{asset.Id}"));
        asset.CompleteDocument(pages);
    }

    /// <summary>Title-cases the token the way player names are stored.</summary>
    /// <param name="token">The lower-case token.</param>
    /// <returns>The token with its first letter in upper case.</returns>
    private static string Title(string token) => char.ToUpperInvariant(token[0]) + token[1..];

    /// <summary>Reads one page with the reader under test.</summary>
    /// <param name="criteria">The criteria.</param>
    /// <param name="page">The page, or the first page of 20.</param>
    /// <returns>The page.</returns>
    private async Task<PagedResult<PlayerListItemDto>> ReadAsync(PlayerListCriteria criteria, PageRequest? page = null)
    {
        await using var context = database.CreateContext();
        return await new PlayerListReader(context).PageAsync(criteria, page ?? new PageRequest(), TestContext.Current.CancellationToken);
    }

    /// <summary>Stores a player named "<paramref name="label"/> <c>token</c>".</summary>
    /// <param name="label">The first part of the name.</param>
    /// <param name="complete">Whether every detail is set.</param>
    /// <param name="publish">Whether the player is published.</param>
    /// <param name="analystId">The owner; this test's analyst unless given.</param>
    /// <returns>The player id.</returns>
    private async Task<Guid> SeedPlayerAsync(string label, bool complete = true, bool publish = false, Guid? analystId = default)
    {
        _now = _now.AddMinutes(1);
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create($"{label} {_token}");
        player.Rename(name, Slug.Create($"{Slug.FromName(name).Value}-{Guid.NewGuid():N}"));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter, Position.Opposite]));
        if (complete)
        {
            player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/player-p{Interlocked.Increment(ref _volleybox)}"));
            player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo-1.webp"));
        }

        if ((analystId == default ? _analyst : analystId) is { } owner)
        {
            player.AssignAnalyst(owner);
        }

        if (publish)
        {
            player.Publish(_now);
        }

        await SaveAsync(player);
        return player.Id;
    }

    /// <summary>Stores a file of a player.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="change">Moves the file to another state, if given.</param>
    /// <returns>A task that completes when it is saved.</returns>
    private async Task SeedAssetAsync(Guid playerId, string fileName, Action<Asset>? change = null)
    {
        var asset = Asset.Add(playerId, null, AssetSource.Drive, null, AssetFile.Create(fileName, Guid.NewGuid().ToString("N"), 1024), fileName, AssetTags.Untagged, 0, _now);
        change?.Invoke(asset);
        await SaveAsync(asset);
    }

    /// <summary>Stores a Drive folder of a player.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="accessLost">Whether the folder lost access.</param>
    /// <returns>A task that completes when it is saved.</returns>
    private async Task SeedFolderAsync(Guid playerId, bool accessLost)
    {
        var folder = DriveFolder.Link(playerId, $"folder_{Guid.NewGuid():N}", "SS-SPORT Folder", _now);
        if (accessLost)
        {
            folder.MarkAccessLost(_now);
        }

        await SaveAsync(folder);
    }

    /// <summary>Saves an entity in its own context.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>A task that completes when it is saved.</returns>
    private async Task SaveAsync(object entity)
    {
        await using var context = database.CreateContext();
        context.Add(entity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
