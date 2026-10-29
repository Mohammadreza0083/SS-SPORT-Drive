using System.Text.RegularExpressions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.DriveFolders;

/// <summary>
/// A pasted Google Drive folder link (or a bare folder id), reduced to the folder id (T-4a). Accepts
/// <c>drive.google.com/drive/folders/{id}</c>, <c>/drive/u/{n}/folders/{id}</c> and <c>/open?id={id}</c>.
/// </summary>
public sealed partial record DriveFolderLink
{
    /// <summary>Initializes the link.</summary>
    /// <param name="folderId">The Drive folder id.</param>
    private DriveFolderLink(string folderId) => FolderId = folderId;

    /// <summary>Gets the Drive folder id.</summary>
    public string FolderId { get; }

    /// <summary>Parses a pasted folder link or id.</summary>
    /// <param name="raw">The link, with or without scheme, or the bare id.</param>
    /// <returns>The link.</returns>
    /// <exception cref="DomainException"><c>drive_folder.link_invalid</c> for anything that is not a Drive folder.</exception>
    public static DriveFolderLink Create(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (FolderIdPattern().IsMatch(text))
        {
            return new DriveFolderLink(text);
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && string.Equals(uri.Host, "drive.google.com", StringComparison.OrdinalIgnoreCase)
            && FindFolderId(uri) is { } folderId)
        {
            return new DriveFolderLink(folderId);
        }

        throw new DomainException("drive_folder.link_invalid", "Paste the link of the player's Google Drive folder, e.g. https://drive.google.com/drive/folders/1AbC….");
    }

    /// <summary>Finds the folder id in a Drive URL's path (<c>…/folders/{id}</c>) or query (<c>open?id={id}</c>).</summary>
    /// <param name="uri">The Drive URL.</param>
    /// <returns>The id, or <see langword="null"/> when the URL is not a folder link.</returns>
    private static string? FindFolderId(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folders = Array.IndexOf(segments, "folders");
        if (segments.Length > 0 && segments[0] == "drive" && folders > 0 && folders + 1 < segments.Length)
        {
            var id = segments[folders + 1];
            return FolderIdPattern().IsMatch(id) ? id : null;
        }

        if (uri.AbsolutePath == "/open")
        {
            var id = uri.Query.TrimStart('?').Split('&')
                .Select(pair => pair.Split('=', 2))
                .Where(pair => pair.Length == 2 && pair[0] == "id")
                .Select(pair => Uri.UnescapeDataString(pair[1]))
                .FirstOrDefault();
            return id is not null && FolderIdPattern().IsMatch(id) ? id : null;
        }

        return null;
    }

    /// <summary>A Drive id: 10 to 100 of A–Z, a–z, 0–9, <c>-</c> and <c>_</c>.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex("^[A-Za-z0-9_-]{10,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex FolderIdPattern();

    /// <inheritdoc/>
    public override string ToString() => FolderId;
}
