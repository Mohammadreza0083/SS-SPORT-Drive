using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.DriveFolders;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Players;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.MiniApp;

/// <summary>
/// A player card in update mode (T-5): every detail, who owns it, its latest folders, and what the caller may change.
/// </summary>
public sealed record MiniAppPlayerDto : PlayerDetailsDto
{
    /// <summary>Initializes the card from the player details.</summary>
    /// <param name="details">The player details.</param>
    public MiniAppPlayerDto(PlayerDetailsDto details)
        : base(details)
    {
    }

    /// <summary>Gets the owning analyst display name, or <see langword="null"/> for archive players.</summary>
    public string? AnalystName { get; init; }

    /// <summary>Gets a value indicating whether the player has a photo.</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Gets how many Drive folders are linked.</summary>
    public int FolderCount { get; init; }

    /// <summary>Gets the latest linked folders, newest first (at most <see cref="GetMiniAppPlayerQuery.FolderLimit"/>).</summary>
    public IReadOnlyList<DriveFolderDto> Folders { get; init; } = [];

    /// <summary>Gets a value indicating whether the caller may edit the details.</summary>
    public bool CanEditDetails { get; init; }

    /// <summary>Gets a value indicating whether the caller may change the photo.</summary>
    public bool CanChangePhoto { get; init; }

    /// <summary>Gets a value indicating whether the caller may edit the files' titles, tags and hero (T-7b).</summary>
    public bool CanEditFiles { get; init; }

    /// <summary>Gets a value indicating whether the caller may hide and show files: admins only (T-7b).</summary>
    public bool CanHideFiles { get; init; }
}

/// <summary>The form that adds a Drive folder to an existing player.</summary>
public sealed record AddFolderDto
{
    /// <summary>Gets the Google Drive folder link (or bare id).</summary>
    public string? DriveFolderUrl { get; init; }
}

/// <summary>The details to change; a field left out (or <see langword="null"/>) stays as it is.</summary>
public sealed record UpdatePlayerDto
{
    /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the full name in Latin letters.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the two-letter country code.</summary>
    public string? NationalityCode { get; init; }

    /// <summary>Gets one or two position codes, primary first.</summary>
    public IReadOnlyList<string>? Positions { get; init; }

    /// <summary>Gets the Volleybox player page URL.</summary>
    public string? VolleyboxUrl { get; init; }

    /// <summary>Turns the form into its command.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The command.</returns>
    public UpdatePlayerDetailsCommand ToCommand(Guid playerId) => new(playerId, Category, Name, NationalityCode, Positions, VolleyboxUrl);
}

/// <summary>A player linked folders, newest first.</summary>
public sealed class PlayerFoldersSpec : Specification<DriveFolder>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="take">How many folders to read.</param>
    public PlayerFoldersSpec(Guid playerId, int take) =>
        Query.Where(folder => folder.PlayerId == playerId).OrderByDescending(folder => folder.LinkedAt).ThenBy(folder => folder.Id).Take(take).AsNoTracking();
}

/// <summary>Reads a player card for update mode (<c>GET /miniapp/players/{id}</c>).</summary>
/// <param name="PlayerId">The player.</param>
public sealed record GetMiniAppPlayerQuery(Guid PlayerId) : IQuery<MiniAppPlayerDto>, IAuthorizedRequest
{
    /// <summary>The most folders the card lists; <see cref="MiniAppPlayerDto.FolderCount"/> has the total.</summary>
    public const int FolderLimit = 10;

    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Handles <see cref="GetMiniAppPlayerQuery"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="folders">The linked folders.</param>
/// <param name="users">The team, for the owner name.</param>
/// <param name="currentUser">The caller.</param>
public sealed class GetMiniAppPlayerQueryHandler(
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> folders,
    IRepository<User, Guid> users,
    ICurrentUser currentUser) : IQueryHandler<GetMiniAppPlayerQuery, MiniAppPlayerDto>
{
    /// <summary>Reads the player, its owner and latest folders, and works out what the caller may change.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The card.</returns>
    /// <exception cref="NotFoundException">The player does not exist.</exception>
    public async Task<MiniAppPlayerDto> Handle(GetMiniAppPlayerQuery request, CancellationToken cancellationToken)
    {
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken) ?? throw new NotFoundException("Player");
        var analyst = player.AnalystId is { } analystId ? await users.FirstOrDefaultAsync(new UserByIdReadOnlySpec(analystId), cancellationToken) : null;
        var spec = new PlayerFoldersSpec(player.Id, GetMiniAppPlayerQuery.FolderLimit);
        return new MiniAppPlayerDto(player.ToDetailsDto())
        {
            AnalystName = analyst?.DisplayName,
            HasPhoto = player.Photo is not null,
            FolderCount = await folders.CountAsync(spec, cancellationToken),
            Folders = [.. (await folders.ListAsync(spec, cancellationToken)).Select(folder => folder.ToDto())],
            CanEditDetails = PlayerAccess.CanEditDetails(player, currentUser),
            CanChangePhoto = PlayerAccess.CanChangePhoto(player, currentUser),
            CanEditFiles = PlayerAccess.CanEditFiles(player, currentUser),
            CanHideFiles = PlayerAccess.CanHideFiles(currentUser),
        };
    }
}

