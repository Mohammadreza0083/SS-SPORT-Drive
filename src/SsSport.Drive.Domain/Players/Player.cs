using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>Whether a player plays men's or women's volleyball; taken from the archive's top-level folder.</summary>
public enum PlayerCategory
{
    /// <summary>Men's volleyball.</summary>
    Men,

    /// <summary>Women's volleyball.</summary>
    Women,
}

/// <summary>Where a player is in its lifecycle.</summary>
public enum PlayerStatus
{
    /// <summary>On the admin waiting list: not visible and no links can be created.</summary>
    Waiting,

    /// <summary>Published by an admin: visible and shareable.</summary>
    Published,
}

/// <summary>A detail a player needs before it can be published.</summary>
public enum PlayerDetail
{
    /// <summary>The full name.</summary>
    Name,

    /// <summary>The nationality.</summary>
    Nationality,

    /// <summary>The positions.</summary>
    Positions,

    /// <summary>The Volleybox page.</summary>
    Volleybox,

    /// <summary>The photo.</summary>
    Photo,
}

/// <summary>Raised when a player is created.</summary>
/// <param name="PlayerId">The new player.</param>
public sealed record PlayerCreated(Guid PlayerId) : IDomainEvent;

/// <summary>Raised when any of a player's details changes.</summary>
/// <param name="PlayerId">The changed player.</param>
public sealed record PlayerDetailsChanged(Guid PlayerId) : IDomainEvent;

/// <summary>Raised when an admin publishes a complete player; starts transcoding and notifies the analyst.</summary>
/// <param name="PlayerId">The published player.</param>
public sealed record PlayerPublished(Guid PlayerId) : IDomainEvent;

/// <summary>
/// A volleyball player represented by SS-SPORT. A player starts with only a category and waits on the admin
/// waiting list until every <see cref="PlayerDetail"/> is set; only then can it be published.
/// </summary>
public sealed class Player : AggregateRoot<Guid>
{
    /// <summary>Initializes a player; use <see cref="Create"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="category">Men or women.</param>
    /// <param name="createdAt">When the player was created (UTC).</param>
    private Player(Guid id, PlayerCategory category, DateTimeOffset createdAt)
        : base(id)
    {
        Category = category;
        CreatedAt = createdAt;
    }

    /// <summary>Gets men's or women's volleyball.</summary>
    public PlayerCategory Category { get; private set; }

    /// <summary>Gets when the player was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the full name, or <see langword="null"/> while unknown.</summary>
    public FullName? Name { get; private set; }

    /// <summary>Gets the unique URL handle for the current name, or <see langword="null"/> while the name is unknown.</summary>
    public Slug? Slug { get; private set; }

    /// <summary>Gets the nationality, or <see langword="null"/> while unknown.</summary>
    public Nationality? Nationality { get; private set; }

    /// <summary>Gets the positions, or <see langword="null"/> while unknown.</summary>
    public PlayerPositions? Positions { get; private set; }

    /// <summary>Gets the Volleybox page, or <see langword="null"/> while not linked.</summary>
    public VolleyboxProfile? Volleybox { get; private set; }

    /// <summary>Gets the photo, or <see langword="null"/> while missing.</summary>
    public PlayerPhoto? Photo { get; private set; }

    /// <summary>Gets whether the player is waiting or published.</summary>
    public PlayerStatus Status { get; private set; } = PlayerStatus.Waiting;

    /// <summary>Gets when the player was first published (UTC), or <see langword="null"/> while waiting.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Gets a value indicating whether the player is published; share links may only be created for published players.</summary>
    public bool IsPublished => Status == PlayerStatus.Published;

    /// <summary>Gets a value indicating whether every detail needed for publishing is set.</summary>
    public bool IsComplete => MissingDetails().Count == 0;

    /// <summary>Creates a player with only its category and raises <see cref="PlayerCreated"/>.</summary>
    /// <param name="category">Men or women.</param>
    /// <param name="createdAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The new player.</returns>
    public static Player Create(PlayerCategory category, DateTimeOffset createdAt)
    {
        EnsureDefined(category);
        var player = new Player(Guid.CreateVersion7(createdAt), category, createdAt);
        player.RaiseDomainEvent(new PlayerCreated(player.Id));
        return player;
    }

