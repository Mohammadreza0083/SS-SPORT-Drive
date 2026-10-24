using System.Text.RegularExpressions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Assets;

/// <summary>What kind of file an asset is; decides how it is processed and shown.</summary>
public enum AssetKind
{
    /// <summary>A video (mp4, mov, m4v), streamed as HLS.</summary>
    Video,

    /// <summary>A PDF or a stats screenshot (png, jpg), shown view-only in the Documents section.</summary>
    Document,

    /// <summary>A DataVolley scout file (dvw), shown in the in-page scout viewer.</summary>
    Scout,
}

/// <summary>The facts of the source file: its name, md5 and size. The kind follows from the extension.</summary>
public sealed partial record AssetFile
{
    /// <summary>Supported extensions and the kind each maps to.</summary>
    private static readonly Dictionary<string, AssetKind> _kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = AssetKind.Video,
        [".mov"] = AssetKind.Video,
        [".m4v"] = AssetKind.Video,
        [".pdf"] = AssetKind.Document,
        [".png"] = AssetKind.Document,
        [".jpg"] = AssetKind.Document,
        [".jpeg"] = AssetKind.Document,
        [".dvw"] = AssetKind.Scout,
    };

    /// <summary>Initializes the file with validated values.</summary>
    /// <param name="originalName">The file name.</param>
    /// <param name="md5">The lower-case md5.</param>
    /// <param name="sizeBytes">The size in bytes.</param>
    /// <param name="kind">The detected kind.</param>
    private AssetFile(string originalName, string md5, long sizeBytes, AssetKind kind)
    {
        OriginalName = originalName;
        Md5 = md5;
        SizeBytes = sizeBytes;
        Kind = kind;
    }

    /// <summary>Gets the file name as it is in Drive.</summary>
    public string OriginalName { get; }

    /// <summary>Gets the md5 checksum (lower-case hex); identical content is stored once.</summary>
    public string Md5 { get; }

    /// <summary>Gets the size in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Gets the kind detected from the extension.</summary>
    public AssetKind Kind { get; }

    /// <summary>Validates a source file.</summary>
    /// <param name="originalName">The file name; its extension decides the kind.</param>
    /// <param name="md5">The md5 checksum as hex, any case.</param>
    /// <param name="sizeBytes">The size in bytes.</param>
    /// <returns>The file.</returns>
    /// <exception cref="DomainException"><c>asset.unsupported_file_type</c>, <c>asset.md5_invalid</c> or <c>asset.file_empty</c>.</exception>
    public static AssetFile Create(string? originalName, string? md5, long sizeBytes)
    {
        var name = (originalName ?? string.Empty).Trim();
        if (!TryDetectKind(name, out var kind))
        {
            throw new DomainException("asset.unsupported_file_type", "Only videos (mp4, mov, m4v), PDFs, images (png, jpg) and DataVolley files (dvw) are supported.");
        }

        var checksum = (md5 ?? string.Empty).Trim().ToLowerInvariant();
        if (!Md5Pattern().IsMatch(checksum))
        {
            throw new DomainException("asset.md5_invalid", "The md5 checksum must be 32 hexadecimal characters.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException("asset.file_empty", "Empty files are skipped.");
        }

        return new AssetFile(name, checksum, sizeBytes, kind);
    }

    /// <summary>Detects the kind of a file from its extension.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="kind">The kind, when supported.</param>
    /// <returns><see langword="true"/> for supported files.</returns>
    public static bool TryDetectKind(string? fileName, out AssetKind kind)
    {
        kind = default;
        var extension = Path.GetExtension((fileName ?? string.Empty).Trim());
        return extension.Length > 1 && _kinds.TryGetValue(extension, out kind);
    }

    /// <summary>32 lower-case hex characters.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Md5Pattern();
}
