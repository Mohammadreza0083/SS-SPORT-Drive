namespace SsSport.Drive.Application.Abstractions.Images;

/// <summary>
/// Turns an uploaded player photo into the stored WebP (T-4b): checks the format and size, applies the camera's
/// orientation, strips every bit of metadata and scales it down.
/// </summary>
public interface IPhotoProcessor
{
    /// <summary>Processes an uploaded photo.</summary>
    /// <param name="upload">The uploaded bytes.</param>
    /// <returns>The WebP and its size.</returns>
    /// <exception cref="Domain.Primitives.DomainException">
    /// <c>player.photo_unreadable</c> for anything that is not a JPEG, PNG, WebP or HEIC image;
    /// <c>player.photo_too_small</c> when the shorter side is under 800 px.
    /// </exception>
    ProcessedPhoto Process(Stream upload);
}

/// <summary>A processed player photo.</summary>
/// <param name="Webp">The WebP bytes.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="OriginalContentType">The MIME type of the upload as it was detected (not as the client claimed).</param>
public sealed record ProcessedPhoto(byte[] Webp, int Width, int Height, string OriginalContentType);
