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

/// <summary>Raised when a player is created.</summary>
/// <param name="PlayerId">The new player.</param>
public sealed record PlayerCreated(Guid PlayerId) : IDomainEvent;

/// <summary>Raised when any of a player's details changes.</summary>
/// <param name="PlayerId">The changed player.</param>
public sealed record PlayerDetailsChanged(Guid PlayerId) : IDomainEvent;

/// <summary>
/// A volleyball player represented by SS-SPORT. A player starts with only a category and may stay incomplete
/// (the admin waiting list); completeness and publishing are added in backlog D-5.
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
