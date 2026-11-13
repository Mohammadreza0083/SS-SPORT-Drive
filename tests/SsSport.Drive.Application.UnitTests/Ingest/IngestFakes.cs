using System.Collections.Concurrent;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Ingest;

/// <summary>An in-memory media bucket that records what was written, for the ingest handler tests.</summary>
internal sealed class FakeObjectStorage : IObjectStorage
{
    /// <summary>The stored objects by key.</summary>
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);

    /// <summary>Gets the keys written, in order.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>Gets the stored keys.</summary>
    public IReadOnlyCollection<string> Keys => _objects.Keys.ToList();

    /// <summary>Stores an object directly, as if an earlier run had written it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="contentType">The MIME type.</param>
    public void Seed(string key, byte[] bytes, string contentType = "application/octet-stream") => _objects[key] = (bytes, contentType);

    /// <summary>Reads a stored object's bytes.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The bytes.</returns>
    public byte[] Bytes(string key) => _objects[key].Bytes;

    /// <summary>Reads a stored object's MIME type.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The MIME type.</returns>
    public string ContentType(string key) => _objects[key].ContentType;

    /// <inheritdoc/>
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _objects[key] = (buffer.ToArray(), contentType);
        Writes.Add(key);
    }

    /// <inheritdoc/>
    public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var item)
            ? new StoredObject(new MemoryStream(item.Bytes, writable: false), item.ContentType, item.Bytes.Length)
            : null);

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_objects.ContainsKey(key));

    /// <inheritdoc/>
    public async Task DownloadToFileAsync(string key, string filePath, CancellationToken cancellationToken = default)
    {
        if (!_objects.TryGetValue(key, out var item))
        {
            throw new FileNotFoundException($"No object {key}.");
        }

        await File.WriteAllBytesAsync(filePath, item.Bytes, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UploadFileAsync(string filePath, string key, string contentType, CancellationToken cancellationToken = default)
    {
        _objects[key] = (await File.ReadAllBytesAsync(filePath, cancellationToken), contentType);
        Writes.Add(key);
    }
}

/// <summary>Real temporary folders that record whether they were cleaned up.</summary>
internal sealed class TestWorkspace : IWorkspace, IDisposable
{
    /// <summary>Every folder handed out.</summary>
    private readonly List<TestWorkFolder> _folders = [];

    /// <summary>Gets a value indicating whether every folder handed out was disposed.</summary>
    public bool AllCleanedUp => _folders.TrueForAll(folder => folder.Disposed);

    /// <summary>Gets how many folders were handed out.</summary>
    public int Created => _folders.Count;

    /// <inheritdoc/>
    public IWorkFolder Create()
    {
        var folder = new TestWorkFolder(Directory.CreateTempSubdirectory("ssd-ingest-test-").FullName);
        _folders.Add(folder);
        return folder;
    }

    /// <summary>Deletes whatever the tests left behind.</summary>
    public void Dispose()
    {
        foreach (var folder in _folders.Where(folder => Directory.Exists(folder.Path)))
        {
            Directory.Delete(folder.Path, recursive: true);
        }
    }

    /// <summary>One temporary folder.</summary>
    /// <param name="path">The folder.</param>
    private sealed class TestWorkFolder(string path) : IWorkFolder
    {
        /// <inheritdoc/>
        public string Path { get; } = path;

        /// <summary>Gets a value indicating whether the folder was disposed.</summary>
        public bool Disposed { get; private set; }

        /// <summary>Marks the folder cleaned up (the workspace deletes the files at the end of the test).</summary>
        public void Dispose() => Disposed = true;
    }
}

/// <summary>A clock stuck at one moment.</summary>
/// <param name="now">The moment.</param>
internal sealed class StuckClock(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Builders shared by the ingest tests.</summary>
internal static class IngestData
{
    /// <summary>The fixed time.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A valid md5.</summary>
    public const string Md5 = "9e107d9d372bb6826bd81d3542a419d6";

    /// <summary>A different valid md5.</summary>
    public const string OtherMd5 = "e4d909c290d0fb1ca068ffaddf22cbd0";

    /// <summary>Builds a complete, published player.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The player.</returns>
    public static Player PublishedPlayer(string name = "Ali Hoshmand")
    {
        var player = WaitingPlayer(name);
        player.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/ali-hoshmand-p81001"));
        player.Publish(Now);
        return player;
    }

    /// <summary>Builds a player with everything but the Volleybox link, so it waits for an admin.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The player.</returns>
    public static Player WaitingPlayer(string name = "Ali Hoshmand")
    {
        var player = Player.Create(PlayerCategory.Men, Now);
        var fullName = FullName.Create(name);
        player.Rename(fullName, Slug.FromName(fullName));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo-1761750000000.webp"));
        return player;
    }

    /// <summary>Builds a queued Drive asset.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="driveFileId">The Drive file id.</param>
    /// <param name="md5">The md5.</param>
    /// <param name="sortOrder">The position.</param>
    /// <returns>The asset.</returns>
    public static Asset QueuedAsset(Guid playerId, Guid? folderId, string fileName, string? driveFileId = "drive-1", string md5 = Md5, int sortOrder = 0)
    {
        var tags = AssetTagger.Classify([], fileName);
        return Asset.Add(playerId, folderId, AssetSource.Drive, driveFileId, AssetFile.Create(fileName, md5, 1234), tags.Title, tags.Tags, sortOrder, Now);
    }

    /// <summary>Builds an asset whose original is stored, ready for processing.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The asset.</returns>
    public static Asset ProcessingAsset(Guid playerId, string fileName)
    {
        var asset = QueuedAsset(playerId, null, fileName);
        asset.AttachOriginal(StorageKey.Create($"originals/{Md5}{Path.GetExtension(fileName).ToLowerInvariant()}"));
        asset.ClearDomainEvents();
        return asset;
    }
}
