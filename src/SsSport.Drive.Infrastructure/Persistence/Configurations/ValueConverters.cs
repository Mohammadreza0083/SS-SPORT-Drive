using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>
/// Converters between value objects and their columns. Reading goes through the value object's own factory, so a
/// value that no longer passes the domain rules fails loudly instead of loading silently.
/// </summary>
internal static class ValueConverters
{
    /// <summary>Gets the converter for <see cref="Domain.Primitives.StorageKey"/> ↔ text.</summary>
    public static ValueConverter<StorageKey?, string> StorageKey { get; } = new(key => key!.Value, value => Domain.Primitives.StorageKey.Create(value));

    /// <summary>Gets the converter for <see cref="EmailAddress"/> ↔ text.</summary>
    public static ValueConverter<EmailAddress?, string> Email { get; } = new(email => email!.Value, value => EmailAddress.Create(value));

    /// <summary>Gets the converter for a required <see cref="EmailAddress"/> ↔ text.</summary>
    public static ValueConverter<EmailAddress, string> RequiredEmail { get; } = new(email => email.Value, value => EmailAddress.Create(value));

    /// <summary>Gets the converter for <see cref="Role"/> ↔ its name.</summary>
    public static ValueConverter<Role, string> Role { get; } = new(role => role.Name, value => Domain.Users.Role.FromName(value));

    /// <summary>Gets the converter for <see cref="FullName"/> ↔ text.</summary>
    public static ValueConverter<FullName?, string> FullName { get; } = new(name => name!.Value, value => Domain.Players.FullName.Create(value));

    /// <summary>Gets the converter for <see cref="Slug"/> ↔ text.</summary>
    public static ValueConverter<Slug?, string> Slug { get; } = new(slug => slug!.Value, value => Domain.Players.Slug.Create(value));

    /// <summary>Gets the converter for <see cref="Nationality"/> ↔ its ISO code.</summary>
    public static ValueConverter<Nationality?, string> Nationality { get; } = new(nationality => nationality!.Code, value => Domain.Players.Nationality.Create(value));

    /// <summary>Gets the converter for <see cref="PlayerPhoto"/> ↔ its storage key.</summary>
    public static ValueConverter<PlayerPhoto?, string> Photo { get; } = new(photo => photo!.StorageKey, value => PlayerPhoto.Create(value));

    /// <summary>Gets the converter for <see cref="PlayerPositions"/> ↔ a <c>text[]</c> of codes, primary first.</summary>
    public static ValueConverter<PlayerPositions?, string[]> Positions { get; } = new(positions => ToCodes(positions!), codes => FromCodes(codes));

    /// <summary>
    /// Gets the comparer for <see cref="PlayerPositions"/>: value equality, and the instance itself as the snapshot because it
    /// is immutable (otherwise EF Core treats the <c>text[]</c> column as a primitive collection of the model type).
    /// </summary>
    public static ValueComparer<PlayerPositions?> PositionsComparer { get; } = new(
        (left, right) => Equals(left, right),
        positions => positions == null ? 0 : positions.GetHashCode(),
        positions => positions);

    /// <summary>Gets the converter for <see cref="ShareLinkCode"/> ↔ its six characters.</summary>
    public static ValueConverter<ShareLinkCode, string> ShareLinkCode { get; } = new(code => code.Value, value => Domain.Links.ShareLinkCode.Create(value));

    /// <summary>Gets the converter for a list of <see cref="EmailAddress"/> ↔ a <c>text[]</c> of addresses, in order.</summary>
    public static ValueConverter<List<EmailAddress>, string[]> Emails { get; } = new(
        emails => emails.Select(email => email.Value).ToArray(),
        values => values.Select(EmailAddress.Create).ToList());

    /// <summary>Gets the comparer for a list of <see cref="EmailAddress"/>: same addresses in the same order, snapshotted as a copy.</summary>
    public static ValueComparer<List<EmailAddress>> EmailsComparer { get; } = new(
        (left, right) => left!.SequenceEqual(right!),
        emails => emails.Aggregate(0, HashCode.Combine),
        emails => emails.ToList());

    /// <summary>Turns positions into their codes, in order.</summary>
    /// <param name="positions">The positions.</param>
    /// <returns>The codes.</returns>
    private static string[] ToCodes(PlayerPositions positions) => positions.Items.Select(position => position.Code()).ToArray();

    /// <summary>Turns stored codes back into positions.</summary>
    /// <param name="codes">The codes, primary first.</param>
    /// <returns>The positions.</returns>
    private static PlayerPositions FromCodes(string[] codes) =>
        PlayerPositions.Create(codes.Select(code => PositionParser.TryParse(code, out var position)
            ? position
            : throw new InvalidOperationException($"Unknown position code '{code}' in the database.")));
}
