using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Primitives;

/// <summary>
/// A broken domain rule is reported with a stable, machine-readable code plus a readable message.
/// </summary>
public sealed class DomainExceptionTests
{
    /// <summary>The code and message given to the exception are exposed unchanged.</summary>
    [Fact]
    public void Carries_code_and_message()
    {
        var exception = new DomainException("player.too_many_positions", "A player can have at most two positions.");

        exception.Code.Should().Be("player.too_many_positions");
        exception.Message.Should().Be("A player can have at most two positions.");
    }

    /// <summary>An empty code is a programming error and is rejected immediately.</summary>
    [Fact]
    public void Rejects_an_empty_code()
    {
        var create = () => new DomainException(" ", "message");

        create.Should().Throw<ArgumentException>();
    }
}
