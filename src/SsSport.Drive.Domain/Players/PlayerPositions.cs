using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>A player's one or two distinct positions, in order; the first is primary.</summary>
public sealed record PlayerPositions
{
    /// <summary>The most positions a player can have.</summary>
    public const int MaxCount = 2;

    /// <summary>The positions, in order.</summary>
    private readonly Position[] _items;

    /// <summary>Initializes the set with already validated positions.</summary>
    /// <param name="items">The positions, in order.</param>
    private PlayerPositions(Position[] items) => _items = items;

    /// <summary>Gets the positions, in order.</summary>
    public IReadOnlyList<Position> Items => _items;

    /// <summary>Gets the primary position.</summary>
    public Position Primary => _items[0];

    /// <summary>Gets the secondary position, or <see langword="null"/> when the player has only one.</summary>
    public Position? Secondary => _items.Length > 1 ? _items[1] : null;

    /// <summary>Validates a list of positions.</summary>
    /// <param name="positions">The positions, primary first.</param>
    /// <returns>The validated positions.</returns>
    /// <exception cref="DomainException"><c>player.positions_required</c>, <c>player.too_many_positions</c> or <c>player.duplicate_position</c>.</exception>
    public static PlayerPositions Create(IEnumerable<Position> positions)
    {
        var items = positions.ToArray();

        if (items.Length == 0)
        {
            throw new DomainException("player.positions_required", "A player needs at least one position.");
        }

        if (items.Length > MaxCount)
        {
            throw new DomainException("player.too_many_positions", $"A player can have at most {MaxCount} positions.");
        }

        if (items.Distinct().Count() != items.Length)
        {
            throw new DomainException("player.duplicate_position", "A position can only be listed once.");
        }

        if (items.Any(position => !Enum.IsDefined(position)))
        {
            throw new ArgumentOutOfRangeException(nameof(positions), "Unknown position.");
        }

        return new PlayerPositions(items);
    }

    /// <summary>Compares the positions and their order.</summary>
    /// <param name="other">The other set.</param>
    /// <returns><see langword="true"/> when both list the same positions in the same order.</returns>
    public bool Equals(PlayerPositions? other) => other is not null && _items.SequenceEqual(other._items);

    /// <summary>Builds a hash code from the positions in order.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => _items.Aggregate(0, HashCode.Combine);

    /// <summary>Returns the codes, primary first (e.g. <c>OH/OPP</c>).</summary>
    /// <returns>The codes joined by a slash.</returns>
    public override string ToString() => string.Join('/', _items.Select(position => position.Code()));
}
