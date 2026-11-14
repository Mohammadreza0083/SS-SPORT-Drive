using System.Net;
using System.Text.Json;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Api.IntegrationTests.Support;

/// <summary>
/// Data helpers for the admin console tests (A-2 to A-11): waiting players with chosen gaps, files in any state, Drive
/// folders, team members and watching through the real tracking endpoints. Every helper uses only types that exist
/// before the console is built, so the console tests fail on behaviour, not on missing helpers.
/// </summary>
public sealed partial class BackendTestHost
{
    /// <summary>The header the viewer endpoints read the viewer session from.</summary>
    public const string ViewerSessionHeader = "X-Viewer-Session";

    /// <summary>Counts created waiting players so each gets its own folder.</summary>
    private int _waiting;

    /// <summary>Counts created team members so each gets its own Telegram id.</summary>
    private long _members = 900;

    /// <summary>Stores a waiting (unpublished) player that lacks the given details.</summary>
    /// <param name="name">The full name, or <see langword="null"/> to leave it missing.</param>
    /// <param name="missing">The details to leave out (the name is controlled by <paramref name="name"/>).</param>
    /// <param name="analystId">The responsible analyst, or <see langword="null"/>.</param>
    /// <param name="createdAt">When the player was created; now when left out.</param>
    /// <returns>The player id.</returns>
    public async Task<Guid> AddWaitingPlayerAsync(string? name, PlayerDetail[]? missing = null, Guid? analystId = null, DateTimeOffset? createdAt = null)
    {
        var n = Interlocked.Increment(ref _waiting);
        var gaps = missing ?? [];
        var player = Player.Create(PlayerCategory.Men, createdAt ?? DateTimeOffset.UtcNow);
        if (name is not null)
        {
            var full = FullName.Create(name);
            player.Rename(full, Slug.FromName(full));
        }

        if (!gaps.Contains(PlayerDetail.Nationality))
        {
            player.ChangeNationality(Nationality.Create("IR"));
        }

        if (!gaps.Contains(PlayerDetail.Positions))
        {
            player.ChangePositions(PlayerPositions.Create([Position.MiddleBlocker]));
        }

        if (!gaps.Contains(PlayerDetail.Volleybox))
        {
            player.LinkVolleybox(VolleyboxProfile.Create($"https://volleybox.net/waiting-player-p{70000 + n}"));
        }

        if (!gaps.Contains(PlayerDetail.Photo))
        {
            player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        }

        if (analystId is { } analyst)
        {
            player.AssignAnalyst(analyst);
        }

        await SaveAsync(player);
        return player.Id;
    }

    /// <summary>Links a Drive folder to a player.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="name">The folder name.</param>
    /// <param name="syncedAt">The last sync, or <see langword="null"/> for never.</param>
    /// <param name="accessLost">Whether the service account lost access.</param>
    /// <returns>The folder id.</returns>
    public async Task<Guid> AddFolderAsync(Guid player, string name, DateTimeOffset? syncedAt = null, bool accessLost = false)
    {
        var folder = DriveFolder.Link(player, $"drive-folder-{Guid.NewGuid():N}", name, DateTimeOffset.UtcNow.AddDays(-3));
        if (syncedAt is { } at)
        {
            folder.MarkSynced(at);
        }

        if (accessLost)
        {
            folder.MarkAccessLost(DateTimeOffset.UtcNow);
        }

        folder.ClearDomainEvents();
        await SaveAsync(folder);
        return folder.Id;
    }

    /// <summary>Stores a file for a player in the given processing state.</summary>
    /// <param name="player">The player id.</param>
    /// <param name="fileName">The original file name; its extension picks the kind.</param>
    /// <param name="state">Queued, Copying, Processing, Ready (documents only) or Failed.</param>
    /// <param name="sortOrder">The page order.</param>
    /// <returns>The asset id.</returns>
    public async Task<Guid> AddFileAsync(Guid player, string fileName, ProcessingState state, int sortOrder)
    {
        var n = Interlocked.Increment(ref _assets);
        var asset = Asset.Add(
            player,
            null,
            AssetSource.Drive,
            $"drive-{n}",
            AssetFile.Create(fileName, n.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), 2048 + n),
            Path.GetFileNameWithoutExtension(fileName),
            AssetTags.Highlight,
            sortOrder,
            DateTimeOffset.UtcNow.AddMinutes(-n));
        if (state is ProcessingState.Copying or ProcessingState.Processing or ProcessingState.Ready)
        {
            asset.StartCopying();
        }

