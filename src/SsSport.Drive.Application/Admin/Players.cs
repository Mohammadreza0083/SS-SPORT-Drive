using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Assets;
using SsSport.Drive.Application.DriveFolders;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Admin;

/// <summary>The console statuses of a player (A-3).</summary>
public static class AdminPlayerStatus
{
    /// <summary>Not published yet.</summary>
    public const string Waiting = "Waiting";

    /// <summary>Published with a failed file or a lost Drive folder.</summary>
    public const string Attention = "Attention";

    /// <summary>Published with files still being processed.</summary>
    public const string Processing = "Processing";

    /// <summary>Published and everything is fine.</summary>
    public const string Live = "Live";

    /// <summary>Gets the statuses the list can be filtered by, lower case as the address writes them.</summary>
    public static IReadOnlyList<string> Filters { get; } = ["live", "processing", "attention", "waiting"];

    /// <summary>Gets the position codes the list can be filtered by.</summary>
    public static IReadOnlyList<string> PositionCodes { get; } = ["S", "OH", "OPP", "MB", "L", "DS"];

    /// <summary>The audit action of publishing a player.</summary>
    public const string PublishedAction = "player.published";

    /// <summary>The audit action of moving a file.</summary>
    public const string MovedAction = "asset.moved";

    /// <summary>The audit action of retrying a failed file.</summary>
    public const string RetriedAction = "asset.retried";

    /// <summary>The audit target type of a player.</summary>
    public const string PlayerTarget = nameof(Player);
}

/// <summary>One row of the console's players list.</summary>
public sealed record AdminPlayerRowDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the name, or <see langword="null"/> while it is missing.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the position codes.</summary>
    public IReadOnlyList<string> Positions { get; init; } = [];

    /// <summary>Gets the country code, or <see langword="null"/>.</summary>
    public string? NationalityCode { get; init; }

    /// <summary>Gets the country name, or <see langword="null"/>.</summary>
    public string? NationalityName { get; init; }

    /// <summary>Gets how many videos the player has.</summary>
    public int VideoCount { get; init; }

    /// <summary>Gets how many documents (PDFs and images) the player has.</summary>
    public int DocumentCount { get; init; }

    /// <summary>Gets how many different emails watched the player.</summary>
    public int Viewers { get; init; }

    /// <summary>Gets when the player was last watched, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastViewedAt { get; init; }

    /// <summary>Gets the responsible analyst's name, or <see langword="null"/>.</summary>
    public string? AnalystName { get; init; }

    /// <summary>Gets the console status: <c>Live</c>, <c>Processing</c>, <c>Attention</c> or <c>Waiting</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether a photo is stored.</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Gets the Volleybox player id, or <see langword="null"/>.</summary>
    public int? VolleyboxId { get; init; }

    /// <summary>Gets when the player was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>What the players list is filtered by.</summary>
/// <param name="Search">Part of a name or a Volleybox id, or <see langword="null"/>.</param>
/// <param name="Position">A position code, or <see langword="null"/>.</param>
/// <param name="Status">A lower-case console status, or <see langword="null"/>.</param>
public sealed record AdminPlayerCriteria(string? Search, string? Position, string? Status);

/// <summary>Facts about one player that need their own queries.</summary>
/// <param name="ViewersWithAccess">Distinct emails that can open or watched the player.</param>
/// <param name="AverageCompletion">The average share of a video watched (0 to 1), or <see langword="null"/>.</param>
public sealed record AdminPlayerStats(int ViewersWithAccess, double? AverageCompletion);

/// <summary>Reads the console's players with one SQL projection (a recorded exception to "conditions in specifications", decisions §2).</summary>
public interface IAdminPlayerReader
{
    /// <summary>Reads one page of players, newest first.</summary>
    /// <param name="criteria">The filters.</param>
    /// <param name="request">The page.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<AdminPlayerRowDto>> PageAsync(AdminPlayerCriteria criteria, PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads one player's row.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The row, or <see langword="null"/> when there is no such player.</returns>
    Task<AdminPlayerRowDto?> GetRowAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Reads the viewers and the completion of one player.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The facts.</returns>
    Task<AdminPlayerStats> GetStatsAsync(Guid playerId, CancellationToken cancellationToken = default);
}

