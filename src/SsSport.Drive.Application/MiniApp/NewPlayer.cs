using Ardalis.Specification;
using FluentValidation;
using Riok.Mapperly.Abstractions;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Players;
using SsSport.Drive.Domain.Countries;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.MiniApp;

/// <summary>A country in the nationality picker.</summary>
public sealed record CountryDto
{
    /// <summary>Gets the two-letter code (ISO 3166-1 alpha-2, plus XK, AC, TA).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the English name.</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>The result of checking a pasted Volleybox link.</summary>
public sealed record VolleyboxCheckDto
{
    /// <summary>Gets the Volleybox player id parsed from the link.</summary>
    public int VolleyboxId { get; init; }

    /// <summary>Gets the normalised player URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Gets the id of the player who already has this Volleybox id, or <see langword="null"/> for a new player.</summary>
    public Guid? ExistingPlayerId { get; init; }

    /// <summary>Gets that player's name, or <see langword="null"/>.</summary>
    public string? ExistingPlayerName { get; init; }
}

/// <summary>The new-player form as the Mini App sends it (<c>POST /miniapp/players</c>).</summary>
public sealed record NewPlayerDto
{
    /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the full name in Latin letters (stored Title Cased).</summary>
    public string? Name { get; init; }

    /// <summary>Gets the two-letter country code.</summary>
    public string? NationalityCode { get; init; }

    /// <summary>Gets one or two position codes (<c>S</c>, <c>OH</c>, <c>OPP</c>, <c>MB</c>, <c>L</c>, <c>DS</c>), primary first.</summary>
    public IReadOnlyList<string>? Positions { get; init; }

    /// <summary>Gets the Volleybox player page URL.</summary>
    public string? VolleyboxUrl { get; init; }

    /// <summary>Gets the Google Drive folder link.</summary>
    public string? DriveFolderUrl { get; init; }

    /// <summary>Turns the form into its command.</summary>
    /// <returns>The command.</returns>
    public CreatePlayerCommand ToCommand() => new(Category, Name, NationalityCode, Positions, VolleyboxUrl, DriveFolderUrl);
}

/// <summary>Maps the country catalogue to DTOs at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class CountryMapper
{
    /// <summary>Maps a country.</summary>
    /// <param name="country">The country.</param>
    /// <returns>The DTO.</returns>
    public static partial CountryDto ToDto(this Country country);
}

/// <summary>The player with a Volleybox id, read untracked.</summary>
public sealed class PlayerByVolleyboxIdSpec : Specification<Player>, ISingleResultSpecification<Player>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="volleyboxId">The Volleybox player id.</param>
    public PlayerByVolleyboxIdSpec(int volleyboxId) => Query.Where(player => player.Volleybox!.PlayerId == volleyboxId).AsNoTracking();
}

/// <summary>Players using a slug.</summary>
public sealed class PlayerBySlugSpec : Specification<Player>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="slug">The slug.</param>
    public PlayerBySlugSpec(Slug slug) => Query.Where(player => player.Slug == slug);
}

/// <summary>The linked folder with a Drive folder id.</summary>
public sealed class DriveFolderByDriveIdSpec : Specification<DriveFolder>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="driveFolderId">The Google Drive folder id.</param>
    public DriveFolderByDriveIdSpec(string driveFolderId) => Query.Where(folder => folder.DriveFolderId == driveFolderId);
}

/// <summary>The nationality picker's countries (<c>GET /miniapp/countries</c>).</summary>
[UnpagedList("The fixed 245-country catalogue, seeded once (D-10); the picker searches it on the device.")]
public sealed record ListCountriesQuery : IQuery<IReadOnlyList<CountryDto>>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Handles <see cref="ListCountriesQuery"/> from the in-memory catalogue.</summary>
public sealed class ListCountriesQueryHandler : IQueryHandler<ListCountriesQuery, IReadOnlyList<CountryDto>>
{
    /// <summary>Lists the countries in catalogue order.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Not used; the catalogue is in memory.</param>
    /// <returns>The countries.</returns>
    public Task<IReadOnlyList<CountryDto>> Handle(ListCountriesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CountryDto>>([.. CountryCatalogue.All.Select(country => country.ToDto())]);
}

/// <summary>Checks a pasted Volleybox link live (<c>GET /miniapp/volleybox</c>).</summary>
/// <param name="Url">The pasted link.</param>
public sealed record CheckVolleyboxQuery(string? Url) : IQuery<VolleyboxCheckDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>Handles <see cref="CheckVolleyboxQuery"/>.</summary>
/// <param name="players">The players.</param>
public sealed class CheckVolleyboxQueryHandler(IRepository<Player, Guid> players) : IQueryHandler<CheckVolleyboxQuery, VolleyboxCheckDto>
{
    /// <summary>Parses the link and looks for a player who already has its id.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The parsed id, the normalised URL and any existing player.</returns>
    /// <exception cref="DomainException">The link is not a Volleybox player page.</exception>
    public async Task<VolleyboxCheckDto> Handle(CheckVolleyboxQuery request, CancellationToken cancellationToken)
    {
        var profile = VolleyboxProfile.Create(request.Url);
        var existing = await players.FirstOrDefaultAsync(new PlayerByVolleyboxIdSpec(profile.PlayerId), cancellationToken);
        return new VolleyboxCheckDto
        {
            VolleyboxId = profile.PlayerId,
            Url = profile.Url,
            ExistingPlayerId = existing?.Id,
            ExistingPlayerName = existing?.Name?.Value,
        };
    }
}

/// <summary>
/// Creates a player from the Mini App's form (<c>POST /miniapp/players</c>, T-4a). The player waits for an admin to
/// publish it, is owned by the analyst who sent it, and gets the Drive folder linked.
/// </summary>
/// <param name="Category">Men or Women.</param>
/// <param name="Name">The full name in Latin letters.</param>
/// <param name="NationalityCode">The country code from the catalogue.</param>
/// <param name="Positions">One or two position codes, primary first.</param>
/// <param name="VolleyboxUrl">The Volleybox player page.</param>
/// <param name="DriveFolderUrl">The Google Drive folder link.</param>
public sealed record CreatePlayerCommand(
    string? Category,
    string? Name,
    string? NationalityCode,
    IReadOnlyList<string>? Positions,
    string? VolleyboxUrl,
    string? DriveFolderUrl) : ICommand<PlayerDetailsDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: every active team member.</summary>
    public IReadOnlyCollection<string> RequiredRoles => MiniAppAccess.TeamRoles;
}

