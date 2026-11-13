using System.Collections.Concurrent;
using SsSport.Drive.Application.Abstractions.Storage;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>Stands in for R2 / RustFS in API tests: keeps objects in memory and lets tests inspect them.</summary>
public sealed class InMemoryObjectStorage : IObjectStorage
{
    /// <summary>The stored objects by key.</summary>
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);

    /// <summary>Gets the stored keys.</summary>
    public IReadOnlyCollection<string> Keys => [.. _objects.Keys];

    /// <summary>Gets an object's content type.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The content type.</returns>
    public string ContentTypeOf(string key) => _objects[key].ContentType;

    /// <inheritdoc/>
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        _objects[key] = (copy.ToArray(), contentType);
    }

    /// <inheritdoc/>
    public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var stored)
            ? new StoredObject(new MemoryStream(stored.Bytes), stored.ContentType, stored.Bytes.Length)
            : null);

    /// <summary>Tells whether an object exists (the ingest members of <see cref="IObjectStorage"/>, I-2).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns><see langword="true"/> when stored.</returns>
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_objects.ContainsKey(key));

    /// <summary>Writes an object to a local file.</summary>
    /// <param name="key">The key.</param>
    /// <param name="filePath">The file to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public Task DownloadToFileAsync(string key, string filePath, CancellationToken cancellationToken = default) =>
        File.WriteAllBytesAsync(filePath, _objects[key].Bytes, cancellationToken);

    /// <summary>Stores a local file as an object.</summary>
    /// <param name="filePath">The file to read.</param>
    /// <param name="key">The key.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the object is stored.</returns>
    public async Task UploadFileAsync(string filePath, string key, string contentType, CancellationToken cancellationToken = default) =>
        _objects[key] = (await File.ReadAllBytesAsync(filePath, cancellationToken), contentType);
}
