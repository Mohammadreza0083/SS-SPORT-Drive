using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.Archive;

/// <summary>A folder that became a player.</summary>
/// <param name="FolderId">The report row.</param>
/// <param name="PlayerId">The new waiting player.</param>
public sealed record ArchiveImportedDto(Guid FolderId, Guid PlayerId);

/// <summary>A folder that was not imported, and why.</summary>
/// <param name="FolderId">The report row.</param>
/// <param name="Code">
/// <c>archive.folder_not_found</c>, <c>archive.folder_not_new</c> (imported or linked already) or <c>drive_folder.taken</c>
/// (linked to a player since the scan).
/// </param>
public sealed record ArchiveRefusedDto(Guid FolderId, string Code);

/// <summary>What an import did.</summary>
public sealed record ArchiveImportResultDto
{
    /// <summary>Gets the folders that became players.</summary>
    public IReadOnlyList<ArchiveImportedDto> Imported { get; init; } = [];

    /// <summary>Gets the folders that were refused.</summary>
    public IReadOnlyList<ArchiveRefusedDto> Refused { get; init; } = [];
}

/// <summary>
/// Imports report folders as waiting archive players (<c>POST /admin/archive/import</c>, M-2), for admins only. Each player
/// gets its category and the name and positions its folder name suggests, and its Drive folder is linked so the normal
/// sync lists the files. Nothing is published.
/// </summary>
/// <param name="FolderIds">One to <see cref="MaxFolders"/> report rows.</param>
public sealed record ImportArchiveFoldersCommand(IReadOnlyList<Guid> FolderIds) : ICommand<ArchiveImportResultDto>, IAuthorizedRequest
{
    /// <summary>The most folders one request imports.</summary>
    public const int MaxFolders = 100;

    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="ImportArchiveFoldersCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class ImportArchiveFoldersCommandValidator : AbstractValidator<ImportArchiveFoldersCommand>
{
    /// <summary>Builds the rules.</summary>
    public ImportArchiveFoldersCommandValidator() =>
        RuleFor(command => command.FolderIds)
            .NotNull()
            .Must(ids => ids.Count is > 0 and <= ImportArchiveFoldersCommand.MaxFolders).WithMessage($"Choose 1 to {ImportArchiveFoldersCommand.MaxFolders} folders.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Name each folder once.")
            .Must(ids => !ids.Contains(Guid.Empty)).WithMessage("A folder id is missing.")
            .OverridePropertyName("folderIds");
}

/// <summary>Handles <see cref="ImportArchiveFoldersCommand"/>; a refused folder does not stop the others.</summary>
/// <param name="found">The report rows.</param>
/// <param name="players">The players.</param>
/// <param name="linked">The linked Drive folders.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The admin.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ImportArchiveFoldersCommandHandler(
    IRepository<ArchiveFolder, Guid> found,
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> linked,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ImportArchiveFoldersCommand, ArchiveImportResultDto>
{
    /// <summary>Imports the folders.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was imported and what was refused.</returns>
    public async Task<ArchiveImportResultDto> Handle(ImportArchiveFoldersCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var actor = ShareLinkRules.ActorId(currentUser);
        var slugs = new UnsavedSlugs(players);
        var driveIds = new HashSet<string>(StringComparer.Ordinal);
        var imported = new List<ArchiveImportedDto>();
        var refused = new List<ArchiveRefusedDto>();
        foreach (var id in request.FolderIds)
        {
            if (await found.GetByIdAsync(id, cancellationToken) is not { } folder)
            {
                refused.Add(new ArchiveRefusedDto(id, "archive.folder_not_found"));
                continue;
            }

            if (folder.State != ArchiveFolderState.New)
            {
                refused.Add(new ArchiveRefusedDto(id, "archive.folder_not_new"));
                continue;
            }

            if (driveIds.Contains(folder.DriveFolderId) || await linked.AnyAsync(new DriveFolderByDriveIdSpec(folder.DriveFolderId), cancellationToken))
            {
                refused.Add(new ArchiveRefusedDto(id, "drive_folder.taken"));
                continue;
            }

            var player = Player.CreateFromArchive(folder.Category, now);
            if (folder.SuggestedName is { } suggested)
            {
                var name = FullName.Create(suggested);
                player.Rename(name, await slugs.TakeAsync(name, player.Id, cancellationToken));
            }

            if (folder.SuggestedPositions is { } positions)
            {
                player.ChangePositions(positions);
            }

            players.Add(player);
            linked.Add(DriveFolder.Link(player.Id, folder.DriveFolderId, folder.FolderName, now));
            driveIds.Add(folder.DriveFolderId);
            folder.MarkImported(player.Id, now);
            audit.Add(AuditEntry.Record(actor, ArchiveRules.ImportedAction, "Player", player.Id.ToString(), null, now));
            imported.Add(new ArchiveImportedDto(folder.Id, player.Id));
        }

        return new ArchiveImportResultDto { Imported = imported, Refused = refused };
    }
}

/// <summary>
/// Hands out free slugs across one request: the database query does not see players added in the same transaction, so the
/// slugs given out so far are remembered too.
/// </summary>
/// <param name="players">The players.</param>
internal sealed class UnsavedSlugs(IRepository<Player, Guid> players)
{
    /// <summary>The slugs given out in this request.</summary>
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <summary>Finds a slug for a name that no saved player and no earlier row of this request uses, and keeps it.</summary>
    /// <param name="name">The name.</param>
    /// <param name="playerId">The player who gets it (left out of the database check).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The slug.</returns>
    public async Task<Slug> TakeAsync(FullName name, Guid playerId, CancellationToken cancellationToken)
    {
        var slug = Slug.FromName(name);
        var candidate = slug;
        for (var number = 2;
            _taken.Contains(candidate.Value) || await players.AnyAsync(new PlayerBySlugSpec(candidate, playerId), cancellationToken);
            number++)
        {
            candidate = slug.WithSuffix(number);
        }

        _taken.Add(candidate.Value);
        return candidate;
    }
}