/// <summary>Reads the players list (<c>GET /admin/players</c>), for admins only.</summary>
/// <param name="Paging">The page.</param>
/// <param name="Search">Part of a name or a Volleybox id.</param>
/// <param name="Position">A position code.</param>
/// <param name="Status">A console status.</param>
public sealed record GetAdminPlayersQuery(PageRequest Paging, string? Search, string? Position, string? Status) : IPagedQuery<AdminPlayerRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetAdminPlayersQuery"/> on the server (decisions §1b rule 3); unknown filters are a 400.</summary>
public sealed class GetAdminPlayersQueryValidator : AbstractValidator<GetAdminPlayersQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAdminPlayersQueryValidator()
    {
        RuleFor(query => query.Paging).ValidPaging();
        RuleFor(query => query.Search).MaximumLength(100).OverridePropertyName("q");
        RuleFor(query => query.Position)
            .Must(code => AdminPlayerStatus.PositionCodes.Contains(code!, StringComparer.OrdinalIgnoreCase))
            .When(query => !string.IsNullOrEmpty(query.Position))
            .WithMessage("Use S, OH, OPP, MB, L or DS.")
            .OverridePropertyName("position");
        RuleFor(query => query.Status)
            .Must(status => AdminPlayerStatus.Filters.Contains(status!, StringComparer.OrdinalIgnoreCase))
            .When(query => !string.IsNullOrEmpty(query.Status))
            .WithMessage("Use live, processing, attention or waiting.")
            .OverridePropertyName("status");
    }
}

/// <summary>Handles <see cref="GetAdminPlayersQuery"/>.</summary>
/// <param name="reader">Reads the players.</param>
public sealed class GetAdminPlayersQueryHandler(IAdminPlayerReader reader) : IQueryHandler<GetAdminPlayersQuery, PagedResult<AdminPlayerRowDto>>
{
    /// <summary>Reads one page.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    public Task<PagedResult<AdminPlayerRowDto>> Handle(GetAdminPlayersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var position = string.IsNullOrEmpty(request.Position) ? null : request.Position.ToUpperInvariant();
        var status = string.IsNullOrEmpty(request.Status) ? null : request.Status.ToLowerInvariant();
        return reader.PageAsync(new AdminPlayerCriteria(search, position, status), request.Paging, cancellationToken);
    }
}

/// <summary>A nationality as the console shows it.</summary>
/// <param name="Code">The ISO country code.</param>
/// <param name="Name">The country name.</param>
public sealed record AdminNationalityDto(string Code, string Name);

/// <summary>One player in full, for the console's player page and the waiting list's drawer.</summary>
public sealed record AdminPlayerDto
{
    /// <summary>Gets the player id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the name, or <see langword="null"/>.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the slug of the public page, or <see langword="null"/>.</summary>
    public string? Slug { get; init; }

    /// <summary>Gets the position codes.</summary>
    public IReadOnlyList<string> Positions { get; init; } = [];

    /// <summary>Gets the nationality, or <see langword="null"/>.</summary>
    public AdminNationalityDto? Nationality { get; init; }

    /// <summary>Gets the Volleybox player id, or <see langword="null"/>.</summary>
    public int? VolleyboxId { get; init; }

    /// <summary>Gets the Volleybox profile link, or <see langword="null"/>.</summary>
    public string? VolleyboxUrl { get; init; }

    /// <summary>Gets a value indicating whether a photo is stored.</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Gets the responsible analyst's id, or <see langword="null"/>.</summary>
    public Guid? AnalystId { get; init; }

    /// <summary>Gets the responsible analyst's name, or <see langword="null"/>.</summary>
    public string? AnalystName { get; init; }

    /// <summary>Gets when the player was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when the player was published, or <see langword="null"/>.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Gets the console status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the details still missing, in the drawer's order.</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>Gets the hero reel's asset id, or <see langword="null"/>.</summary>
    public Guid? HeroAssetId { get; init; }

    /// <summary>Gets how many emails can open or watched the player.</summary>
    public int ViewersWithAccess { get; init; }

    /// <summary>Gets the average share of a video watched (0 to 1), or <see langword="null"/>.</summary>
    public double? AverageCompletion { get; init; }

