using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Assets;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.MiniApp;

/// <summary>The edit-video sheet's changes; a field left out (or <see langword="null"/>) stays as it is.</summary>
public sealed record EditPlayerFileDto
{
    /// <summary>Gets the new title (1 to 200 characters).</summary>
    public string? Title { get; init; }

    /// <summary>Gets every tag the file should have (e.g. <c>Highlight</c>, <c>Spike</c>); empty clears them.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Gets <see langword="true"/> to make the file the hero, <see langword="false"/> to stop it being the hero.</summary>
    public bool? IsHero { get; init; }

    /// <summary>Gets <see langword="true"/> to hide the file from viewers, <see langword="false"/> to show it (admins only).</summary>
    public bool? Hidden { get; init; }

    /// <summary>Turns the sheet into its command.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fileId">The file.</param>
    /// <returns>The command.</returns>
    public EditPlayerFileCommand ToCommand(Guid playerId, Guid fileId) => new(playerId, fileId, Title, Tags, IsHero, Hidden);
}

/// <summary>
/// Edits one file of a player from the edit-video sheet (<c>PATCH /miniapp/players/{id}/files/{fileId}</c>, T-7b): its
/// title, tags and whether it is the hero, for the owning analyst or an admin; hiding and showing it, for admins only.
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="FileId">The file.</param>
/// <param name="Title">The new title, or <see langword="null"/>.</param>
/// <param name="Tags">Every tag the file should have, or <see langword="null"/>.</param>
/// <param name="IsHero">Whether the file is the hero, or <see langword="null"/>.</param>
/// <param name="Hidden">Whether the file is hidden, or <see langword="null"/>.</param>
public sealed record EditPlayerFileCommand(
    Guid PlayerId,
    Guid FileId,
    string? Title,
    IReadOnlyList<string>? Tags,
    bool? IsHero,
    bool? Hidden) : ICommand<PlayerFileDto>, IAuthorizedRequest
{
    /// <summary>The audit action for hiding a file.</summary>
    public const string HiddenAction = "asset.hidden";

    /// <summary>The audit action for showing a hidden file again.</summary>
    public const string ShownAction = "asset.shown";

    /// <summary>Gets the roles allowed: every active team member (who may edit is checked by the handler).</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;

    /// <summary>Reads one tag by name, case-insensitively; numbers and <c>Untagged</c> are not tags.</summary>
    /// <param name="name">The name sent.</param>
    /// <param name="tag">The tag.</param>
    /// <returns><see langword="true"/> when the name is a single tag.</returns>
    public static bool TryParseTag(string? name, out AssetTags tag)
    {
        tag = AssetTags.Untagged;
        return !string.IsNullOrWhiteSpace(name)
            && !name.Any(char.IsDigit)
            && Enum.TryParse(name.Trim(), ignoreCase: true, out tag)
            && tag != AssetTags.Untagged
            && Enum.IsDefined(tag);
    }
}

/// <summary>A sent title is 1 to 200 characters and every sent tag is a known tag.</summary>
public sealed class EditPlayerFileCommandValidator : AbstractValidator<EditPlayerFileCommand>
{
    /// <summary>Builds the rules.</summary>
    public EditPlayerFileCommandValidator()
    {
        RuleFor(command => command.Title)
            .Must(title => title!.Trim().Length is > 0 and <= Asset.MaxTitleLength)
            .When(command => command.Title is not null)
            .WithMessage($"Use a title of 1 to {Asset.MaxTitleLength} characters.")
            .OverridePropertyName("title");
        RuleFor(command => command.Tags)
            .Must(tags => tags!.All(tag => EditPlayerFileCommand.TryParseTag(tag, out _)))
            .When(command => command.Tags is not null)
            .WithMessage("Use Highlight, Spike, Serve, Block, Reception, Defence, FullMatch or Stats.")
            .OverridePropertyName("tags");
    }
}

/// <summary>Handles <see cref="EditPlayerFileCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="assets">The files.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class EditPlayerFileCommandHandler(
    IRepository<Player, Guid> players,
    IRepository<Asset, Guid> assets,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<EditPlayerFileCommand, PlayerFileDto>
{
    /// <summary>
    /// Checks who may edit, then applies visibility first (so hiding the hero clears it), then the title, the tags and
    /// the hero choice.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file after the edit.</returns>
    /// <exception cref="NotFoundException">The player, or the file of that player, does not exist.</exception>
    /// <exception cref="ForbiddenAccessException">Another analyst, or a non-admin hiding or showing the file.</exception>
    /// <exception cref="Domain.Primitives.DomainException">The file cannot be the hero.</exception>
    public async Task<PlayerFileDto> Handle(EditPlayerFileCommand request, CancellationToken cancellationToken)
    {
        var player = await players.GetByIdAsync(request.PlayerId, cancellationToken) ?? throw new NotFoundException("Player");
        var asset = await assets.GetByIdAsync(request.FileId, cancellationToken);
        if (asset is null || asset.PlayerId != player.Id)
        {
            throw new NotFoundException("File");
        }

        PlayerAccess.EnsureCanManage(player, currentUser);
        if (request.Hidden is { } hidden)
        {
            if (!PlayerAccess.CanHideFiles(currentUser))
            {
                throw new ForbiddenAccessException();
            }

            ChangeVisibility(player, asset, hidden);
        }

        if (request.Title is { } title && title.Trim() != asset.Title)
        {
            asset.ChangeTitle(title);
        }

        if (request.Tags is { } tags)
        {
            asset.ChangeTags(tags.Aggregate(AssetTags.Untagged, (all, name) => EditPlayerFileCommand.TryParseTag(name, out var tag) ? all | tag : all));
        }

        if (request.IsHero == true)
        {
            player.SetHero(asset);
        }
        else if (request.IsHero == false && player.HeroAssetId == asset.Id)
        {
            player.ClearHero();
        }

        return asset.ToFileDto() with { IsHero = player.HeroAssetId == asset.Id };
    }

    /// <summary>Hides or shows the file and audits a real change; hiding the hero clears it.</summary>
    /// <param name="player">The player.</param>
    /// <param name="asset">The file.</param>
    /// <param name="hidden">Whether it should be hidden.</param>
    private void ChangeVisibility(Player player, Asset asset, bool hidden)
    {
        if (hidden == (asset.Visibility == AssetVisibility.Hidden))
        {
            return;
        }

        if (hidden)
        {
            asset.Hide();
            if (player.HeroAssetId == asset.Id)
            {
                player.ClearHero();
            }
        }
        else
        {
            asset.Show();
        }

        Guid? actorId = Guid.TryParse(currentUser.UserId, out var userId) ? userId : null;
        var action = hidden ? EditPlayerFileCommand.HiddenAction : EditPlayerFileCommand.ShownAction;
        audit.Add(AuditEntry.Record(actorId, action, nameof(Asset), asset.Id.ToString(), null, timeProvider.GetUtcNow()));
    }
}
