using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Primitives;

/// <summary>
/// Aggregates collect the domain events they raise until the persistence layer dispatches and clears them.
/// </summary>
public sealed class AggregateRootTests
{
    /// <summary>A new aggregate has no pending events.</summary>
    [Fact]
    public void Starts_without_events()
    {
        new Match(Guid.NewGuid()).DomainEvents.Should().BeEmpty();
    }

    /// <summary>Raised events are kept in the order they were raised.</summary>
    [Fact]
    public void Keeps_raised_events_in_order()
    {
        var match = new Match(Guid.NewGuid());

        match.ScorePoint();
        match.ScorePoint();

        match.DomainEvents.Should().HaveCount(2)
            .And.AllBeEquivalentTo(new PointScored(match.Id));
    }

    /// <summary>Clearing removes every pending event.</summary>
    [Fact]
    public void Clear_removes_all_events()
    {
        var match = new Match(Guid.NewGuid());
        match.ScorePoint();

        match.ClearDomainEvents();

        match.DomainEvents.Should().BeEmpty();
    }

    /// <summary>Callers cannot add or remove events behind the aggregate's back, even by casting the collection.</summary>
    [Fact]
    public void Exposed_events_cannot_be_modified()
    {
        var match = new Match(Guid.NewGuid());
        match.ScorePoint();

        var asCollection = match.DomainEvents as ICollection<IDomainEvent>;
        var add = () => asCollection?.Add(new PointScored(match.Id));
        var clear = () => asCollection?.Clear();

        add.Should().Throw<NotSupportedException>();
        clear.Should().Throw<NotSupportedException>();
        match.DomainEvents.Should().ContainSingle();
    }
}