    /// <summary>Gets the linked Drive folders, newest first (at most <see cref="GetAdminPlayerQuery.FolderLimit"/>).</summary>
    public IReadOnlyList<DriveFolderDto> Folders { get; init; } = [];
}

/// <summary>Reads one player (<c>GET /admin/players/{id}</c>), for admins only.</summary>
/// <param name="PlayerId">The player.</param>
public sealed record GetAdminPlayerQuery(Guid PlayerId) : IQuery<AdminPlayerDto>, IAuthorizedRequest
{
    /// <summary>How many folders the page lists.</summary>
    public const int FolderLimit = 20;

    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetAdminPlayerQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetAdminPlayerQueryValidator : AbstractValidator<GetAdminPlayerQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAdminPlayerQueryValidator() => RuleFor(query => query.PlayerId).ValidId().OverridePropertyName("playerId");
}

/// <summary>Handles <see cref="GetAdminPlayerQuery"/>.</summary>
/// <param name="reader">Reads the row and the stats.</param>
/// <param name="players">The players.</param>
/// <param name="folders">The Drive folders.</param>
public sealed class GetAdminPlayerQueryHandler(IAdminPlayerReader reader, IRepository<Player, Guid> players, IRepository<DriveFolder, Guid> folders) : IQueryHandler<GetAdminPlayerQuery, AdminPlayerDto>
{
    /// <summary>Reads the player.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The player.</returns>
    /// <exception cref="NotFoundException">There is no such player.</exception>
    public async Task<AdminPlayerDto> Handle(GetAdminPlayerQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = await reader.GetRowAsync(request.PlayerId, cancellationToken) ?? throw new NotFoundException("Player");
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken) ?? throw new NotFoundException("Player");
        var stats = await reader.GetStatsAsync(request.PlayerId, cancellationToken);
        var linked = await folders.ListAsync(new PlayerFoldersSpec(request.PlayerId, GetAdminPlayerQuery.FolderLimit), cancellationToken);
        return new AdminPlayerDto
        {
            Id = player.Id,
            Name = player.Name?.Value,
            Slug = player.Slug?.Value,
            Positions = row.Positions,
            Nationality = player.Nationality is { } nationality ? new AdminNationalityDto(nationality.Code, nationality.CountryName) : null,
            VolleyboxId = player.Volleybox?.PlayerId,
            VolleyboxUrl = player.Volleybox?.Url,
            HasPhoto = player.Photo is not null,
            AnalystId = player.AnalystId,
            AnalystName = row.AnalystName,
            CreatedAt = player.CreatedAt,
            PublishedAt = player.PublishedAt,
            Status = row.Status,
            Missing = [.. player.MissingDetails().Select(detail => detail.ToString())],
            HeroAssetId = player.HeroAssetId,
            ViewersWithAccess = stats.ViewersWithAccess,
            AverageCompletion = stats.AverageCompletion,
            Folders = [.. linked.Select(folder => new DriveFolderDto
            {
                Id = folder.Id,
                PlayerId = folder.PlayerId,
                DriveFolderId = folder.DriveFolderId,
                Name = folder.Name,
                Access = folder.Access.ToString(),
                LinkedAt = folder.LinkedAt,
                LastSyncedAt = folder.LastSyncedAt,
                AccessLostAt = folder.AccessLostAt,
            })],
        };
    }
}

/// <summary>One file on the console's files tab.</summary>
public sealed record AdminPlayerFileDto
{
    /// <summary>Gets the file id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets <c>Video</c>, <c>Document</c> or <c>Scout</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Gets the title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the original file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Gets the tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets the place on the page, starting at 1.</summary>
    public int SortOrder { get; init; }

    /// <summary>Gets <c>Visible</c> or <c>Hidden</c>.</summary>
    public string Visibility { get; init; } = string.Empty;

    /// <summary>Gets the processing state.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Gets why processing failed, or <see langword="null"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Gets where the file came from: <c>Drive</c> or <c>Backup</c>.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the file left its Drive folder.</summary>
    public bool RemovedFromSource { get; init; }

    /// <summary>Gets a video's length in seconds, or <see langword="null"/>.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Gets a document's page count, or <see langword="null"/>.</summary>
    public int? PageCount { get; init; }

    /// <summary>Gets a value indicating whether this is the hero reel.</summary>
    public bool IsHero { get; init; }
}