        if (state is ProcessingState.Processing or ProcessingState.Ready)
        {
            asset.AttachOriginal(StorageKey.Create($"assets/{asset.Id}/original{Path.GetExtension(fileName)}"));
        }

        if (state == ProcessingState.Ready)
        {
            asset.CompleteDocument(3);
        }

        if (state == ProcessingState.Failed)
        {
            asset.Fail("ffmpeg exited with code 1");
        }

        asset.ClearDomainEvents();
        await SaveAsync(asset);
        return asset.Id;
    }

    /// <summary>Stores an active team member.</summary>
    /// <param name="displayName">The name.</param>
    /// <param name="role">The role.</param>
    /// <param name="email">The email (admins need one), or <see langword="null"/>.</param>
    /// <returns>The user id.</returns>
    public async Task<Guid> AddMemberAsync(string displayName, Role role, string? email = null)
    {
        var id = Interlocked.Increment(ref _members);
        var user = User.RequestAccess(id, displayName.ToLowerInvariant().Replace(' ', '_'), displayName, DateTimeOffset.UtcNow.AddDays(-1));
        if (email is not null)
        {
            user.ChangeEmail(EmailAddress.Create(email));
        }

        user.Approve(role);
        user.ClearDomainEvents();
        await SaveAsync(user);
        return user.Id;
    }

    /// <summary>Stores a user who asked for access through the bot and waits for a decision.</summary>
    /// <param name="displayName">The name.</param>
    /// <returns>The user id.</returns>
    public async Task<Guid> AddPendingMemberAsync(string displayName)
    {
        var id = Interlocked.Increment(ref _members);
        var user = User.RequestAccess(id, displayName.ToLowerInvariant().Replace(' ', '_'), displayName, DateTimeOffset.UtcNow.AddHours(-1));
        user.ClearDomainEvents();
        await SaveAsync(user);
        return user.Id;
    }

    /// <summary>Watches a video through the real tracking endpoints: one play with the given heartbeats.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="player">The player id.</param>
    /// <param name="video">The video id.</param>
    /// <param name="session">The viewer session token.</param>
    /// <param name="ranges">The watched ranges, in seconds.</param>
    /// <returns>The watch id.</returns>
    public async Task<Guid> WatchAsync(string code, Guid player, Guid video, string session, params (double From, double To)[] ranges)
    {
        var headers = new Dictionary<string, string> { [ViewerSessionHeader] = session };
        Guid watchId;
        using (var start = await SendAsync(HttpMethod.Post, $"/links/{code}/players/{player}/videos/{video}/watch", new { }, headers: headers))
        {
            start.StatusCode.Should().Be(HttpStatusCode.Created);
            using var body = await ReadJsonAsync(start);
            watchId = body.RootElement.GetProperty("watchId").GetGuid();
        }

        // A heartbeat covers at most 30 seconds (WatchSession.MaxHeartbeatSeconds); longer ranges go as back-to-back beats,
        // which the session merges into one range.
        foreach (var (from, to) in ranges)
        {
            for (var start = from; start < to; start += 20)
            {
                var end = Math.Min(start + 20, to);
                using var beat = await SendAsync(HttpMethod.Post, $"/links/{code}/watch/{watchId}/heartbeat", new { from = start, to = end }, headers: headers);
                beat.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }
        }

        return watchId;
    }

    /// <summary>Sends an admin request (signed as the admin) and parses the JSON answer.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <param name="expected">The status the call must answer.</param>
    /// <returns>The parsed body; an empty object for an empty answer.</returns>
    public async Task<JsonDocument> AdminJsonAsync(HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(method, path, body, telegramId: AdminTelegramId);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }
}
