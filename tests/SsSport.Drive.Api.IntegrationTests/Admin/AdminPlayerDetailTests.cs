using System.Net;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;

namespace SsSport.Drive.Api.IntegrationTests.Admin;

/// <summary>
/// The console's player page (A-4, canvas <c>AdminPlayer</c>): the header facts, the Drive folders, and the files tab
/// where an admin reorders files, picks the hero reel, hides or shows a file and retries a failed one. The page's links
/// come from <c>GET /admin/links?playerId=</c> and its "Who has access" tab from <c>GET /admin/viewers?playerId=</c>.
/// </summary>
public sealed class AdminPlayerDetailTests : IAsyncLifetime
{
    /// <summary>The API and its database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the host.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the host.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>The header carries the player's facts, viewers with access, average completion and the Drive folders.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_header_shows_the_players_facts()
    {
        var player = await _host.AddPlayerAsync();
        var video = await _host.AddVideoAsync(player, seconds: 100);
        await _host.AddFolderAsync(player, "SS-SPORT Arash Tehrani", syncedAt: DateTimeOffset.UtcNow.AddMinutes(-4));
        var code = await _host.CreateLinkAsync(player);
        await _host.WatchAsync(code, player, video, await _host.StartViewerSessionAsync(code), (0, 72));

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/players/{player}");

        var root = body.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(player);
        root.GetProperty("name").GetString().Should().Be("Arash Tehrani");
        root.GetProperty("slug").GetString().Should().Be("arash-tehrani");
        root.GetProperty("positions").EnumerateArray().Select(item => item.GetString()).Should().Equal("OPP", "OH");
        root.GetProperty("nationality").GetProperty("code").GetString().Should().Be("IR");
        root.GetProperty("volleyboxId").GetInt32().Should().Be(81001);
        root.GetProperty("status").GetString().Should().Be("Live");
        root.GetProperty("missing").GetArrayLength().Should().Be(0);
        root.GetProperty("viewersWithAccess").GetInt32().Should().Be(1);
        root.GetProperty("averageCompletion").GetDouble().Should().BeApproximately(0.72, 0.001);
        var folder = root.GetProperty("folders").EnumerateArray().Should().ContainSingle().Subject;
        folder.GetProperty("name").GetString().Should().Be("SS-SPORT Arash Tehrani");
        folder.GetProperty("access").GetString().Should().Be("Ok");
        folder.GetProperty("lastSyncedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(-4), TimeSpan.FromMinutes(1));
    }

    /// <summary>An unknown player is a 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_player_is_not_found()
    {
        using var response = await _host.SendAsync(HttpMethod.Get, $"/admin/players/{Guid.NewGuid()}", telegramId: BackendTestHost.AdminTelegramId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The files tab lists every file in page order, with its state, failure reason, source and hero flag.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Files_are_listed_in_page_order_with_their_state()
    {
        var player = await _host.AddPlayerAsync();
        var failed = await _host.AddFileAsync(player, "Full_Match_02.mov", ProcessingState.Failed, 2);
        var stats = await _host.AddFileAsync(player, "Stats_Oct_2026.pdf", ProcessingState.Ready, 1);

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/players/{player}/files?page=1&pageSize=20");

        var files = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        files.Select(file => file.GetProperty("id").GetGuid()).Should().Equal(stats, failed);
        files[0].GetProperty("kind").GetString().Should().Be("Document");
        files[0].GetProperty("state").GetString().Should().Be("Ready");
        files[0].GetProperty("pageCount").GetInt32().Should().Be(3);
        files[0].GetProperty("source").GetString().Should().Be("Drive");
        files[0].GetProperty("visibility").GetString().Should().Be("Visible");
        files[0].GetProperty("isHero").GetBoolean().Should().BeFalse();
        files[0].GetProperty("removedFromSource").GetBoolean().Should().BeFalse();
        files[1].GetProperty("state").GetString().Should().Be("Failed");
        files[1].GetProperty("failureReason").GetString().Should().Be("ffmpeg exited with code 1");
    }

    /// <summary>Moving a file to a position shifts the others; the order is 1-based and stored, not just shown.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Moving_a_file_reorders_the_page()
    {
        var player = await _host.AddPlayerAsync();
        var a = await _host.AddFileAsync(player, "A.pdf", ProcessingState.Ready, 1);
        var b = await _host.AddFileAsync(player, "B.pdf", ProcessingState.Ready, 2);
        var c = await _host.AddFileAsync(player, "C.pdf", ProcessingState.Ready, 3);

        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/players/{player}/files/{c}/move", new { position = 1 }, HttpStatusCode.NoContent))
        {
        }

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/players/{player}/files");
        var files = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        files.Select(file => file.GetProperty("id").GetGuid()).Should().Equal(c, a, b);
        files.Select(file => file.GetProperty("sortOrder").GetInt32()).Should().Equal(1, 2, 3);
    }

    /// <summary>A position outside 1..file count is a 400; another player's file is a 404.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Moving_checks_the_position_and_the_owner()
    {
        var player = await _host.AddPlayerAsync();
        var other = await _host.AddPlayerAsync();
        var file = await _host.AddFileAsync(player, "A.pdf", ProcessingState.Ready, 1);
        var foreign = await _host.AddFileAsync(other, "B.pdf", ProcessingState.Ready, 1);

        using var zero = await _host.SendAsync(HttpMethod.Post, $"/admin/players/{player}/files/{file}/move", new { position = 0 }, telegramId: BackendTestHost.AdminTelegramId);
        zero.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var beyond = await _host.SendAsync(HttpMethod.Post, $"/admin/players/{player}/files/{file}/move", new { position = 2 }, telegramId: BackendTestHost.AdminTelegramId);
        beyond.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var notMine = await _host.SendAsync(HttpMethod.Post, $"/admin/players/{player}/files/{foreign}/move", new { position = 1 }, telegramId: BackendTestHost.AdminTelegramId);
        notMine.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>An admin picks the hero reel and hides a file; both show in the list and are audited.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Hero_and_visibility_are_set_from_the_files_tab()
    {
        var player = await _host.AddPlayerAsync();
        var hero = await _host.AddVideoAsync(player, seconds: 84);
        var old = await _host.AddVideoAsync(player, seconds: 58);

        using (await _host.AdminJsonAsync(HttpMethod.Patch, $"/admin/players/{player}/files/{hero}", new { isHero = true }))
        {
        }

        using (await _host.AdminJsonAsync(HttpMethod.Patch, $"/admin/players/{player}/files/{old}", new { hidden = true }))
        {
        }

        using var body = await _host.AdminJsonAsync(HttpMethod.Get, $"/admin/players/{player}/files");
        var files = body.RootElement.GetProperty("items").EnumerateArray().ToDictionary(file => file.GetProperty("id").GetGuid());
        files[hero].GetProperty("isHero").GetBoolean().Should().BeTrue();
        files[old].GetProperty("visibility").GetString().Should().Be("Hidden");
        var actions = await _host.QueryAsync(db => Task.FromResult(db.Set<AuditEntry>().Select(entry => entry.Action).ToList()));
        actions.Should().Contain("asset.hidden");
    }

    /// <summary>Retrying a failed file puts it back on the queue; retrying any other file is a 422.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_failed_file_can_be_retried()
    {
        var player = await _host.AddPlayerAsync();
        var failed = await _host.AddFileAsync(player, "Full_Match_02.mov", ProcessingState.Failed, 1);
        var ready = await _host.AddFileAsync(player, "Stats.pdf", ProcessingState.Ready, 2);

        using (await _host.AdminJsonAsync(HttpMethod.Post, $"/admin/files/{failed}/retry", new { }, HttpStatusCode.Accepted))
        {
        }

        var asset = await _host.QueryAsync(db => Task.FromResult(db.Set<Asset>().Single(item => item.Id == failed)));
        asset.State.Should().Be(ProcessingState.Queued);
        asset.FailureReason.Should().BeNull();

        using var notFailed = await _host.SendAsync(HttpMethod.Post, $"/admin/files/{ready}/retry", new { }, telegramId: BackendTestHost.AdminTelegramId);
        notFailed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BackendTestHost.ProblemCodeAsync(notFailed)).Should().Be("asset.invalid_state");

        using var unknown = await _host.SendAsync(HttpMethod.Post, $"/admin/files/{Guid.NewGuid()}/retry", new { }, telegramId: BackendTestHost.AdminTelegramId);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Only admins use the player page; analysts keep the Mini App.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_admins_use_the_player_page()
    {
        var player = await _host.AddPlayerAsync();
        var file = await _host.AddFileAsync(player, "A.pdf", ProcessingState.Failed, 1);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, $"/admin/players/{player}"),
            (HttpMethod.Get, $"/admin/players/{player}/files"),
            (HttpMethod.Post, $"/admin/players/{player}/files/{file}/move"),
            (HttpMethod.Patch, $"/admin/players/{player}/files/{file}"),
            (HttpMethod.Post, $"/admin/files/{file}/retry"),
        })
        {
            using var analyst = await _host.SendAsync(method, path, new { position = 1 }, telegramId: BackendTestHost.AnalystTelegramId);
            analyst.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }
}
