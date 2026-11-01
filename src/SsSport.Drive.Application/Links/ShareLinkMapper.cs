using Riok.Mapperly.Abstractions;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Links;

/// <summary>Maps links, recipients and their players to DTOs at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target, EnumMappingStrategy = EnumMappingStrategy.ByName)]
public static partial class ShareLinkMapper
{
    /// <summary>Maps a link with its recipient and players, as it stands at a moment.</summary>
    /// <param name="link">The link.</param>
    /// <param name="recipient">Its recipient.</param>
    /// <param name="players">Its players, in any order; the link's order is kept.</param>
    /// <param name="now">The moment the access is judged at.</param>
    /// <returns>The DTO.</returns>
    public static ShareLinkDto ToDto(this ShareLink link, Recipient recipient, IReadOnlyCollection<Player> players, DateTimeOffset now)
    {
        var byId = players.ToDictionary(player => player.Id);
        return MapLink(link) with
        {
            Path = $"/r/{link.Code.Value}",
            Access = link.AccessAt(now).ToString(),
            Recipient = recipient.ToDto(),
            Players = [.. link.PlayerIds.Where(byId.ContainsKey).Select(id => MapPlayer(byId[id]))],
        };
    }

    /// <summary>Maps a recipient.</summary>
    /// <param name="recipient">The recipient.</param>
    /// <returns>The DTO.</returns>
    [MapProperty(nameof(Recipient.Country), nameof(RecipientDto.CountryCode))]
    public static partial RecipientDto ToDto(this Recipient recipient);

    /// <summary>Maps the link's own fields; the path, access, recipient and players are filled in by <see cref="ToDto(ShareLink, Recipient, IReadOnlyCollection{Player}, DateTimeOffset)"/>.</summary>
    /// <param name="link">The link.</param>
    /// <returns>The DTO without the related data.</returns>
    [MapperIgnoreTarget(nameof(ShareLinkDto.Path))]
    [MapperIgnoreTarget(nameof(ShareLinkDto.Access))]
    [MapperIgnoreTarget(nameof(ShareLinkDto.Recipient))]
    [MapperIgnoreTarget(nameof(ShareLinkDto.Players))]
    [MapperIgnoreSource(nameof(ShareLink.RecipientId))]
    [MapperIgnoreSource(nameof(ShareLink.PlayerIds))]
    [MapperIgnoreSource(nameof(ShareLink.DomainEvents))]
    private static partial ShareLinkDto MapLink(ShareLink link);

    /// <summary>Maps a player on a link.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The DTO.</returns>
    private static partial ShareLinkPlayerDto MapPlayer(Player player);

    /// <summary>Turns a code into its text.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The text.</returns>
    private static string FromCode(ShareLinkCode code) => code.Value;

    /// <summary>Turns an email into the full address (the DTO is admin-only).</summary>
    /// <param name="email">The email.</param>
    /// <returns>The address.</returns>
    private static string FromEmail(EmailAddress email) => email.Value;

    /// <summary>Turns a country into its code.</summary>
    /// <param name="country">The country, or <see langword="null"/>.</param>
    /// <returns>The code, or <see langword="null"/>.</returns>
    private static string? FromCountry(Nationality? country) => country?.Code;

    /// <summary>Turns a name into its text.</summary>
    /// <param name="name">The name, or <see langword="null"/>.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string? FromName(FullName? name) => name?.Value;

    /// <summary>Turns a slug into its text.</summary>
    /// <param name="slug">The slug, or <see langword="null"/>.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string? FromSlug(Slug? slug) => slug?.Value;
}