/// <summary>A team member by id, read untracked.</summary>
public sealed class UserByIdReadOnlySpec : Specification<User>, ISingleResultSpecification<User>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="id">The user id.</param>
    public UserByIdReadOnlySpec(Guid id) => Query.Where(user => user.Id == id).AsNoTracking();
}

/// <summary>
/// Adds a Drive folder to an existing player (<c>POST /miniapp/players/{id}/folders</c>, T-5). Any team member may;
/// the folder is added, never a replacement, after the same checks as a new player folder.
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="DriveFolderUrl">The Drive folder link.</param>
public sealed record AddPlayerFolderCommand(Guid PlayerId, string? DriveFolderUrl) : ICommand<DriveFolderDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Handles <see cref="AddPlayerFolderCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="folders">The linked folders.</param>
/// <param name="drive">Reads the folder with the service account.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class AddPlayerFolderCommandHandler(
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> folders,
    IDriveFolderReader drive,
    TimeProvider timeProvider) : ICommandHandler<AddPlayerFolderCommand, DriveFolderDto>
{
    /// <summary>Checks the player and the folder, then links the folder under its Drive name.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The linked folder.</returns>
    /// <exception cref="NotFoundException">The player does not exist.</exception>
    /// <exception cref="Domain.Primitives.DomainException">The link or the folder cannot be used.</exception>
    /// <exception cref="ServiceUnavailableException">Google Drive cannot be reached.</exception>
    public async Task<DriveFolderDto> Handle(AddPlayerFolderCommand request, CancellationToken cancellationToken)
    {
        if (!await players.AnyAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken))
        {
            throw new NotFoundException("Player");
        }

        var link = DriveFolderLink.Create(request.DriveFolderUrl);
        await DriveFolderChecks.EnsureFreeAsync(folders, link.FolderId, cancellationToken);
        var summary = await DriveFolderChecks.ReadUsableAsync(drive, link.FolderId, cancellationToken);
        var folder = DriveFolder.Link(request.PlayerId, link.FolderId, summary.Name!, timeProvider.GetUtcNow());
        folders.Add(folder);
        return folder.ToDto();
    }
}

/// <summary>
/// Edits a player details (<c>PATCH /miniapp/players/{id}</c>, T-5): the owning analyst while the player waits, an
/// admin always. Fields left out stay as they are.
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Category">Men or Women, or <see langword="null"/>.</param>
/// <param name="Name">The full name, or <see langword="null"/>.</param>
/// <param name="NationalityCode">The country code, or <see langword="null"/>.</param>
/// <param name="Positions">One or two position codes, or <see langword="null"/>.</param>
/// <param name="VolleyboxUrl">The Volleybox player page, or <see langword="null"/>.</param>
public sealed record UpdatePlayerDetailsCommand(
    Guid PlayerId,
    string? Category,
    string? Name,
    string? NationalityCode,
    IReadOnlyList<string>? Positions,
    string? VolleyboxUrl) : ICommand<PlayerDetailsDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member (who may edit is checked by the handler).</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Fields that are sent must be readable; the business rules are the Domain ones.</summary>
