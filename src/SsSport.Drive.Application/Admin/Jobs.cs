using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Emails;

namespace SsSport.Drive.Application.Admin;

/// <summary>The states the jobs list can be filtered by, and the audit action of an email retry (A-9).</summary>
public static class JobRules
{
    /// <summary>The audit action of retrying a failed email.</summary>
    public const string EmailRetriedAction = "email.retried";

    /// <summary>Gets the states of files that are not ready, as the filter writes them.</summary>
    public static IReadOnlyList<string> StateFilters { get; } = ["queued", "copying", "processing", "failed"];
}

/// <summary>One file that is not ready yet.</summary>
public sealed record JobRowDto
{
    /// <summary>Gets the file id.</summary>
    public Guid AssetId { get; init; }

    /// <summary>Gets the player id.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the player's name.</summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>Gets the file's title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the original file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Gets <c>Video</c>, <c>Document</c> or <c>Scout</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Gets the processing state.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Gets why processing failed, or <see langword="null"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Gets when the file was added.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The tiles of the Jobs and email queue page.</summary>
public sealed record JobsSummaryDto
{
    /// <summary>Gets the files waiting for a worker.</summary>
    public int Queued { get; init; }

    /// <summary>Gets the files being copied from Drive.</summary>
    public int Copying { get; init; }

    /// <summary>Gets the files being transcoded or rendered.</summary>
    public int Processing { get; init; }

    /// <summary>Gets the files whose processing failed.</summary>
    public int Failed { get; init; }

    /// <summary>Gets how many Drive folders are linked.</summary>
    public int Folders { get; init; }

    /// <summary>Gets how many folders lost access.</summary>
    public int LostFolders { get; init; }

    /// <summary>Gets when a folder was last synced, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastDriveSyncAt { get; init; }

    /// <summary>Gets the emails sent in the last 24 hours.</summary>
    public int EmailsSentLast24Hours { get; init; }

    /// <summary>Gets the emails that used up every attempt.</summary>
    public int EmailsFailed { get; init; }

    /// <summary>Gets the emails still on the queue.</summary>
    public int EmailsQueued { get; init; }
}

/// <summary>Counts the jobs page's tiles with aggregate queries.</summary>
public interface IJobsReader
{
    /// <summary>Counts the tiles.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The tiles.</returns>
    Task<JobsSummaryDto> SummaryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>The files that are not ready, optionally in one state; newest first.</summary>
public sealed class UnfinishedAssetsSpec : Specification<Asset>
{
    /// <summary>Selects the files.</summary>
    /// <param name="state">One state, or <see langword="null"/> for every unfinished one.</param>
    public UnfinishedAssetsSpec(ProcessingState? state)
    {
        if (state is { } value)
        {
            Query.Where(asset => asset.State == value);
        }
        else
        {
            Query.Where(asset => asset.State != ProcessingState.Ready);
        }

        Query.OrderByDescending(asset => asset.CreatedAt);
    }
}

/// <summary>Reads the files still being ingested or failed (<c>GET /admin/jobs</c>), for admins only.</summary>
/// <param name="Paging">The page.</param>
/// <param name="State">A state filter, or <see langword="null"/>.</param>
public sealed record GetJobsQuery(PageRequest Paging, string? State) : IPagedQuery<JobRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetJobsQuery"/> on the server (decisions §1b rule 3); a state that is not in progress is a 400.</summary>
public sealed class GetJobsQueryValidator : AbstractValidator<GetJobsQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetJobsQueryValidator()
    {
        RuleFor(query => query.Paging).ValidPaging();
        RuleFor(query => query.State)
            .Must(state => JobRules.StateFilters.Contains(state!, StringComparer.OrdinalIgnoreCase))
            .When(query => !string.IsNullOrEmpty(query.State))
            .WithMessage("Use queued, copying, processing or failed.")
            .OverridePropertyName("state");
    }
}

/// <summary>Handles <see cref="GetJobsQuery"/>.</summary>
/// <param name="pages">The paged reader.</param>
/// <param name="lookup">Reads the players' names for the page.</param>
public sealed class GetJobsQueryHandler(IPagedReader pages, IAdminLookup lookup) : IQueryHandler<GetJobsQuery, PagedResult<JobRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<PagedResult<JobRowDto>> Handle(GetJobsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProcessingState? state = string.IsNullOrEmpty(request.State) ? null : Enum.Parse<ProcessingState>(request.State, ignoreCase: true);
        var page = await pages.PageAsync(new UnfinishedAssetsSpec(state), request.Paging, asset => asset, cancellationToken);
        var names = await lookup.PlayerNamesAsync([.. page.Items.Select(asset => asset.PlayerId).Distinct()], cancellationToken);
        return PagedResult<JobRowDto>.Create(
            [.. page.Items.Select(asset => new JobRowDto
            {
                AssetId = asset.Id,
                PlayerId = asset.PlayerId,
                PlayerName = names.GetValueOrDefault(asset.PlayerId, string.Empty),
                Title = asset.Title,
                FileName = asset.File.OriginalName,
                Kind = asset.Kind.ToString(),
                State = asset.State.ToString(),
                FailureReason = asset.FailureReason,
                CreatedAt = asset.CreatedAt,
            })],
            request.Paging,
            page.TotalCount);
    }
}

/// <summary>Counts the jobs page's tiles (<c>GET /admin/jobs/summary</c>), for admins only.</summary>
public sealed record GetJobsSummaryQuery : IQuery<JobsSummaryDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetJobsSummaryQuery"/>; it carries no input.</summary>
public sealed class GetJobsSummaryQueryValidator : AbstractValidator<GetJobsSummaryQuery>;

/// <summary>Handles <see cref="GetJobsSummaryQuery"/>.</summary>
/// <param name="reader">Counts the tiles.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetJobsSummaryQueryHandler(IJobsReader reader, TimeProvider timeProvider) : IQueryHandler<GetJobsSummaryQuery, JobsSummaryDto>
{
    /// <summary>Counts the tiles.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The tiles.</returns>
    public Task<JobsSummaryDto> Handle(GetJobsSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return reader.SummaryAsync(timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>Sends a failed email again (<c>POST /admin/emails/{id}/retry</c>), for admins only.</summary>
/// <param name="EmailId">The email.</param>
public sealed record RetryEmailCommand(Guid EmailId) : ICommand, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="RetryEmailCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class RetryEmailCommandValidator : AbstractValidator<RetryEmailCommand>
{
    /// <summary>Builds the rules.</summary>
    public RetryEmailCommandValidator() => RuleFor(command => command.EmailId).ValidId().OverridePropertyName("emailId");
}

/// <summary>Handles <see cref="RetryEmailCommand"/>: the email goes back on the queue and its send event runs after the save.</summary>
/// <param name="emails">The queued emails.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class RetryEmailCommandHandler(IRepository<OutgoingEmail, Guid> emails, IRepository<AuditEntry, Guid> audit, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<RetryEmailCommand>
{
    /// <summary>Requeues the email.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the email is requeued.</returns>
    /// <exception cref="NotFoundException">There is no such email.</exception>
    /// <exception cref="SsSport.Drive.Domain.Primitives.DomainException"><c>email.not_failed</c> when it did not fail.</exception>
    public async Task Handle(RetryEmailCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var email = await emails.GetByIdAsync(request.EmailId, cancellationToken) ?? throw new NotFoundException("Email");
        var now = timeProvider.GetUtcNow();
        email.Retry(now);
        audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), JobRules.EmailRetriedAction, nameof(OutgoingEmail), email.Id.ToString(), null, now));
    }
}