/// <summary>Reads a player's files in page order (<c>GET /admin/players/{id}/files</c>), for admins only.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Paging">The page.</param>
public sealed record GetAdminPlayerFilesQuery(Guid PlayerId, PageRequest Paging) : IPagedQuery<AdminPlayerFileDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetAdminPlayerFilesQuery"/> on the server (decisions §1b rule 3).</summary>
public sealed class GetAdminPlayerFilesQueryValidator : AbstractValidator<GetAdminPlayerFilesQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetAdminPlayerFilesQueryValidator()
    {
        RuleFor(query => query.PlayerId).ValidId().OverridePropertyName("playerId");
        RuleFor(query => query.Paging).ValidPaging();
    }
}

/// <summary>Handles <see cref="GetAdminPlayerFilesQuery"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="pages">The paged reader.</param>
public sealed class GetAdminPlayerFilesQueryHandler(IRepository<Player, Guid> players, IPagedReader pages) : IQueryHandler<GetAdminPlayerFilesQuery, PagedResult<AdminPlayerFileDto>>
{
    /// <summary>Reads one page of files.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    /// <exception cref="NotFoundException">There is no such player.</exception>
    public async Task<PagedResult<AdminPlayerFileDto>> Handle(GetAdminPlayerFilesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken) ?? throw new NotFoundException("Player");
        return await pages.PageAsync(
            new PlayerFilesInPageOrderSpec(player.Id),
            request.Paging,
            asset =>
            {
                var file = asset.ToFileDto();
                return new AdminPlayerFileDto
                {
                    Id = asset.Id,
                    Kind = file.Kind,
                    Title = file.Title,
                    FileName = asset.File.OriginalName,
                    Tags = file.Tags,
                    SortOrder = file.SortOrder,
                    Visibility = file.Visibility,
                    State = file.State,
                    FailureReason = asset.FailureReason,
                    Source = asset.Source.ToString(),
                    RemovedFromSource = asset.RemovedFromSourceAt is not null,
                    DurationSeconds = file.DurationSeconds,
                    PageCount = file.PageCount,
                    IsHero = asset.Id == player.HeroAssetId,
                };
            },
            cancellationToken);
    }
}

/// <summary>The body of the move call.</summary>
public sealed record MoveFileDto
{
    /// <summary>Gets the new place on the page, starting at 1.</summary>
    public int? Position { get; init; }
}

/// <summary>Moves a file to a place on the page; the others shift (<c>POST /admin/players/{id}/files/{fileId}/move</c>), for admins only.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="FileId">The file.</param>
/// <param name="Position">The new place, starting at 1.</param>
public sealed record MoveFileCommand(Guid PlayerId, Guid FileId, int? Position) : ICommand, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="MoveFileCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class MoveFileCommandValidator : AbstractValidator<MoveFileCommand>
{
    /// <summary>Builds the rules.</summary>
    public MoveFileCommandValidator()
    {
        RuleFor(command => command.PlayerId).ValidId().OverridePropertyName("playerId");
        RuleFor(command => command.FileId).ValidId().OverridePropertyName("fileId");
        RuleFor(command => command.Position).Must(position => position is >= 1).WithMessage("Use a place from 1.").OverridePropertyName("position");
    }
}

/// <summary>The files of one player, in page order, to renumber.</summary>
public sealed class PlayerFilesToReorderSpec : Specification<Asset>
{
    /// <summary>Selects the files.</summary>
    /// <param name="playerId">The player.</param>
    public PlayerFilesToReorderSpec(Guid playerId) => Query.Where(asset => asset.PlayerId == playerId).OrderBy(asset => asset.SortOrder).ThenBy(asset => asset.Id);
}

