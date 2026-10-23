namespace SsSport.Drive.Domain.UnitTests.Primitives;

/// <summary>
/// Entities are equal when they are the same type and carry the same identifier.
/// </summary>
public sealed class EntityTests
{
    /// <summary>Two instances of the same type with the same id are equal, by method and by operator.</summary>
    [Fact]
    public void Same_type_and_id_are_equal()
    {
        var id = Guid.NewGuid();
        var first = new Ball(id);
        var second = new Ball(id);

        first.Equals(second).Should().BeTrue();
        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    /// <summary>Two instances with different ids are not equal.</summary>
    [Fact]
    public void Different_ids_are_not_equal()
    {
        var first = new Ball(Guid.NewGuid());
        var second = new Ball(Guid.NewGuid());

        first.Equals(second).Should().BeFalse();
        (first == second).Should().BeFalse();
        (first != second).Should().BeTrue();
    }

    /// <summary>Entities of different types are never equal, even with the same id.</summary>
    [Fact]
    public void Different_types_with_the_same_id_are_not_equal()
    {
        var id = Guid.NewGuid();

        new Ball(id).Equals(new Net(id)).Should().BeFalse();
    }

    /// <summary>An entity is never equal to null, and two null references compare equal.</summary>
    [Fact]
    public void Null_handling_is_safe()
    {
        var ball = new Ball(Guid.NewGuid());
        Ball? none = null;

        ball.Equals(null).Should().BeFalse();
        (ball == none).Should().BeFalse();
        (none == ball).Should().BeFalse();
        (none == null).Should().BeTrue();
    }

    /// <summary>The identifier passed to the constructor is exposed unchanged.</summary>
    [Fact]
    public void Exposes_its_id()
    {
        var id = Guid.NewGuid();

        new Ball(id).Id.Should().Be(id);
    }
}
