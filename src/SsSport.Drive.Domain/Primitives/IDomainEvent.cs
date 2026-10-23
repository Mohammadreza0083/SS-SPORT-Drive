namespace SsSport.Drive.Domain.Primitives;

/// <summary>
/// Something meaningful that happened inside an aggregate (for example "player published").
/// Events are raised by aggregates and dispatched after the change is saved; the Domain layer
/// knows nothing about how they are delivered.
/// </summary>
public interface IDomainEvent;
