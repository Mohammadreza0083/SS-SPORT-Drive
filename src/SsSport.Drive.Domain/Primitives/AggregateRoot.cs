namespace SsSport.Drive.Domain.Primitives;

/// <summary>
/// Base class for aggregate roots: the entry point of a consistency boundary, and the only kind of
/// entity that repositories load and save. It collects the domain events it raises until they are dispatched.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    /// <summary>The events raised since the aggregate was loaded or last cleared.</summary>
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Initializes the aggregate with its identifier.</summary>
    /// <param name="id">The identifier; it never changes afterwards.</param>
    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>Gets the pending domain events, in the order they were raised. The collection cannot be modified.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Removes every pending event; called once the events have been dispatched.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Records a domain event to be dispatched after the aggregate is saved.</summary>
    /// <param name="domainEvent">The event that happened.</param>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
