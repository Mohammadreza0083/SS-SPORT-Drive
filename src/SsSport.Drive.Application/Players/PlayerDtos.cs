namespace SsSport.Drive.Application.Players;

/// <summary>A player in lists (players page, waiting list, search results): plain values only.</summary>
public record PlayerSummaryDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the full name, or <see langword="null"/> while unknown.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the URL slug, or <see langword="null"/> while the name is unknown.</summary>
    public string? Slug { get; init; }

    /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Gets the two-letter country code, or <see langword="null"/>.</summary>
    public string? NationalityCode { get; init; }

    /// <summary>Gets the country's English name, or <see langword="null"/>.</summary>
    public string? NationalityName { get; init; }

    /// <summary>Gets the position codes, primary first (e.g. <c>OH</c>, <c>OPP</c>); empty while unknown.</summary>
    public IReadOnlyList<string> Positions { get; init; } = [];

    /// <summary>Gets the storage key of the photo, or <see langword="null"/>.</summary>
    public string? PhotoKey { get; init; }

    /// <summary>Gets <c>Waiting</c> or <c>Published</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether every detail needed for publishing is set.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Gets when the player was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when the player was published (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>One player with every detail (player page in the console, waiting-list drawer).</summary>
public sealed record PlayerDetailsDto : PlayerSummaryDto
{
    /// <summary>Gets the Volleybox player id, or <see langword="null"/>.</summary>
    public int? VolleyboxId { get; init; }

    /// <summary>Gets the Volleybox page URL, or <see langword="null"/>.</summary>
    public string? VolleyboxUrl { get; init; }

    /// <summary>Gets the details still needed before publishing (<c>Name</c>, <c>Nationality</c>, <c>Positions</c>, <c>Volleybox</c>, <c>Photo</c>).</summary>
    public IReadOnlyList<string> MissingDetails { get; init; } = [];

    /// <summary>Gets the hero video asset id, or <see langword="null"/>.</summary>
    public Guid? HeroAssetId { get; init; }

    /// <summary>Gets the responsible analyst's user id, or <see langword="null"/>.</summary>
    public Guid? AnalystId { get; init; }

    /// <summary>Gets the storage key of the QR code image, or <see langword="null"/>.</summary>
    public string? QrCodeKey { get; init; }
}
