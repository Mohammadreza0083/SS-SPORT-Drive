using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Images;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.MiniApp;

/// <summary>A player's photo after an upload.</summary>
public sealed record PlayerPhotoDto
{
    /// <summary>Gets the storage key of the WebP the player now points at.</summary>
    public string PhotoKey { get; init; } = string.Empty;

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Gets a value indicating whether every detail is now set, so an admin can publish.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Gets the details still missing (<c>Name</c>, <c>Nationality</c>, <c>Positions</c>, <c>Volleybox</c>, <c>Photo</c>).</summary>
    public IReadOnlyList<string> MissingDetails { get; init; } = [];
}

/// <summary>
/// Who may change a player from the Mini App (owner, 2026-10-29): the photo by the owning analyst or any admin; the
/// details by the owning analyst while the player waits, and by an admin always. Anyone in the team may add a folder.
/// </summary>
internal static class PlayerAccess
{
    /// <summary>Tells whether the caller may change the photo: the owning analyst or an admin.</summary>
    /// <param name="player">The player.</param>
    /// <param name="currentUser">The caller.</param>
    /// <returns><see langword="true"/> when they may.</returns>
    public static bool CanChangePhoto(Player player, ICurrentUser currentUser) =>
        currentUser.IsInRole(Role.Admin.Name) || IsOwner(player, currentUser);

    /// <summary>Tells whether a known team member may change the photo (the bot, which has no signed-in caller).</summary>
    /// <param name="player">The player.</param>
    /// <param name="user">The team member.</param>
    /// <returns><see langword="true"/> for the owning analyst or an active admin.</returns>
    public static bool CanChangePhoto(Player player, User user) => user.IsAdmin || player.AnalystId == user.Id;

    /// <summary>Tells whether the caller may edit the details: an admin, or the owning analyst while the player waits.</summary>
    /// <param name="player">The player.</param>
    /// <param name="currentUser">The caller.</param>
    /// <returns><see langword="true"/> when they may.</returns>
    public static bool CanEditDetails(Player player, ICurrentUser currentUser) =>
        currentUser.IsInRole(Role.Admin.Name) || (IsOwner(player, currentUser) && player.Status == PlayerStatus.Waiting);

    /// <summary>Tells whether the caller may edit a player's files (title, tags, hero): the owning analyst or an admin.</summary>
    /// <param name="player">The player.</param>
    /// <param name="currentUser">The caller.</param>
    /// <returns><see langword="true"/> when they may.</returns>
    public static bool CanEditFiles(Player player, ICurrentUser currentUser) => CanChangePhoto(player, currentUser);

    /// <summary>Tells whether the caller may hide and show files: admins only (owner, 2026-10-30).</summary>
    /// <param name="currentUser">The caller.</param>
    /// <returns><see langword="true"/> for an admin.</returns>
    public static bool CanHideFiles(ICurrentUser currentUser) => currentUser.IsInRole(Role.Admin.Name);

    /// <summary>Refuses the caller unless they own the player or are an admin.</summary>
    /// <param name="player">The player.</param>
    /// <param name="currentUser">The caller.</param>
    /// <exception cref="ForbiddenAccessException">The caller is another analyst.</exception>
    public static void EnsureCanManage(Player player, ICurrentUser currentUser)
    {
        if (!CanChangePhoto(player, currentUser))
        {
            throw new ForbiddenAccessException();
        }
    }

    /// <summary>Refuses the caller unless they may edit the details.</summary>
    /// <param name="player">The player.</param>
    /// <param name="currentUser">The caller.</param>
    /// <exception cref="ForbiddenAccessException">Another analyst, or the owner of a published player.</exception>
    public static void EnsureCanEditDetails(Player player, ICurrentUser currentUser)
    {
        if (!CanEditDetails(player, currentUser))
        {
            throw new ForbiddenAccessException();
        }
    }

    /// <summary>Tells whether the caller is the player's analyst.</summary>
    /// <param name="player">The player.</param>
    /// <param name="currentUser">The caller.</param>
    /// <returns><see langword="true"/> for the owning analyst.</returns>
    private static bool IsOwner(Player player, ICurrentUser currentUser) =>
        Guid.TryParse(currentUser.UserId, out var userId) && player.AnalystId == userId;
}

/// <summary>A player by id, read untracked.</summary>
public sealed class PlayerByIdReadOnlySpec : Specification<Player>, ISingleResultSpecification<Player>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="id">The player id.</param>
    public PlayerByIdReadOnlySpec(Guid id) => Query.Where(player => player.Id == id).AsNoTracking();
}

/// <summary>
/// Uploads a player's photo (<c>PUT /miniapp/players/{id}/photo</c>, T-4b). The original and the processed WebP are
/// stored under new, versioned keys; the player then points at the WebP.
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Content">The uploaded image bytes (at most <see cref="MaxBytes"/>).</param>
public sealed record UploadPlayerPhotoCommand(Guid PlayerId, byte[] Content) : ICommand<PlayerPhotoDto>, IAuthorizedRequest
{
    /// <summary>The largest accepted upload: 15 MB.</summary>
    public const int MaxBytes = 15 * 1024 * 1024;

