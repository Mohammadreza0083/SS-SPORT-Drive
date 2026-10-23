using System.Globalization;
using System.Text.RegularExpressions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>
/// A player's Volleybox page: the player id parsed from the URL (<c>…-p48213</c> → 48213) and the normalised URL.
/// Volleybox is never scraped; only the pasted URL is read.
/// </summary>
public sealed partial record VolleyboxProfile
{
    /// <summary>Initializes the profile with parsed values.</summary>
    /// <param name="playerId">The Volleybox player id.</param>
    /// <param name="url">The normalised URL.</param>
    private VolleyboxProfile(int playerId, string url)
    {
        PlayerId = playerId;
        Url = url;
    }

    /// <summary>Gets the Volleybox player id, unique per player.</summary>
    public int PlayerId { get; }

    /// <summary>Gets the normalised URL: https, lower-case, no <c>www.</c>, no sub-page, query or trailing slash.</summary>
    public string Url { get; }

    /// <summary>Parses a pasted Volleybox player URL.</summary>
    /// <param name="raw">The URL, with or without scheme.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="DomainException"><c>player.volleybox_not_a_player</c> for team pages, <c>player.volleybox_url_invalid</c> otherwise.</exception>
    public static VolleyboxProfile Create(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !IsVolleyboxHost(uri.Host))
        {
            throw Invalid();
        }

        var host = uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
        var segment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

        if (TeamSegment().IsMatch(segment))
        {
            throw new DomainException("player.volleybox_not_a_player", "This is a Volleybox team page; paste the player's page instead.");
        }

        var match = PlayerSegment().Match(segment);
        if (!match.Success || !int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var playerId) || playerId <= 0)
        {
            throw Invalid();
        }

        return new VolleyboxProfile(playerId, $"https://{host}/{segment}");
    }

    /// <summary>Checks for volleybox.net or one of its sub-domains.</summary>
    /// <param name="host">The lower-case host.</param>
    /// <returns><see langword="true"/> for Volleybox hosts.</returns>
    private static bool IsVolleyboxHost(string host) =>
        host == "volleybox.net" || host.EndsWith(".volleybox.net", StringComparison.Ordinal);

    /// <summary>Builds the generic invalid-URL error.</summary>
    /// <returns>The exception.</returns>
    private static DomainException Invalid() =>
        new("player.volleybox_url_invalid", "Paste the player's Volleybox page URL, e.g. https://volleybox.net/name-p12345.");

    /// <summary>A player page segment: <c>name-p12345</c>.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^[a-z0-9-]+-p(?<id>\d{1,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerSegment();

    /// <summary>A team page segment: <c>name-t12345</c>.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^[a-z0-9-]+-t\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex TeamSegment();
}
