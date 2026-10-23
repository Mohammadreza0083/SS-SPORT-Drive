using MediatR;
using NSubstitute;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Behaviors;

namespace SsSport.Drive.Application.UnitTests.Pipeline;

/// <summary>
/// Commands are committed by the unit of work once their handler succeeds; queries never write.
/// </summary>
public sealed class TransactionBehaviorTests
{
    /// <summary>The unit of work, recorded per test.</summary>
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    /// <summary>A successful command is saved exactly once, after its handler.</summary>
    [Fact]
    public async Task Commands_are_saved_after_the_handler()
    {
        var behavior = new TransactionBehavior<PublishTeam, Unit>(_unitOfWork);
        var handlerRan = false;

        await behavior.Handle(new PublishTeam(), _ =>
        {
            _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            handlerRan = true;
            return Unit.Task;
        }, TestContext.Current.CancellationToken);

        handlerRan.Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A failing command is not saved.</summary>
    [Fact]
    public async Task Failed_commands_are_not_saved()
    {
        var behavior = new TransactionBehavior<PublishTeam, Unit>(_unitOfWork);

        var send = () => behavior.Handle(new PublishTeam(), _ => throw new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<InvalidOperationException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Queries never trigger a save.</summary>
    [Fact]
    public async Task Queries_are_not_saved()
    {
        var behavior = new TransactionBehavior<CountTeams, int>(_unitOfWork);

        await behavior.Handle(new CountTeams(), _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
