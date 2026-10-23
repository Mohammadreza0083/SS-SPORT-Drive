namespace SsSport.Drive.Domain.Primitives;

/// <summary>
/// Non-generic view of an aggregate's pending domain events, so persistence code can collect them
/// from every aggregate regardless of its identifier type.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Gets the pending domain events, in the order they were raised.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Removes every pending event; called once the events have been dispatched.</summary>
    void ClearDomainEvents();
}