/// <summary>
/// Checks that every field of <see cref="CreatePlayerCommand"/> is present and that the category and positions can be
/// read. Errors use the JSON field names. The business rules (name, country, Volleybox, Drive link) are the Domain's.
/// </summary>
public sealed class CreatePlayerCommandValidator : AbstractValidator<CreatePlayerCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreatePlayerCommandValidator()
    {
        RuleFor(command => command.Category)
            .Must(category => Enum.TryParse<PlayerCategory>(category, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            .WithMessage("Choose Men or Women.")
            .OverridePropertyName("category");
        RuleFor(command => command.Name).NotEmpty().OverridePropertyName("name");
        RuleFor(command => command.NationalityCode).NotEmpty().OverridePropertyName("nationalityCode");
        RuleFor(command => command.Positions).NotEmpty().OverridePropertyName("positions");
        RuleFor(command => command.Positions)
            .Must(positions => positions!.Count <= PlayerPositions.MaxCount).WithMessage("Choose at most two positions.")
            .Must(positions => positions!.All(code => PositionParser.TryParse(code, out _))).WithMessage("Use S, OH, OPP, MB, L or DS.")
            .When(command => command.Positions is { Count: > 0 })
            .OverridePropertyName("positions");
        RuleFor(command => command.VolleyboxUrl).NotEmpty().OverridePropertyName("volleyboxUrl");
        RuleFor(command => command.DriveFolderUrl).NotEmpty().OverridePropertyName("driveFolderUrl");
    }
}

/// <summary>Handles <see cref="CreatePlayerCommand"/>; the transaction behaviour saves the player and its folder together.</summary>
/// <param name="players">The players.</param>
/// <param name="folders">The linked Drive folders.</param>
/// <param name="currentUser">The analyst sending the form.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class CreatePlayerCommandHandler(
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> folders,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreatePlayerCommand, PlayerDetailsDto>
{
    /// <summary>Builds the player from the form, after checking that its Volleybox id and Drive folder are free.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The new player.</returns>
    /// <exception cref="DomainException">A field breaks a rule, or the Volleybox id or the Drive folder is taken.</exception>
    public async Task<PlayerDetailsDto> Handle(CreatePlayerCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.UserId, out var analystId))
        {
            throw new UnauthenticatedException();
        }

        var name = FullName.Create(request.Name);
        var nationality = Nationality.Create(request.NationalityCode);
        var positions = PlayerPositions.Create(request.Positions!.Select(code => PositionParser.TryParse(code, out var position) ? position : default));
        var volleybox = VolleyboxProfile.Create(request.VolleyboxUrl);
        var folderLink = DriveFolderLink.Create(request.DriveFolderUrl);

        if (await players.AnyAsync(new PlayerByVolleyboxIdSpec(volleybox.PlayerId), cancellationToken))
        {
            throw new DomainException("player.volleybox_taken", "A player with this Volleybox page already exists.");
        }

        if (await folders.AnyAsync(new DriveFolderByDriveIdSpec(folderLink.FolderId), cancellationToken))
        {
            throw new DomainException("drive_folder.taken", "This Drive folder already belongs to a player.");
        }

        var now = timeProvider.GetUtcNow();
        var player = Player.Create(Enum.Parse<PlayerCategory>(request.Category!, ignoreCase: true), now);
        player.Rename(name, await FreeSlugAsync(Slug.FromName(name), cancellationToken));
        player.ChangeNationality(nationality);
        player.ChangePositions(positions);
        player.LinkVolleybox(volleybox);
        player.AssignAnalyst(analystId);
        players.Add(player);

        // The real folder name arrives with the Drive check (T-4c) and the sync; until then the id stands in for it.
        folders.Add(DriveFolder.Link(player.Id, folderLink.FolderId, folderLink.FolderId, now));
        return player.ToDetailsDto();
    }

    /// <summary>Finds the first free slug: the name's slug, then <c>-2</c>, <c>-3</c>…</summary>
    /// <param name="slug">The name's slug.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>A slug no player uses.</returns>
    private async Task<Slug> FreeSlugAsync(Slug slug, CancellationToken cancellationToken)
    {
        var candidate = slug;
        for (var number = 2; await players.AnyAsync(new PlayerBySlugSpec(candidate), cancellationToken); number++)
        {
            candidate = slug.WithSuffix(number);
        }

        return candidate;
    }
}