    /// <summary>Sets the name and its slug. The caller chooses a slug that no other player uses.</summary>
    /// <param name="name">The validated name.</param>
    /// <param name="slug">The unique slug for that name.</param>
    public void Rename(FullName name, Slug slug)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(slug);
        if (name == Name && slug == Slug)
        {
            return;
        }

        Name = name;
        Slug = slug;
        DetailsChanged();
    }

    /// <summary>Sets the nationality.</summary>
    /// <param name="nationality">The validated nationality.</param>
    public void ChangeNationality(Nationality nationality)
    {
        ArgumentNullException.ThrowIfNull(nationality);
        if (nationality == Nationality)
        {
            return;
        }

        Nationality = nationality;
        DetailsChanged();
    }

    /// <summary>Sets the positions.</summary>
    /// <param name="positions">The validated positions, primary first.</param>
    public void ChangePositions(PlayerPositions positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (positions == Positions)
        {
            return;
        }

        Positions = positions;
        DetailsChanged();
    }

    /// <summary>Links the player's Volleybox page. The caller ensures no other player has the same Volleybox id.</summary>
    /// <param name="profile">The parsed Volleybox profile.</param>
    public void LinkVolleybox(VolleyboxProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile == Volleybox)
        {
            return;
        }

        Volleybox = profile;
        DetailsChanged();
    }

    /// <summary>Sets the photo.</summary>
    /// <param name="photo">The validated photo reference.</param>
    public void ChangePhoto(PlayerPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo == Photo)
        {
            return;
        }

        Photo = photo;
        DetailsChanged();
    }

    /// <summary>Lists the details still needed before the player can be published, in display order.</summary>
    /// <returns>The missing details; empty when the player is complete.</returns>
    public IReadOnlyList<PlayerDetail> MissingDetails()
    {
        var missing = new List<PlayerDetail>(5);
        if (Name is null)
        {
            missing.Add(PlayerDetail.Name);
        }

        if (Nationality is null)
        {
            missing.Add(PlayerDetail.Nationality);
        }

        if (Positions is null)
        {
            missing.Add(PlayerDetail.Positions);
        }

        if (Volleybox is null)
        {
            missing.Add(PlayerDetail.Volleybox);
        }

        if (Photo is null)
        {
            missing.Add(PlayerDetail.Photo);
        }

        return missing;
    }

    /// <summary>Publishes a complete player and raises <see cref="PlayerPublished"/>. Publishing again changes nothing.</summary>
    /// <param name="publishedAt">The current time (UTC).</param>
    /// <exception cref="DomainException"><c>player.incomplete</c> while any detail is missing.</exception>
    public void Publish(DateTimeOffset publishedAt)
    {
        if (IsPublished)
        {
            return;
        }

        var missing = MissingDetails();
        if (missing.Count > 0)
        {
            throw new DomainException("player.incomplete", $"The player cannot be published until these details are set: {string.Join(", ", missing)}.");
        }

        Status = PlayerStatus.Published;
        PublishedAt = publishedAt;
        RaiseDomainEvent(new PlayerPublished(Id));
    }

    /// <summary>Corrects the category, e.g. when the folder was filed under the wrong one.</summary>
    /// <param name="category">Men or women.</param>
    public void ChangeCategory(PlayerCategory category)
    {
        EnsureDefined(category);
        if (category == Category)
        {
            return;
        }

        Category = category;
        DetailsChanged();
    }

    /// <summary>Rejects values outside the enum.</summary>
    /// <param name="category">The category to check.</param>
    private static void EnsureDefined(PlayerCategory category)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown player category.");
        }
    }

    /// <summary>Raises <see cref="PlayerDetailsChanged"/>.</summary>
    private void DetailsChanged() => RaiseDomainEvent(new PlayerDetailsChanged(Id));
}
