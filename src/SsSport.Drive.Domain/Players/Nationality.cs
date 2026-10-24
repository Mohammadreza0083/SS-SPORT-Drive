using SsSport.Drive.Domain.Countries;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>
/// A player's nationality: a country code from <see cref="CountryCatalogue"/> (stored upper-case, e.g. <c>IR</c>).
/// </summary>
public sealed record Nationality
{
    /// <summary>Initializes the nationality with a known code.</summary>
    /// <param name="code">The upper-case code.</param>
    private Nationality(string code) => Code = code;

    /// <summary>Gets the country code, upper-case.</summary>
    public string Code { get; }

    /// <summary>Gets the country's English name, e.g. <c>Iran</c>.</summary>
    public string CountryName => CountryCatalogue.TryFind(Code, out var country) ? country!.Name : Code;

    /// <summary>Validates a country code.</summary>
    /// <param name="raw">The code in any case.</param>
    /// <returns>The nationality.</returns>
    /// <exception cref="DomainException"><c>player.nationality_invalid</c> for anything that is not in the country catalogue.</exception>
    public static Nationality Create(string? raw)
    {
        if (!CountryCatalogue.TryFind(raw, out var country))
        {
            throw new DomainException("player.nationality_invalid", "The nationality must be a two-letter country code from the country list.");
        }

        return new Nationality(country!.Code);
    }

    /// <summary>Returns the code.</summary>
    /// <returns>The country code.</returns>
    public override string ToString() => Code;
}
