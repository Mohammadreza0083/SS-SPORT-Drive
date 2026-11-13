using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Users;

/// <summary>Who a signed-in Telegram user is in our team: used by sign-in to build the caller's id and role claims.</summary>
public sealed record TeamMemberDto
{
    /// <summary>Gets the user id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the role name (<c>Analyst</c> or <c>Admin</c>).</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the user is approved and not removed.</summary>
    public bool IsActive { get; init; }
}

/// <summary>The team member with a Telegram id, read untracked (sign-in never changes the user).</summary>
public sealed class TeamMemberByTelegramIdSpec : Specification<User>, ISingleResultSpecification<User>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="telegramId">The Telegram user id.</param>
    public TeamMemberByTelegramIdSpec(long telegramId) => Query.Where(user => user.TelegramId == telegramId).AsNoTracking();
}

/// <summary>Finds the team member behind a verified Telegram id (Mini App sign-in, T-3).</summary>
/// <param name="TelegramId">The Telegram user id, already verified by its signature.</param>
public sealed record GetTeamMemberByTelegramIdQuery(long TelegramId) : IQuery<TeamMemberDto?>;

/// <summary>Handles <see cref="GetTeamMemberByTelegramIdQuery"/>.</summary>
/// <param name="users">The users.</param>
public sealed class GetTeamMemberByTelegramIdQueryHandler(IRepository<User, Guid> users) : IQueryHandler<GetTeamMemberByTelegramIdQuery, TeamMemberDto?>
{
    /// <summary>Reads the user, if any.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The team member, or <see langword="null"/> when the Telegram user never asked for access.</returns>
    public async Task<TeamMemberDto?> Handle(GetTeamMemberByTelegramIdQuery request, CancellationToken cancellationToken)
    {
        var user = await users.FirstOrDefaultAsync(new TeamMemberByTelegramIdSpec(request.TelegramId), cancellationToken);
        return user?.ToTeamMemberDto();
    }
}

/// <summary>Validates <see cref="GetTeamMemberByTelegramIdQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetTeamMemberByTelegramIdQueryValidator : AbstractValidator<GetTeamMemberByTelegramIdQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetTeamMemberByTelegramIdQueryValidator()
    {
        RuleFor(r => r.TelegramId).ValidTelegramId().OverridePropertyName("telegramId");
    }
}
