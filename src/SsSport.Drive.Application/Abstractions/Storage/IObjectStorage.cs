namespace SsSport.Drive.Application.Abstractions.Storage;

/// <summary>
/// The media bucket (Cloudflare R2; RustFS locally). Objects are private: the API reads them and streams them on.
/// Keys are relative paths such as <c>players/{id}/photo-1761750000000.webp</c>.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Stores an object, replacing any object with the same key.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="content">The bytes; read to the end, not disposed.</param>
    /// <param name="contentType">The MIME type to store with it.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>A task that completes when the object is stored.</returns>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Reads an object.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The object, or <see langword="null"/> when there is none with that key.</returns>
    Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>An object read from storage; the caller disposes <see cref="Content"/>.</summary>
/// <param name="Content">The bytes.</param>
/// <param name="ContentType">The stored MIME type.</param>
/// <param name="Length">The size in bytes.</param>
public sealed record StoredObject(Stream Content, string ContentType, long Length);
