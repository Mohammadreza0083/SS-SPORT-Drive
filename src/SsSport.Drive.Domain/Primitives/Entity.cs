namespace SsSport.Drive.Domain.Primitives;

/// <summary>
/// Base class for domain entities: objects with an identity that stays the same while their data changes.
/// Two entities are equal when they are the same concrete type and have the same <see cref="Id"/>.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>Initializes the entity with its identifier.</summary>
    /// <param name="id">The identifier; it never changes afterwards.</param>
    protected Entity(TId id) => Id = id;

    /// <summary>Gets the identifier of the entity.</summary>
    public TId Id { get; }

    /// <summary>Compares two entities by type and identifier.</summary>
    /// <param name="left">The first entity, or null.</param>
    /// <param name="right">The second entity, or null.</param>
    /// <returns><see langword="true"/> when both are null, or both have the same type and identifier.</returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    /// <summary>Compares two entities by type and identifier.</summary>
    /// <param name="left">The first entity, or null.</param>
    /// <param name="right">The second entity, or null.</param>
    /// <returns><see langword="true"/> when the entities are not equal.</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);

    /// <summary>Checks whether another entity has the same type and identifier.</summary>
    /// <param name="other">The entity to compare with.</param>
    /// <returns><see langword="true"/> when the entities are equal.</returns>
    public bool Equals(Entity<TId>? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id)));

    /// <summary>Checks whether an object is an entity with the same type and identifier.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the objects are equal.</returns>
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <summary>Builds a hash code from the concrete type and the identifier.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
