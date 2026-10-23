using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Players;

/// <summary>
/// A player's nationality as an ISO 3166-1 alpha-2 country code (stored upper-case, e.g. <c>IR</c>).
/// The code list is embedded because the app runs with invariant globalization (no OS country data).
/// </summary>
public sealed record Nationality
{
    /// <summary>
    /// Every officially assigned ISO 3166-1 alpha-2 code, plus <c>XK</c> (Kosovo), which volleyball federations use.
    /// </summary>
    private static readonly HashSet<string> _codes = new(
        """
        AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ
        CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR
        GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP
        KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT
        MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW
        SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG
        UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW XK
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    /// <summary>Initializes the nationality with a known code.</summary>
    /// <param name="code">The upper-case code.</param>
    private Nationality(string code) => Code = code;

    /// <summary>Gets the ISO 3166-1 alpha-2 code, upper-case.</summary>
    public string Code { get; }

    /// <summary>Validates a country code.</summary>
    /// <param name="raw">The code in any case.</param>
    /// <returns>The nationality.</returns>
    /// <exception cref="DomainException"><c>player.nationality_invalid</c> for anything that is not a known alpha-2 code.</exception>
    public static Nationality Create(string? raw)
    {
        var code = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (!_codes.Contains(code))
        {
            throw new DomainException("player.nationality_invalid", "The nationality must be an ISO 3166-1 alpha-2 country code.");
        }

        return new Nationality(code);
    }

    /// <summary>Returns the code.</summary>
    /// <returns>The ISO code.</returns>
    public override string ToString() => Code;
}
