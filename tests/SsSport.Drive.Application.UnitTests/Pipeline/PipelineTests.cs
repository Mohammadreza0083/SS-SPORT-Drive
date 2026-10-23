using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Application.UnitTests.Pipeline;

/// <summary>Handles <see cref="CreateTeam"/> and counts its calls.</summary>
public sealed class CreateTeamHandler : ICommandHandler<CreateTeam, Guid>
{
    /// <summary>Gets how many times the handler ran.</summary>
    public int Calls { get; private set; }

    /// <summary>Returns a new id.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A new team id.</returns>
    public Task<Guid> Handle(CreateTeam request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Guid.NewGuid());
    }
}

/// <summary>Handles <see cref="PublishTeam"/>.</summary>
public sealed class PublishTeamHandler : ICommandHandler<PublishTeam>
{
    /// <summary>Does nothing.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task Handle(PublishTeam request, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Handles <see cref="CountTeams"/>.</summary>
public sealed class CountTeamsHandler : IQueryHandler<CountTeams, int>
{
    /// <summary>Returns a fixed count.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Two.</returns>
    public Task<int> Handle(CountTeams request, CancellationToken cancellationToken) => Task.FromResult(2);
}

/// <summary>
/// The real MediatR pipeline from <c>AddApplication</c>: logging, then authorization, then validation,
/// then the transaction around the handler.
/// </summary>
public sealed class PipelineTests : IDisposable
{
    /// <summary>The unit of work the transaction behaviour commits through.</summary>
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    /// <summary>The caller.</summary>
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    /// <summary>The handler instance used by the container, so tests can count its calls.</summary>
    private readonly CreateTeamHandler _createTeam = new();

    /// <summary>The container under test.</summary>
    private readonly ServiceProvider _services;

    /// <summary>Builds the container the way the API host does, plus this assembly's handlers and validators.</summary>
    public PipelineTests()
    {
        var services = new ServiceCollection().AddLogging().AddApplication();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<PipelineTests>());
        services.AddValidatorsFromAssemblyContaining<PipelineTests>();
        services.AddSingleton<IRequestHandler<CreateTeam, Guid>>(_createTeam);
        services.AddSingleton(_unitOfWork);
        services.Replace(ServiceDescriptor.Singleton(_user));
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A valid command runs its handler and is committed once.</summary>
    [Fact]
    public async Task Valid_command_is_handled_and_committed()
    {
        using var scope = _services.CreateScope();

        var id = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateTeam("Lions", "coach@example.com"), TestContext.Current.CancellationToken);

        id.Should().NotBeEmpty();
        _createTeam.Calls.Should().Be(1);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>An invalid command never reaches its handler and nothing is committed.</summary>
    [Fact]
    public async Task Invalid_command_is_rejected_before_the_handler()
    {
        using var scope = _services.CreateScope();

        var send = () => scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateTeam(string.Empty, "coach@example.com"), TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<RequestValidationException>();
        _createTeam.Calls.Should().Be(0);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Authorization runs before validation, so anonymous callers learn nothing about validation rules.</summary>
    [Fact]
    public async Task Authorization_runs_before_the_handler_and_the_commit()
    {
        _user.IsAuthenticated.Returns(false);
        using var scope = _services.CreateScope();

        var send = () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new PublishTeam(), TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<UnauthenticatedException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A query is answered without committing anything.</summary>
    [Fact]
    public async Task Query_is_answered_without_a_commit()
    {
        using var scope = _services.CreateScope();

        var count = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CountTeams(), TestContext.Current.CancellationToken);

        count.Should().Be(2);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Disposes the container.</summary>
    public void Dispose() => _services.Dispose();
}
