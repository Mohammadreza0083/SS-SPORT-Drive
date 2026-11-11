using SsSport.Drive.Application.MiniApp;

namespace SsSport.Drive.Application.Links;

/// <summary>A recipient of personal links, as admins see it (full emails: admin-only).</summary>
public sealed record RecipientDto
{
    /// <summary>Gets the recipient id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the person's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the club or agency, or <see langword="null"/>.</summary>
    public string? Organisation { get; init; }

    /// <summary>Gets the organisation's two-letter country code, or <see langword="null"/>.</summary>
    public string? CountryCode { get; init; }

    /// <summary>Gets the email addresses that pass the email gate, lower-cased.</summary>
    public IReadOnlyList<string> Emails { get; init; } = [];
}

/// <summary>A player on a link, in page order.</summary>
public sealed record ShareLinkPlayerDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the full name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the URL handle.</summary>
    public string? Slug { get; init; }
}

/// <summary>A personal link with its recipient and players, as admins see it.</summary>
public sealed record ShareLinkDto
{
    /// <summary>Gets the link id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the six-character code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the link's path on the viewer site, <c>/r/{code}</c>.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets <c>Personal</c> (one player) or <c>Roster</c> (several).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Gets <c>Active</c>, <c>Paused</c> or <c>Revoked</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets whether it opens right now: <c>Open</c>, <c>Expired</c>, <c>Paused</c> or <c>Revoked</c>.</summary>
    public string Access { get; init; } = string.Empty;

    /// <summary>Gets the recipient.</summary>
    public RecipientDto Recipient { get; init; } = new();

    /// <summary>Gets the players, in page order.</summary>
    public IReadOnlyList<ShareLinkPlayerDto> Players { get; init; } = [];

    /// <summary>Gets the personal note, or <see langword="null"/>.</summary>
    public string? Note { get; init; }

    /// <summary>Gets when it stops opening (UTC), or <see langword="null"/> for never.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets when it was revoked (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Gets whether another device must verify an email first.</summary>
    public bool VerifyOnNewDevice { get; init; }

    /// <summary>Gets whether an email that is not the recipient's may open it after verifying.</summary>
    public bool AllowForwarding { get; init; }

    /// <summary>Gets <c>AnyEmail</c> or <c>SameDomain</c>.</summary>
    public string ForwardingScope { get; init; } = string.Empty;

    /// <summary>Gets whether admins are told when it is opened.</summary>
    public bool NotifyOnOpen { get; init; }

    /// <summary>Gets the admin who created it, or <see langword="null"/> for the system.</summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>Gets when it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One row of the share links list.</summary>
public sealed record ShareLinkListItemDto
{
    /// <summary>Gets the link id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the six-character code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets <c>Personal</c> or <c>Roster</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Gets <c>Active</c>, <c>Paused</c> or <c>Revoked</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets <c>Open</c>, <c>Expired</c>, <c>Paused</c> or <c>Revoked</c>.</summary>
    public string Access { get; init; } = string.Empty;

    /// <summary>Gets the recipient id.</summary>
    public Guid RecipientId { get; init; }

    /// <summary>Gets the recipient's name.</summary>
    public string RecipientName { get; init; } = string.Empty;

    /// <summary>Gets the recipient's organisation, or <see langword="null"/>.</summary>
    public string? RecipientOrganisation { get; init; }

    /// <summary>Gets the players' names, in page order.</summary>
    public IReadOnlyList<string> PlayerNames { get; init; } = [];

    /// <summary>Gets when it stops opening (UTC), or <see langword="null"/> for never.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets when it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// What anyone may learn about an open link (<c>GET /links/{code}</c>): enough for the viewer site to show the email gate
/// (L-2), and nothing about the players or the recipient beyond a masked email.
/// </summary>
public sealed record ResolvedShareLinkDto
{
    /// <summary>Gets the code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets <c>Personal</c> or <c>Roster</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Gets how many players the link shows.</summary>
    public int PlayerCount { get; init; }

    /// <summary>Gets the recipient's first email, masked (<c>m***@lube.it</c>).</summary>
    public string RecipientHint { get; init; } = string.Empty;

    /// <summary>Gets the player on the gate card for a Personal link, or <see langword="null"/> for a Roster (L-2).</summary>
    public GatePlayerDto? Player { get; init; }
}

/// <summary>What the gate card shows of a player before the email is verified (L-2): no photo, videos or files.</summary>
public sealed record GatePlayerDto
{
    /// <summary>Gets the full name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the primary position's short code, e.g. <c>OPP</c>.</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>Gets the nationality.</summary>
    public CountryDto Nationality { get; init; } = new();
}
