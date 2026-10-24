using System.Text.RegularExpressions;

namespace SsSport.Drive.Domain.Assets;

/// <summary>What an asset shows. A file can carry several tags; <see cref="Untagged"/> sorts last.</summary>
[Flags]
public enum AssetTags
{
    /// <summary>No tag could be found.</summary>
    Untagged = 0,

    /// <summary>A highlight reel.</summary>
    Highlight = 1 << 0,

    /// <summary>Spikes / attacks.</summary>
    Spike = 1 << 1,

    /// <summary>Serves.</summary>
    Serve = 1 << 2,

    /// <summary>Blocks.</summary>
    Block = 1 << 3,

    /// <summary>Reception / passing.</summary>
    Reception = 1 << 4,

    /// <summary>Defence / digs.</summary>
    Defence = 1 << 5,

    /// <summary>A full match.</summary>
    FullMatch = 1 << 6,

    /// <summary>Stats sheets and screenshots (the archive's <c>STATS</c> folders).</summary>
    Stats = 1 << 7,
}

/// <summary>The tags and title suggested for a file.</summary>
/// <param name="Tags">The suggested tags.</param>
/// <param name="Title">The suggested title.</param>
public sealed record AssetClassification(AssetTags Tags, string Title);

/// <summary>
/// Suggests tags and a title for a file from where it sits in the player folder. The subfolder name wins
/// (every spelling found in the real archive is covered); file-name keywords are the fallback.
/// </summary>
public static partial class AssetTagger
{
    /// <summary>File-name keyword rules: a word that equals or starts with a keyword gives the tag.</summary>
    private static readonly (AssetTags Tag, string[] Exact, string[] Prefixes)[] _keywordRules =
    [
        (AssetTags.Highlight, ["hl"], ["highlight"]),
        (AssetTags.Spike, [], ["spike", "attack"]),
        (AssetTags.Serve, [], ["serve", "serving"]),
        (AssetTags.Block, [], ["block"]),
        (AssetTags.Reception, [], ["recep", "receiv", "pass"]),
        (AssetTags.Defence, ["def", "dig", "digs"], ["defen", "digging"]),
        (AssetTags.FullMatch, [], ["full", "match", "game"]),
    ];

    /// <summary>Suggests tags and a title for a file.</summary>
    /// <param name="subfolders">The folders between the player folder and the file, outermost first; empty when the file sits in the player folder.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The suggestion.</returns>
    public static AssetClassification Classify(IReadOnlyList<string> subfolders, string fileName)
    {
        ArgumentNullException.ThrowIfNull(subfolders);

        for (var i = 0; i < subfolders.Count; i++)
        {
            var tag = FolderTag(subfolders[i]);
            if (tag != AssetTags.Untagged)
            {
                // A folder below the tag folder (one per match or competition) names the video.
                var title = i + 1 < subfolders.Count ? subfolders[^1].Trim() : TitleFromFileName(fileName);
                return new AssetClassification(tag, title);
            }
        }

        return new AssetClassification(KeywordTags(fileName), TitleFromFileName(fileName));
    }

    /// <summary>Recognises a tag folder name.</summary>
    /// <param name="folder">The folder name.</param>
    /// <returns>The tag, or <see cref="AssetTags.Untagged"/>.</returns>
    private static AssetTags FolderTag(string folder)
    {
        var squashed = Whitespace().Replace(folder, string.Empty).ToLowerInvariant();
        if (FullMatchFolder().IsMatch(squashed))
        {
            return AssetTags.FullMatch;
        }

        if (squashed.Contains("highlight", StringComparison.Ordinal))
        {
            return AssetTags.Highlight;
        }

        return Words(folder).Any(word => word is "stats" or "stat" or "statistics") ? AssetTags.Stats : AssetTags.Untagged;
    }

    /// <summary>Collects every keyword tag in a file name.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The tags, or <see cref="AssetTags.Untagged"/>.</returns>
    private static AssetTags KeywordTags(string fileName)
    {
        var tags = AssetTags.Untagged;
        foreach (var word in Words(Path.GetFileNameWithoutExtension(fileName)))
        {
            foreach (var (tag, exact, prefixes) in _keywordRules)
            {
                if (exact.Contains(word) || prefixes.Any(prefix => word.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    tags |= tag;
                }
            }
        }

        return tags;
    }

    /// <summary>Builds a title from a file name: no extension, separators turned into single spaces.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The title.</returns>
    private static string TitleFromFileName(string fileName) =>
        Whitespace().Replace(Path.GetFileNameWithoutExtension(fileName.Trim()).Replace('_', ' '), " ").Trim();

    /// <summary>Splits text into lower-case words of letters and digits.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The words.</returns>
    private static IEnumerable<string> Words(string text) =>
        NonWord().Split(text.ToLowerInvariant()).Where(word => word.Length > 0);

    /// <summary>"full" followed by "match" or "game" (spaces removed, any plural or typo ending).</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex("full(match|game)", RegexOptions.CultureInvariant)]
    private static partial Regex FullMatchFolder();

    /// <summary>Runs of whitespace.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    /// <summary>Anything that is not a letter or a digit.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWord();
}
