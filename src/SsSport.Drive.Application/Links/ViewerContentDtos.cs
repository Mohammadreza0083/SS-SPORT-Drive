using SsSport.Drive.Application.MiniApp;

namespace SsSport.Drive.Application.Links;

/// <summary>What a signed-in viewer sees about the link itself (V-2b): its kind, who it was prepared for, the note and their own email.</summary>
public sealed record LinkContentDto
{
    /// <summary>Gets the link type: <c>Personal</c> (one player) or <c>Roster</c> (up to 20).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Gets how many players the link shows.</summary>
    public int PlayerCount { get; init; }

    /// <summary>Gets who the link was prepared for.</summary>
    public LinkRecipientDto Recipient { get; init; } = new();

    /// <summary>Gets the admin's note on the link, if any.</summary>
    public string? Note { get; init; }

    /// <summary>Gets the email the viewer verified, laid faintly over the videos and documents.</summary>
    public string ViewerEmail { get; init; } = string.Empty;
}

/// <summary>Who a link was prepared for, as the "Prepared for" ribbon shows it. Their emails are never sent.</summary>
public sealed record LinkRecipientDto
{
    /// <summary>Gets the recipient's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the recipient's club or agency, if known.</summary>
    public string? Organisation { get; init; }
}

/// <summary>A position with its short code and label.</summary>
public sealed record PositionDto
{
    /// <summary>Gets the short code: S, OH, OPP, MB, L or DS.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the English label, e.g. "Opposite".</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>One player card on a Roster link.</summary>
public sealed record LinkPlayerCardDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the full name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the first name.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Gets the primary position.</summary>
    public PositionDto Position { get; init; } = new();

    /// <summary>Gets the nationality.</summary>
    public CountryDto Nationality { get; init; } = new();

    /// <summary>Gets a value indicating whether the player has a photo to show.</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Gets how many videos the viewer can watch.</summary>
    public int VideoCount { get; init; }
}

/// <summary>One player's page header: the card, the Volleybox profile and the stats row.</summary>
public sealed record LinkPlayerDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the full name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the first name, used in "Interested in {first name}?".</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Gets the primary position.</summary>
    public PositionDto Position { get; init; } = new();

    /// <summary>Gets the nationality.</summary>
    public CountryDto Nationality { get; init; } = new();

    /// <summary>Gets the player's Volleybox profile.</summary>
    public string VolleyboxUrl { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the player has a photo to show.</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Gets the stats row.</summary>
    public PlayerStatsDto Stats { get; init; } = new();
}

/// <summary>The stats row of a player page, counting only what the viewer can watch or read.</summary>
public sealed record PlayerStatsDto
{
    /// <summary>Gets the number of videos.</summary>
    public int Videos { get; init; }

    /// <summary>Gets the total length of the videos in whole minutes.</summary>
    public int Minutes { get; init; }

    /// <summary>Gets the number of documents.</summary>
    public int Documents { get; init; }

    /// <summary>Gets the number of DataVolley scout files the viewer can open (shown as matches).</summary>
    public int Matches { get; init; }
}

/// <summary>One video on a player page. Its stream is signed per viewer by the edge (V-5b); no storage key is sent.</summary>
public sealed record LinkVideoDto
{
    /// <summary>Gets the video id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the display title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the tag shown on the chips: Highlight, Spike, Serve, Block, Reception, Defence, Full match, Stats, or Video when untagged.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Gets the length in whole seconds.</summary>
    public int DurationSeconds { get; init; }
}

/// <summary>One view-only document on a player page. Its page images are signed by the edge (I-4, V-5b); the file is never sent.</summary>
public sealed record LinkDocumentDto
{
    /// <summary>Gets the document id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the display title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the number of pages (1 for an image).</summary>
    public int PageCount { get; init; }
}

/// <summary>
/// The facts about one player a viewer page needs, read in one projection (no tracking): the card, the profile, whether
/// a photo exists, and the counts and total length of what is ready and visible.
/// </summary>
/// <param name="Id">The player id.</param>
/// <param name="Name">The full name.</param>
/// <param name="NationalityCode">The nationality code.</param>
/// <param name="Positions">The position codes, primary first.</param>
/// <param name="HasPhoto">Whether a photo is stored.</param>
/// <param name="VolleyboxUrl">The Volleybox profile.</param>
/// <param name="VideoCount">Ready and visible videos.</param>
/// <param name="VideoSeconds">Their total length in seconds.</param>
/// <param name="DocumentCount">Ready and visible documents.</param>
/// <param name="ScoutCount">Ready and visible DataVolley scout files.</param>
public sealed record LinkPlayerFacts(
    Guid Id,
    string Name,
    string NationalityCode,
    IReadOnlyList<string> Positions,
    bool HasPhoto,
    string VolleyboxUrl,
    int VideoCount,
    double VideoSeconds,
    int DocumentCount,
    int ScoutCount);
