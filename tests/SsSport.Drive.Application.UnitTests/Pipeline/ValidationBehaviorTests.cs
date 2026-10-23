using FluentValidation;
using SsSport.Drive.Application.Behaviors;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Application.UnitTests.Pipeline;

/// <summary>
/// Every FluentValidation validator of a request runs before its handler; any failure stops the request.
/// </summary>
public sealed class ValidationBehaviorTests
{
    /// <summary>A request without validators passes through.</summary>
    [Fact]
    public async Task Requests_without_validators_pass_through()
    {
        var behavior = new ValidationBehavior<CountTeams, int>([]);

        var result = await behavior.Handle(new CountTeams(), _ => Task.FromResult(7), TestContext.Current.CancellationToken);

        result.Should().Be(7);
    }

    /// <summary>A valid request reaches the handler.</summary>
    [Fact]
    public async Task Valid_requests_reach_the_handler()
    {
        var behavior = new ValidationBehavior<CreateTeam, Guid>([new CreateTeamValidator()]);
        var id = Guid.NewGuid();

        var result = await behavior.Handle(new CreateTeam("Lions", "coach@example.com"), _ => Task.FromResult(id), TestContext.Current.CancellationToken);

        result.Should().Be(id);
    }

    /// <summary>An invalid request fails with every error grouped by property, and the handler never runs.</summary>
    [Fact]
    public async Task Invalid_requests_fail_with_grouped_errors()
    {
        var behavior = new ValidationBehavior<CreateTeam, Guid>([new CreateTeamValidator(), new InlineValidator<CreateTeam>()]);
        var handlerRan = false;

        var send = () => behavior.Handle(
            new CreateTeam("A name that is far too long", "coach@example.com"),
            _ => { handlerRan = true; return Task.FromResult(Guid.NewGuid()); },
            TestContext.Current.CancellationToken);

        var failure = await send.Should().ThrowAsync<RequestValidationException>();
        failure.Which.Errors.Should().ContainKey(nameof(CreateTeam.Name))
            .WhoseValue.Should().ContainSingle();
        handlerRan.Should().BeFalse();
    }
}