public sealed class UpdatePlayerDetailsCommandValidator : AbstractValidator<UpdatePlayerDetailsCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpdatePlayerDetailsCommandValidator()
    {
        RuleFor(command => command.Category)
            .Must(category => Enum.TryParse<PlayerCategory>(category, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            .When(command => command.Category is not null)
            .WithMessage("Choose Men or Women.")
            .OverridePropertyName("category");
        RuleFor(command => command.Positions)
            .Must(positions => positions!.Count is > 0 and <= PlayerPositions.MaxCount).WithMessage("Choose one or two positions.")
            .Must(positions => positions!.All(code => PositionParser.TryParse(code, out _))).WithMessage("Use S, OH, OPP, MB, L or DS.")
            .When(command => command.Positions is not null)
            .OverridePropertyName("positions");
    }
}

/// <summary>Handles <see cref="UpdatePlayerDetailsCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="currentUser">The caller.</param>
public sealed class UpdatePlayerDetailsCommandHandler(IRepository<Player, Guid> players, ICurrentUser currentUser)
    : ICommandHandler<UpdatePlayerDetailsCommand, PlayerDetailsDto>
{
    /// <summary>Checks who may edit, then applies the sent fields with the same rules as a new player.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The player after the edit.</returns>
    /// <exception cref="NotFoundException">The player does not exist.</exception>
    /// <exception cref="ForbiddenAccessException">Another analyst, or the owner of a published player.</exception>
    /// <exception cref="Domain.Primitives.DomainException">A field breaks a rule, or the Volleybox id belongs to another player.</exception>
    public async Task<PlayerDetailsDto> Handle(UpdatePlayerDetailsCommand request, CancellationToken cancellationToken)
    {
        var player = await players.GetByIdAsync(request.PlayerId, cancellationToken) ?? throw new NotFoundException("Player");
        PlayerAccess.EnsureCanEditDetails(player, currentUser);

        if (request.Category is not null)
        {
            player.ChangeCategory(Enum.Parse<PlayerCategory>(request.Category, ignoreCase: true));
        }

        if (request.Name is not null)
        {
            var name = FullName.Create(request.Name);
            player.Rename(name, await PlayerUniqueness.FreeSlugAsync(players, name, player.Id, cancellationToken));
        }

        if (request.NationalityCode is not null)
        {
            player.ChangeNationality(Nationality.Create(request.NationalityCode));
        }

        if (request.Positions is not null)
        {
            player.ChangePositions(PlayerPositions.Create(request.Positions.Select(code => PositionParser.TryParse(code, out var position) ? position : default)));
        }

        if (request.VolleyboxUrl is not null)
        {
            var volleybox = VolleyboxProfile.Create(request.VolleyboxUrl);
            await PlayerUniqueness.EnsureVolleyboxFreeAsync(players, volleybox, player.Id, cancellationToken);
            player.LinkVolleybox(volleybox);
        }

        return player.ToDetailsDto();
    }
}

/// <summary>
/// Tells the owning analyst when someone else adds a Drive folder to their player (T-5). Runs after the save; the
/// owner adding a folder, archive players without an analyst and system changes notify nobody.
/// </summary>
/// <param name="folders">The linked folders.</param>
/// <param name="players">The players.</param>
/// <param name="users">The team.</param>
/// <param name="currentUser">Who added the folder.</param>
/// <param name="bot">The messenger.</param>
public sealed class NotifyOwnerOfAddedFolder(
    IRepository<DriveFolder, Guid> folders,
    IRepository<Player, Guid> players,
    IRepository<User, Guid> users,
    ICurrentUser currentUser,
    ITelegramMessenger bot) : IDomainEventHandler<DriveFolderLinked>
{
    /// <summary>Sends the notice to the owner when the folder was added by someone else.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the owner has been told, or nobody needed to be.</returns>
    public async Task HandleAsync(DriveFolderLinked domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (!Guid.TryParse(currentUser.UserId, out var actorId)
            || await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(domainEvent.PlayerId), cancellationToken) is not { AnalystId: { } ownerId } player
            || ownerId == actorId
            || await users.FirstOrDefaultAsync(new UserByIdReadOnlySpec(ownerId), cancellationToken) is not { IsActive: true } owner
            || await folders.FirstOrDefaultAsync(new FolderByIdReadOnlySpec(domainEvent.FolderId), cancellationToken) is not { } folder)
        {
            return;
        }

        var actor = await users.FirstOrDefaultAsync(new UserByIdReadOnlySpec(actorId), cancellationToken);
        var text = BotTexts.FormatFolderAddedByOther(player.Name?.Value ?? player.Id.ToString(), actor?.DisplayName ?? string.Empty, folder.Name);
        await bot.SendAsync(owner.TelegramId, text, null, cancellationToken);
    }
}

/// <summary>A linked folder by id, read untracked.</summary>
public sealed class FolderByIdReadOnlySpec : Specification<DriveFolder>, ISingleResultSpecification<DriveFolder>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="id">The folder id.</param>
    public FolderByIdReadOnlySpec(Guid id) => Query.Where(folder => folder.Id == id).AsNoTracking();
}
