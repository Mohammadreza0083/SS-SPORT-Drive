using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.MiniApp;

namespace SsSport.Drive.Application.Public;

/// <summary>
/// The facts the public site reads about one published player in one read-only projection (P-8). The id stays inside
/// the Application layer; no DTO carries it.
/// </summary>
/// <param name="Id">The player.</param>
/// <param name="Slug">The URL-safe name.</param>
/// <param name="Name">The full name.</param>
/// <param name="NationalityCode">The nationality code.</param>
/// <param name="Positions">The position codes, primary first.</param>
/// <param name="HasPhoto">Whether a photo exists.</param>
/// <param name="VolleyboxUrl">The Volleybox profile.</param>
/// <param name="VideoCount">How many ready, visible videos exist.</param>
/// <param name="VideoSeconds">Their total length.</param>
/// <param name="DocumentCount">How many ready, visible documents exist.</param>
public sealed record PublicPlayerFacts(
    Guid Id,
    string Slug,
    string Name,
    string NationalityCode,
    IReadOnlyList<string> Positions,
    bool HasPhoto,
    string VolleyboxUrl,
    int VideoCount,
    double VideoSeconds,
    int DocumentCount);

/// <summary>A player card on the public home page: only facts the public may see.</summary>
public sealed record PublicPlayerCardDto
{
    /// <summary>Gets the URL-safe name; the card leads to <c>/players/{slug}</c>.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Gets the full name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the primary position.</summary>
    public PositionDto Position { get; init; } = new();

    /// <summary>Gets the nationality.</summary>
    public CountryDto Nationality { get; init; } = new();

    /// <summary>Gets how many videos exist (the videos themselves stay private).</summary>
    public int Videos { get; init; }

    /// <summary>Gets how many documents exist.</summary>
    public int Documents { get; init; }

    /// <summary>Gets whether a photo exists at <c>/public/players/{slug}/photo</c>.</summary>
    public bool HasPhoto { get; init; }
}

/// <summary>The header of the locked player page.</summary>
public sealed record PublicPlayerDto
{
    /// <summary>Gets the URL-safe name.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Gets the full name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the first name, shown on its own line.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Gets the primary position.</summary>
    public PositionDto Position { get; init; } = new();

    /// <summary>Gets the nationality.</summary>
    public CountryDto Nationality { get; init; } = new();

    /// <summary>Gets the Volleybox profile.</summary>
    public string VolleyboxUrl { get; init; } = string.Empty;

    /// <summary>Gets whether a photo exists.</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Gets the counts of footage.</summary>
    public PlayerStatsDto Stats { get; init; } = new();
}

/// <summary>A locked video: its title, tag and length only. It has no id, address, stream or thumbnail (P-2).</summary>
public sealed record LockedVideoDto
{
    /// <summary>Gets the title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the first tag, or <c>Video</c>.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Gets the length in whole seconds.</summary>
    public int DurationSeconds { get; init; }
}

/// <summary>A locked document: its title and page count only.</summary>
public sealed record LockedDocumentDto
{
    /// <summary>Gets the title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the number of pages.</summary>
    public int PageCount { get; init; }
}

/// <summary>The body of a request for a code.</summary>
public sealed record AccessCodeFormDto
{
    /// <summary>Gets the email the code should go to.</summary>
    public string? Email { get; init; }
}

/// <summary>The body of a request for access.</summary>
public sealed record AccessRequestFormDto
{
    /// <summary>Gets the slug of the player asked for.</summary>
    public string? PlayerSlug { get; init; }

    /// <summary>Gets what the visitor writes about themselves (1 to 1000 characters).</summary>
    public string? About { get; init; }

    /// <summary>Gets the verified email; ignored for a signed-in viewer.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the 6-digit code; ignored for a signed-in viewer.</summary>
    public string? Code { get; init; }

    /// <summary>Gets the Cloudflare Turnstile token; not needed for a signed-in viewer.</summary>
    public string? TurnstileToken { get; init; }
}

/// <summary>What the dialog shows after a request was made.</summary>
public sealed record AccessRequestSentDto
{
    /// <summary>Gets the status, always <c>pending</c> today.</summary>
    public string Status { get; init; } = "pending";
}

/// <summary>Who is signed in on the public site.</summary>
public sealed record PublicViewerDto
{
    /// <summary>Gets the viewer's email.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets when the session ends.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
