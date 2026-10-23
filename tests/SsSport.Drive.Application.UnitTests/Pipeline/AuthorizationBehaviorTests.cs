using MediatR;
using NSubstitute;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Behaviors;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Application.UnitTests.Pipeline;

/// <summary>
/// Requests marked <c>IAuthorizedRequest</c> need a signed-in user with one of the required roles;
/// other requests pass straight through.
/// </summary>
public sealed class AuthorizationBehaviorTests
{
    /// <summary>The current user, set up per test.</summary>
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    /// <summary>Requests that do not ask for authorization are not checked.</summary>
    [Fact]
    public async Task Unprotected_requests_pass_through()
    {
        _user.IsAuthenticated.Returns(false);
        var behavior = new AuthorizationBehavior<CountTeams, int>(_user);

        var result = await behavior.Handle(new CountTeams(), _ => Task.FromResult(3), TestContext.Current.CancellationToken);

        result.Should().Be(3);
    }

    /// <summary>An anonymous caller is rejected before the handler runs.</summary>
    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        _user.IsAuthenticated.Returns(false);
        var handlerRan = false;
        var behavior = new AuthorizationBehavior<PublishTeam, Unit>(_user);

        var send = () => behavior.Handle(new PublishTeam(), _ => { handlerRan = true; return Unit.Task; }, TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<UnauthenticatedException>();
        handlerRan.Should().BeFalse();
    }

    /// <summary>A signed-in user without a required role is forbidden before the handler runs.</summary>
    [Fact]
    public async Task Users_without_a_required_role_are_forbidden()
    {
        _user.IsAuthenticated.Returns(true);
        _user.IsInRole("Admin").Returns(false);
        var handlerRan = false;
        var behavior = new AuthorizationBehavior<PublishTeam, Unit>(_user);

        var send = () => behavior.Handle(new PublishTeam(), _ => { handlerRan = true; return Unit.Task; }, TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<ForbiddenAccessException>();
        handlerRan.Should().BeFalse();
    }

    /// <summary>A user with a required role reaches the handler.</summary>
    [Fact]
    public async Task Users_with_a_required_role_pass()
    {
        _user.IsAuthenticated.Returns(true);
        _user.IsInRole("Admin").Returns(true);
        var behavior = new AuthorizationBehavior<PublishTeam, Unit>(_user);

        var result = await behavior.Handle(new PublishTeam(), _ => Unit.Task, TestContext.Current.CancellationToken);

        result.Should().Be(Unit.Value);
    }
}
