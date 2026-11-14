using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Application.Validation;

namespace SsSport.Drive.Application.Admin;

/// <summary>The details a waiting player can lack, as the <c>missing</c> filter writes them.</summary>
public static class WaitingDetails
{
    /// <summary>Gets the filter values, in the order the drawer lists the details.</summary>
    public static IReadOnlyList<string> Filters { get; } = ["name", "nationality", "positions", "volleybox", "photo"];
}

/// <summary>One player on the waiting list (A-5).</summary>
public sealed record WaitingPlayerRowDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the name, or <see langword="null"/> while it is missing.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the newest linked Drive folder's name, or <see langword="null"/>.</summary>
    public string? FolderName { get; init; }

    /// <summary>Gets how many videos the player has.</summary>
    public int VideoCount { get; init; }

    /// <summary>Gets how many documents the player has.</summary>
    public int DocumentCount { get; init; }

    /// <summary>Gets what is missing, in the drawer's order (<c>Name</c>, <c>Nationality</c>, <c>Positions</c>, <c>Volleybox</c>, <c>Photo</c>).</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>Gets since when the player waits (UTC).</summary>
    public DateTimeOffset WaitingSince { get; init; }
}

/// <summary>The tiles above the waiting list.</summary>
public sealed record WaitingSummaryDto
{
    /// <summary>Gets how many players wait.</summary>
    public int Waiting { get; init; }

    /// <summary>Gets how many waiting players have no Volleybox link.</summary>
    public int NoVolleybox { get; init; }

    /// <summary>Gets how many waiting players have no photo.</summary>
    public int NoPhoto { get; init; }

    /// <summary>Gets how many players were published today (Tehran).</summary>
    public int PublishedToday { get; init; }
}

/// <summary>Reads the waiting list with SQL projections (a recorded exception to "conditions in specifications", decisions §2).</summary>
public interface IWaitingReader
{
    /// <summary>Reads one page of waiting players, oldest first.</summary>
    /// <param name="missing">A detail filter from <see cref="WaitingDetails.Filters"/>, or <see langword="null"/>.</param>
    /// <param name="request">The page.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<WaitingPlayerRowDto>> PageAsync(string? missing, PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Counts the tiles.</summary>
    /// <param name="todayFrom">The start of today in Tehran (UTC).</param>
    /// <param name="todayTo">The start of tomorrow in Tehran (UTC).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The tiles.</returns>
    Task<WaitingSummaryDto> SummaryAsync(DateTimeOffset todayFrom, DateTimeOffset todayTo, CancellationToken cancellationToken = default);
}

/// <summary>Reads the waiting list (<c>GET /admin/waiting</c>), for admins only.</summary>
/// <param name="Paging">The page.</param>
/// <param name="Missing">A detail filter.</param>
public sealed record GetWaitingPlayersQuery(PageRequest Paging, string? Missing) : IPagedQuery<WaitingPlayerRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetWaitingPlayersQuery"/> on the server (decisions §1b rule 3); an unknown detail is a 400.</summary>
public sealed class GetWaitingPlayersQueryValidator : AbstractValidator<GetWaitingPlayersQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetWaitingPlayersQueryValidator()
    {
        RuleFor(query => query.Paging).ValidPaging();
        RuleFor(query => query.Missing)
            .Must(missing => WaitingDetails.Filters.Contains(missing!, StringComparer.OrdinalIgnoreCase))
            .When(query => !string.IsNullOrEmpty(query.Missing))
            .WithMessage("Use name, nationality, positions, volleybox or photo.")
            .OverridePropertyName("missing");
    }
}

/// <summary>Handles <see cref="GetWaitingPlayersQuery"/>.</summary>
/// <param name="reader">Reads the list.</param>
public sealed class GetWaitingPlayersQueryHandler(IWaitingReader reader) : IQueryHandler<GetWaitingPlayersQuery, PagedResult<WaitingPlayerRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    public Task<PagedResult<WaitingPlayerRowDto>> Handle(GetWaitingPlayersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return reader.PageAsync(string.IsNullOrEmpty(request.Missing) ? null : request.Missing.ToLowerInvariant(), request.Paging, cancellationToken);
    }
}

/// <summary>Reads the waiting list's tiles (<c>GET /admin/waiting/summary</c>), for admins only.</summary>
public sealed record GetWaitingSummaryQuery : IQuery<WaitingSummaryDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetWaitingSummaryQuery"/>; it carries no input.</summary>
public sealed class GetWaitingSummaryQueryValidator : AbstractValidator<GetWaitingSummaryQuery>;

/// <summary>Handles <see cref="GetWaitingSummaryQuery"/>.</summary>
/// <param name="reader">Reads the tiles.</param>
/// <param name="settings">The notification rules, for the Tehran time zone.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetWaitingSummaryQueryHandler(IWaitingReader reader, INotificationSettings settings, TimeProvider timeProvider) : IQueryHandler<GetWaitingSummaryQuery, WaitingSummaryDto>
{
    /// <summary>Counts the tiles.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The tiles.</returns>
    public Task<WaitingSummaryDto> Handle(GetWaitingSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var window = DashboardPeriods.WindowFor("7d", timeProvider.GetUtcNow(), settings.Rules.TimeZone);
        return reader.SummaryAsync(window.To.AddDays(-1), window.To, cancellationToken);
    }
}
