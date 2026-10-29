using System.Globalization;
using System.Resources;

namespace SsSport.Drive.Application.Telegram;

/// <summary>The bot's English copy, read from <c>BotTexts.resx</c> (UI copy stays out of the code).</summary>
public static class BotTexts
{
    /// <summary>The resource reader.</summary>
    private static readonly ResourceManager _resources = new("SsSport.Drive.Application.Telegram.BotTexts", typeof(BotTexts).Assembly);

    /// <summary>Gets the "RequestAccessButton" text.</summary>
    public static string RequestAccessButton => Get(nameof(RequestAccessButton));

    /// <summary>Gets the "RequestSent" text.</summary>
    public static string RequestSent => Get(nameof(RequestSent));

    /// <summary>Gets the "RequestPending" text.</summary>
    public static string RequestPending => Get(nameof(RequestPending));

    /// <summary>Gets the "AccessEnded" text.</summary>
    public static string AccessEnded => Get(nameof(AccessEnded));

    /// <summary>Gets the "NoUsername" text.</summary>
    public static string NoUsername => Get(nameof(NoUsername));

    /// <summary>Gets the "ApproveAnalystButton" text.</summary>
    public static string ApproveAnalystButton => Get(nameof(ApproveAnalystButton));

    /// <summary>Gets the "ApproveAdminButton" text.</summary>
    public static string ApproveAdminButton => Get(nameof(ApproveAdminButton));

    /// <summary>Gets the "DeclineButton" text.</summary>
    public static string DeclineButton => Get(nameof(DeclineButton));

    /// <summary>Gets the "SomeAdmin" text.</summary>
    public static string SomeAdmin => Get(nameof(SomeAdmin));

    /// <summary>Gets the "OnlyAdmins" text.</summary>
    public static string OnlyAdmins => Get(nameof(OnlyAdmins));

    /// <summary>Gets the "RequestGone" text.</summary>
    public static string RequestGone => Get(nameof(RequestGone));

    /// <summary>Gets the "DeclinedNotice" text.</summary>
    public static string DeclinedNotice => Get(nameof(DeclinedNotice));

    /// <summary>Gets the "NewPlayerButton" text.</summary>
    public static string NewPlayerButton => Get(nameof(NewPlayerButton));

    /// <summary>Gets the "MyPlayersButton" text.</summary>
    public static string MyPlayersButton => Get(nameof(MyPlayersButton));

    /// <summary>Gets the "HelpButton" text.</summary>
    public static string HelpButton => Get(nameof(HelpButton));

    /// <summary>Gets the "Help" text.</summary>
    public static string Help => Get(nameof(Help));

    /// <summary>Gets the "ComingSoon" text.</summary>
    public static string ComingSoon => Get(nameof(ComingSoon));

    /// <summary>Gets the "FirstAdminNeedsEmail" text.</summary>
    public static string FirstAdminNeedsEmail => Get(nameof(FirstAdminNeedsEmail));

    /// <summary>Formats the "Welcome" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatWelcome(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("Welcome"), args);

    /// <summary>Formats the "RequestTooSoon" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatRequestTooSoon(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("RequestTooSoon"), args);

    /// <summary>Formats the "AdminRequest" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatAdminRequest(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("AdminRequest"), args);

    /// <summary>Formats the "Approved" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatApproved(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("Approved"), args);

    /// <summary>Formats the "Declined" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDeclined(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("Declined"), args);

    /// <summary>Formats the "AlreadyHandled" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatAlreadyHandled(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("AlreadyHandled"), args);

    /// <summary>Formats the "AdminNeedsEmail" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatAdminNeedsEmail(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("AdminNeedsEmail"), args);

    /// <summary>Formats the "FolderAddedByOther" text: {0} the player, {1} who added it, {2} the folder name.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatFolderAddedByOther(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("FolderAddedByOther"), args);

    /// <summary>Formats the "WelcomeApproved" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatWelcomeApproved(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("WelcomeApproved"), args);

    /// <summary>Formats the "WelcomeBack" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatWelcomeBack(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("WelcomeBack"), args);

    /// <summary>Reads a text.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidOperationException">The key is missing from the resources.</exception>
    private static string Get(string key) =>
        _resources.GetString(key, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException($"Bot text '{key}' is missing.");
}
