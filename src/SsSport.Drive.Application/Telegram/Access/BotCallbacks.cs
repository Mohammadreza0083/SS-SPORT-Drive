using System.Globalization;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Access;

/// <summary>An admin's decision on an access request.</summary>
public enum AccessDecision
{
    /// <summary>Approve as an analyst.</summary>
    ApproveAsAnalyst,

    /// <summary>Approve as an admin.</summary>
    ApproveAsAdmin,

    /// <summary>Decline.</summary>
    Decline,
}

/// <summary>The callback data of the bot's buttons, and the keyboards that use them. All values fit Telegram's 64 bytes.</summary>
public static class BotCallbacks
{
    /// <summary>Ask for access.</summary>
    public const string RequestAccess = "access:request";

    /// <summary>Open the new-player form (T-4).</summary>
    public const string NewPlayer = "menu:new";

    /// <summary>Open my players (T-7).</summary>
    public const string MyPlayers = "menu:players";

    /// <summary>Show help.</summary>
    public const string Help = "menu:help";

    /// <summary>Prefix of approve-as-analyst buttons.</summary>
    private const string ApproveAnalystPrefix = "access:approve:analyst:";

    /// <summary>Prefix of approve-as-admin buttons.</summary>
    private const string ApproveAdminPrefix = "access:approve:admin:";

    /// <summary>Prefix of decline buttons.</summary>
    private const string DeclinePrefix = "access:decline:";

    /// <summary>Gets the single Request access button.</summary>
    public static BotKeyboard RequestAccessKeyboard => BotKeyboard.Column(new BotButton(BotTexts.RequestAccessButton, RequestAccess));

    /// <summary>Gets the main menu of an active team member.</summary>
    public static BotKeyboard MenuKeyboard => BotKeyboard.Column(
        new BotButton(BotTexts.NewPlayerButton, NewPlayer),
        new BotButton(BotTexts.MyPlayersButton, MyPlayers),
        new BotButton(BotTexts.HelpButton, Help));

    /// <summary>Builds the review buttons admins get for a request.</summary>
    /// <param name="userId">The requesting user.</param>
    /// <returns>The keyboard.</returns>
    public static BotKeyboard ReviewKeyboard(Guid userId) => BotKeyboard.Column(
        new BotButton(BotTexts.ApproveAnalystButton, ApproveAnalystPrefix + Id(userId)),
        new BotButton(BotTexts.ApproveAdminButton, ApproveAdminPrefix + Id(userId)),
        new BotButton(BotTexts.DeclineButton, DeclinePrefix + Id(userId)));

    /// <summary>Reads a review button's decision and user.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="decision">The decision.</param>
    /// <param name="userId">The requesting user.</param>
    /// <returns><see langword="true"/> for a review button.</returns>
    public static bool TryParseReview(string data, out AccessDecision decision, out Guid userId)
    {
        (decision, var id) = data switch
        {
            _ when data.StartsWith(ApproveAnalystPrefix, StringComparison.Ordinal) => (AccessDecision.ApproveAsAnalyst, data[ApproveAnalystPrefix.Length..]),
            _ when data.StartsWith(ApproveAdminPrefix, StringComparison.Ordinal) => (AccessDecision.ApproveAsAdmin, data[ApproveAdminPrefix.Length..]),
            _ when data.StartsWith(DeclinePrefix, StringComparison.Ordinal) => (AccessDecision.Decline, data[DeclinePrefix.Length..]),
            _ => (default, string.Empty),
        };
        return Guid.TryParseExact(id, "N", out userId);
    }

    /// <summary>Gets the role a decision grants.</summary>
    /// <param name="decision">The decision.</param>
    /// <returns>The role, or <see langword="null"/> for a decline.</returns>
    public static Role? RoleOf(AccessDecision decision) => decision switch
    {
        AccessDecision.ApproveAsAnalyst => Role.Analyst,
        AccessDecision.ApproveAsAdmin => Role.Admin,
        _ => null,
    };

    /// <summary>Formats an id compactly (32 hex digits).</summary>
    /// <param name="id">The id.</param>
    /// <returns>The text.</returns>
    private static string Id(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);
}
