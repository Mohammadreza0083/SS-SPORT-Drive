using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;

namespace SsSport.Drive.Application.UnitTests.Pipeline;

/// <summary>A command with a result and a personal field, used to prove the pipeline never logs request data.</summary>
/// <param name="Name">The team name; validated.</param>
/// <param name="ContactEmail">Personal data that must never reach the logs.</param>
public sealed record CreateTeam(string Name, string ContactEmail) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateTeam"/>.</summary>
public sealed class CreateTeamValidator : AbstractValidator<CreateTeam>
{
    /// <summary>Requires a name of at most 10 characters.</summary>
    public CreateTeamValidator() => RuleFor(command => command.Name).NotEmpty().MaximumLength(10);
}

/// <summary>A command without a result that only admins may send.</summary>
public sealed record PublishTeam : ICommand, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed to publish: admins only.</summary>
    public IReadOnlyCollection<string> RequiredRoles { get; } = ["Admin"];
}

/// <summary>A read-only query.</summary>
public sealed record CountTeams : IQuery<int>;