    /// <summary>Gets the roles allowed: every active team member (ownership is checked by the handler).</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>The upload must not be empty or larger than 15 MB.</summary>
public sealed class UploadPlayerPhotoCommandValidator : AbstractValidator<UploadPlayerPhotoCommand>
{
    /// <summary>Builds the rules.</summary>
    public UploadPlayerPhotoCommandValidator() =>
        RuleFor(command => command.Content)
            .Must(content => content is { Length: > 0 and <= UploadPlayerPhotoCommand.MaxBytes })
            .WithMessage("Send one JPEG, PNG, WebP or HEIC photo of at most 15 MB.")
            .OverridePropertyName("photo");
}

/// <summary>Handles <see cref="UploadPlayerPhotoCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="processor">Checks and converts the photo.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="timeProvider">The clock; its milliseconds version the keys.</param>
public sealed class UploadPlayerPhotoCommandHandler(
    IRepository<Player, Guid> players,
    ICurrentUser currentUser,
    IPhotoProcessor processor,
    IObjectStorage storage,
    TimeProvider timeProvider) : ICommandHandler<UploadPlayerPhotoCommand, PlayerPhotoDto>
{
    /// <summary>Checks access, processes and stores the photo, and points the player at it.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The photo and what the player still misses.</returns>
    /// <exception cref="NotFoundException">The player does not exist.</exception>
    /// <exception cref="ForbiddenAccessException">The caller neither owns the player nor is an admin.</exception>
    public async Task<PlayerPhotoDto> Handle(UploadPlayerPhotoCommand request, CancellationToken cancellationToken)
    {
        var player = await players.GetByIdAsync(request.PlayerId, cancellationToken) ?? throw new NotFoundException("Player");
        PlayerAccess.EnsureCanManage(player, currentUser);
        return await PlayerPhotos.SaveAsync(player, request.Content, processor, storage, timeProvider, cancellationToken);
    }
}

/// <summary>Stores a player photo; shared by the Mini App upload (T-4b) and the bot chat (T-4d), so both follow one path.</summary>
internal static class PlayerPhotos
{
    /// <summary>Processes the photo, stores the original and the WebP under new versioned keys, and points the player at the WebP.</summary>
    /// <param name="player">The tracked player.</param>
    /// <param name="content">The uploaded bytes.</param>
    /// <param name="processor">Checks and converts the photo.</param>
    /// <param name="storage">The media bucket.</param>
    /// <param name="timeProvider">The clock; its milliseconds version the keys.</param>
    /// <param name="cancellationToken">Cancels the uploads.</param>
    /// <returns>The photo and what the player still misses.</returns>
    /// <exception cref="Domain.Primitives.DomainException"><c>player.photo_too_small</c> or <c>player.photo_unreadable</c>.</exception>
    public static async Task<PlayerPhotoDto> SaveAsync(
        Player player,
        byte[] content,
        IPhotoProcessor processor,
        IObjectStorage storage,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var photo = processor.Process(new MemoryStream(content, writable: false));
        var version = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var key = $"players/{player.Id}/photo-{version}";
        await storage.PutAsync($"{key}-original", new MemoryStream(content, writable: false), photo.OriginalContentType, cancellationToken);
        await storage.PutAsync($"{key}.webp", new MemoryStream(photo.Webp, writable: false), "image/webp", cancellationToken);

        player.ChangePhoto(PlayerPhoto.Create($"{key}.webp"));
        return new PlayerPhotoDto
        {
            PhotoKey = player.Photo!.StorageKey,
            Width = photo.Width,
            Height = photo.Height,
            IsComplete = player.IsComplete,
            MissingDetails = [.. player.MissingDetails().Select(detail => detail.ToString())],
        };
    }
}

/// <summary>Reads a player's current photo (<c>GET /miniapp/players/{id}/photo</c>).</summary>
/// <param name="PlayerId">The player.</param>
public sealed record GetPlayerPhotoQuery(Guid PlayerId) : IQuery<StoredObject>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member (ownership is checked by the handler).</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Handles <see cref="GetPlayerPhotoQuery"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="storage">The media bucket.</param>
public sealed class GetPlayerPhotoQueryHandler(IRepository<Player, Guid> players, ICurrentUser currentUser, IObjectStorage storage)
    : IQueryHandler<GetPlayerPhotoQuery, StoredObject>
{
    /// <summary>Checks access and reads the WebP the player points at.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The photo.</returns>
    /// <exception cref="NotFoundException">The player does not exist or has no photo yet.</exception>
    /// <exception cref="ForbiddenAccessException">The caller neither owns the player nor is an admin.</exception>
    public async Task<StoredObject> Handle(GetPlayerPhotoQuery request, CancellationToken cancellationToken)
    {
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(request.PlayerId), cancellationToken) ?? throw new NotFoundException("Player");
        PlayerAccess.EnsureCanManage(player, currentUser);
        if (player.Photo is null)
        {
            throw new NotFoundException("Photo");
        }

        return await storage.GetAsync(player.Photo.StorageKey, cancellationToken) ?? throw new NotFoundException("Photo");
    }
}