/// <summary>Handles <see cref="MoveFileCommand"/>.</summary>
/// <param name="assets">The files.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class MoveFileCommandHandler(IRepository<Asset, Guid> assets, IRepository<AuditEntry, Guid> audit, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<MoveFileCommand>
{
    /// <summary>Moves the file and renumbers the page 1..n.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the order is changed.</returns>
    /// <exception cref="NotFoundException">The file is not this player's.</exception>
    /// <exception cref="RequestValidationException">The place is beyond the last file.</exception>
    public async Task Handle(MoveFileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var files = (await assets.ListAsync(new PlayerFilesToReorderSpec(request.PlayerId), cancellationToken)).ToList();
        var file = files.Find(item => item.Id == request.FileId) ?? throw new NotFoundException("File");
        var position = request.Position!.Value;
        if (position > files.Count)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["position"] = [$"Use a place from 1 to {files.Count}."] });
        }

        files.Remove(file);
        files.Insert(position - 1, file);
        for (var index = 0; index < files.Count; index++)
        {
            if (files[index].SortOrder != index + 1)
            {
                files[index].MoveTo(index + 1);
            }
        }

        audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), AdminPlayerStatus.MovedAction, nameof(Asset), file.Id.ToString(), null, timeProvider.GetUtcNow()));
    }
}

/// <summary>Puts a failed file back in the queue (<c>POST /admin/files/{id}/retry</c>), for admins only.</summary>
/// <param name="FileId">The file.</param>
public sealed record RetryFileCommand(Guid FileId) : ICommand, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="RetryFileCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class RetryFileCommandValidator : AbstractValidator<RetryFileCommand>
{
    /// <summary>Builds the rules.</summary>
    public RetryFileCommandValidator() => RuleFor(command => command.FileId).ValidId().OverridePropertyName("fileId");
}

/// <summary>Handles <see cref="RetryFileCommand"/>.</summary>
/// <param name="assets">The files.</param>
/// <param name="players">The players, for the file's lane (M-4).</param>
/// <param name="scheduler">Queues the ingest steps.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class RetryFileCommandHandler(IRepository<Asset, Guid> assets, IRepository<Player, Guid> players, IIngestScheduler scheduler, IRepository<AuditEntry, Guid> audit, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<RetryFileCommand>
{
    /// <summary>Resets the file and queues the copy again.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the file is queued.</returns>
    /// <exception cref="NotFoundException">There is no such file.</exception>
    /// <exception cref="SsSport.Drive.Domain.Primitives.DomainException"><c>asset.invalid_state</c> when the file did not fail.</exception>
    public async Task Handle(RetryFileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var asset = await assets.GetByIdAsync(request.FileId, cancellationToken) ?? throw new NotFoundException("File");
        asset.Retry();
        audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), AdminPlayerStatus.RetriedAction, nameof(Asset), asset.Id.ToString(), null, timeProvider.GetUtcNow()));
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(asset.PlayerId), cancellationToken);
        await scheduler.EnqueueAssetAsync(IngestStep.CopyOriginal, asset.Id, player is null ? IngestLane.Live : IngestLanes.For(player), cancellationToken);
    }
}

/// <summary>Publishes a complete player (<c>POST /admin/players/{id}/publish</c>), for admins only.</summary>
/// <param name="PlayerId">The player.</param>
public sealed record PublishPlayerCommand(Guid PlayerId) : ICommand, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="PublishPlayerCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class PublishPlayerCommandValidator : AbstractValidator<PublishPlayerCommand>
{
    /// <summary>Builds the rules.</summary>
    public PublishPlayerCommandValidator() => RuleFor(command => command.PlayerId).ValidId().OverridePropertyName("playerId");
}

/// <summary>Handles <see cref="PublishPlayerCommand"/>; publishing twice changes and audits nothing the second time.</summary>
/// <param name="players">The players.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class PublishPlayerCommandHandler(IRepository<Player, Guid> players, IRepository<AuditEntry, Guid> audit, ICurrentUser currentUser, TimeProvider timeProvider) : ICommandHandler<PublishPlayerCommand>
{
    /// <summary>Publishes the player.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the player is published.</returns>
    /// <exception cref="NotFoundException">There is no such player.</exception>
    /// <exception cref="SsSport.Drive.Domain.Primitives.DomainException"><c>player.incomplete</c> while details are missing.</exception>
    public async Task Handle(PublishPlayerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var player = await players.GetByIdAsync(request.PlayerId, cancellationToken) ?? throw new NotFoundException("Player");
        if (player.IsPublished)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        player.Publish(now);
        audit.Add(AuditEntry.Record(ShareLinkRules.ActorId(currentUser), AdminPlayerStatus.PublishedAction, AdminPlayerStatus.PlayerTarget, player.Id.ToString(), null, now));
    }
}
