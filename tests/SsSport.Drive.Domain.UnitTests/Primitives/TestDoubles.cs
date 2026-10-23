using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Primitives;

/// <summary>A minimal entity used to test <see cref="Entity{TId}"/> equality.</summary>
/// <param name="id">The entity identifier.</param>
internal sealed class Ball(Guid id) : Entity<Guid>(id);

/// <summary>A second entity type, used to prove that equality also compares the type.</summary>
/// <param name="id">The entity identifier.</param>
internal sealed class Net(Guid id) : Entity<Guid>(id);

/// <summary>Raised by <see cref="Match"/> when a point is scored.</summary>
/// <param name="MatchId">The match the point belongs to.</param>
internal sealed record PointScored(Guid MatchId) : IDomainEvent;

/// <summary>A minimal aggregate used to test domain event handling on <see cref="AggregateRoot{TId}"/>.</summary>
/// <param name="id">The aggregate identifier.</param>
internal sealed class Match(Guid id) : AggregateRoot<Guid>(id)
{
    /// <summary>Records a point and raises <see cref="PointScored"/>.</summary>
    public void ScorePoint() => RaiseDomainEvent(new PointScored(Id));
}
