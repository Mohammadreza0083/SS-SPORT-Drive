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

    /// <summary>Gets the "OpenMyPlayers" text.</summary>
    public static string OpenMyPlayers => Get(nameof(OpenMyPlayers));

    /// <summary>Gets the "OpenMyPlayersButton" text.</summary>
    public static string OpenMyPlayersButton => Get(nameof(OpenMyPlayersButton));

    /// <summary>Gets the "MiniAppNotSetUp" text.</summary>
    public static string MiniAppNotSetUp => Get(nameof(MiniAppNotSetUp));

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

    /// <summary>Gets the "ForwardKeepButton" text.</summary>
    public static string ForwardKeepButton => Get(nameof(ForwardKeepButton));

    /// <summary>Gets the "ForwardBlockButton" text.</summary>
    public static string ForwardBlockButton => Get(nameof(ForwardBlockButton));

    /// <summary>Gets the "ForwardGone" text.</summary>
    public static string ForwardGone => Get(nameof(ForwardGone));

    /// <summary>Gets the "OnlyAdminsForwards" text.</summary>
    public static string OnlyAdminsForwards => Get(nameof(OnlyAdminsForwards));

    /// <summary>Formats the "ForwardAlert" text: link code, recipient, players, email.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatForwardAlert(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ForwardAlert"), args);

    /// <summary>Formats the "ForwardKept" text: email, link code, admin.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatForwardKept(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ForwardKept"), args);

    /// <summary>Formats the "ForwardBlocked" text: email, link code, admin.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatForwardBlocked(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ForwardBlocked"), args);

    /// <summary>Formats the "ForwardAlreadyDecided" text: email, link code, kept or blocked, admin.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatForwardAlreadyDecided(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ForwardAlreadyDecided"), args);

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

    /// <summary>Gets the "ProgressUnnamed" text (T-6a progress message).</summary>
    public static string ProgressUnnamed => Get(nameof(ProgressUnnamed));

    /// <summary>Gets the "ProgressLinksVerified" text (T-6a progress message).</summary>
    public static string ProgressLinksVerified => Get(nameof(ProgressLinksVerified));

    /// <summary>Gets the "ProgressOneFolder" text (T-6a progress message).</summary>
    public static string ProgressOneFolder => Get(nameof(ProgressOneFolder));

    /// <summary>Gets the "ProgressNoFolder" text (T-6a progress message).</summary>
    public static string ProgressNoFolder => Get(nameof(ProgressNoFolder));

    /// <summary>Gets the "ProgressPhotoAdded" text (T-6a progress message).</summary>
    public static string ProgressPhotoAdded => Get(nameof(ProgressPhotoAdded));

    /// <summary>Gets the "ProgressPhotoMissing" text (T-6a progress message).</summary>
    public static string ProgressPhotoMissing => Get(nameof(ProgressPhotoMissing));

    /// <summary>Gets the "ProgressWaitingDetails" text (T-6a progress message).</summary>
    public static string ProgressWaitingDetails => Get(nameof(ProgressWaitingDetails));

    /// <summary>Gets the "ProgressWaitingAdmin" text (T-6a progress message).</summary>
    public static string ProgressWaitingAdmin => Get(nameof(ProgressWaitingAdmin));

    /// <summary>Gets the "ProgressPublished" text (T-6a progress message).</summary>
    public static string ProgressPublished => Get(nameof(ProgressPublished));

    /// <summary>Formats the "ProgressHeaderWaiting" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressHeaderWaiting(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressHeaderWaiting"), args);

    /// <summary>Formats the "ProgressHeaderPublished" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressHeaderPublished(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressHeaderPublished"), args);

    /// <summary>Formats the "ProgressFolders" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressFolders(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressFolders"), args);

    /// <summary>Formats the "ProgressStillMissing" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressStillMissing(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressStillMissing"), args);

    /// <summary>Formats the "PhotoSaved" text (T-4d photo in the chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatPhotoSaved(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("PhotoSaved"), args);

    /// <summary>Gets the "PhotoPickPlayer" text (T-4d photo in the chat).</summary>
    public static string PhotoPickPlayer => Get(nameof(PhotoPickPlayer));

    /// <summary>Gets the "PhotoNoPlayers" text (T-4d photo in the chat).</summary>
    public static string PhotoNoPlayers => Get(nameof(PhotoNoPlayers));

    /// <summary>Gets the "PhotoNeedsAccess" text (T-4d photo in the chat).</summary>
    public static string PhotoNeedsAccess => Get(nameof(PhotoNeedsAccess));

    /// <summary>Gets the "PhotoTooSmall" text (T-4d photo in the chat).</summary>
    public static string PhotoTooSmall => Get(nameof(PhotoTooSmall));

    /// <summary>Gets the "PhotoUnreadable" text (T-4d photo in the chat).</summary>
    public static string PhotoUnreadable => Get(nameof(PhotoUnreadable));

    /// <summary>Gets the "PhotoTooLarge" text (T-4d photo in the chat).</summary>
    public static string PhotoTooLarge => Get(nameof(PhotoTooLarge));

    /// <summary>Formats the "PhotoNotYours" text (T-4d photo in the chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatPhotoNotYours(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("PhotoNotYours"), args);

    /// <summary>Gets the "PhotoPlayerGone" text (T-4d photo in the chat).</summary>
    public static string PhotoPlayerGone => Get(nameof(PhotoPlayerGone));

    /// <summary>Gets the "PhotoFailed" text (T-4d photo in the chat).</summary>
    public static string PhotoFailed => Get(nameof(PhotoFailed));

    /// <summary>Gets the "PreviousButton" text (T-4d photo in the chat).</summary>
    public static string PreviousButton => Get(nameof(PreviousButton));

    /// <summary>Gets the "NextButton" text (T-4d photo in the chat).</summary>
    public static string NextButton => Get(nameof(NextButton));

    /// <summary>Formats the "WelcomeApproved" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatWelcomeApproved(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("WelcomeApproved"), args);

    /// <summary>Formats the "WelcomeBack" text.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatWelcomeBack(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("WelcomeBack"), args);

    /// <summary>Gets the "ChatCategoryPrompt" text (T-10a chat).</summary>
    public static string ChatCategoryPrompt => Get(nameof(ChatCategoryPrompt));

    /// <summary>Gets the "ChatNamePrompt" text (T-10a chat).</summary>
    public static string ChatNamePrompt => Get(nameof(ChatNamePrompt));

    /// <summary>Gets the "ChatNationalityPrompt" text (T-10a chat).</summary>
    public static string ChatNationalityPrompt => Get(nameof(ChatNationalityPrompt));

    /// <summary>Gets the "ChatNationalityPick" text (T-10a chat).</summary>
    public static string ChatNationalityPick => Get(nameof(ChatNationalityPick));

    /// <summary>Formats the "ChatNationalityNone" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatNationalityNone(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatNationalityNone"), args);

    /// <summary>Formats the "ChatPositionsPrompt" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatPositionsPrompt(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatPositionsPrompt"), args);

    /// <summary>Gets the "ChatNoneChosen" text (T-10a chat).</summary>
    public static string ChatNoneChosen => Get(nameof(ChatNoneChosen));

    /// <summary>Gets the "ChatPositionsNone" text (T-10a chat).</summary>
    public static string ChatPositionsNone => Get(nameof(ChatPositionsNone));

    /// <summary>Gets the "ChatPositionsMax" text (T-10a chat).</summary>
    public static string ChatPositionsMax => Get(nameof(ChatPositionsMax));

    /// <summary>Gets the "ChatVolleyboxPrompt" text (T-10a chat).</summary>
    public static string ChatVolleyboxPrompt => Get(nameof(ChatVolleyboxPrompt));

    /// <summary>Formats the "ChatVolleyboxExisting" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatVolleyboxExisting(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatVolleyboxExisting"), args);

    /// <summary>Formats the "ChatAddFolderButton" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatAddFolderButton(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatAddFolderButton"), args);

    /// <summary>Gets the "ChatUpdateInMiniAppButton" text (T-10a chat).</summary>
    public static string ChatUpdateInMiniAppButton => Get(nameof(ChatUpdateInMiniAppButton));

    /// <summary>Gets the "ChatOtherLinkButton" text (T-10a chat).</summary>
    public static string ChatOtherLinkButton => Get(nameof(ChatOtherLinkButton));

    /// <summary>Gets the "ChatDrivePrompt" text (T-10a chat).</summary>
    public static string ChatDrivePrompt => Get(nameof(ChatDrivePrompt));

    /// <summary>Formats the "ChatDriveForExistingPrompt" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatDriveForExistingPrompt(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatDriveForExistingPrompt"), args);

    /// <summary>Formats the "ChatDriveFound" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatDriveFound(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatDriveFound"), args);

    /// <summary>Formats the "ChatDriveTaken" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatDriveTaken(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatDriveTaken"), args);

    /// <summary>Gets the "ChatPhotoPrompt" text (T-10a chat).</summary>
    public static string ChatPhotoPrompt => Get(nameof(ChatPhotoPrompt));

    /// <summary>Gets the "ChatSendPhotoOrSkip" text (T-10a chat).</summary>
    public static string ChatSendPhotoOrSkip => Get(nameof(ChatSendPhotoOrSkip));

    /// <summary>Gets the "ChatUseButtons" text (T-10a chat).</summary>
    public static string ChatUseButtons => Get(nameof(ChatUseButtons));

    /// <summary>Formats the "ChatSummary" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatSummary(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatSummary"), args);

    /// <summary>Gets the "ChatPhotoAdded" text (T-10a chat).</summary>
    public static string ChatPhotoAdded => Get(nameof(ChatPhotoAdded));

    /// <summary>Gets the "ChatPhotoLater" text (T-10a chat).</summary>
    public static string ChatPhotoLater => Get(nameof(ChatPhotoLater));

    /// <summary>Gets the "ChatIncomplete" text (T-10a chat).</summary>
    public static string ChatIncomplete => Get(nameof(ChatIncomplete));

    /// <summary>Formats the "ChatCreated" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatCreated(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatCreated"), args);

    /// <summary>Formats the "ChatFolderAdded" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatFolderAdded(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatFolderAdded"), args);

    /// <summary>Gets the "ChatCancelled" text (T-10a chat).</summary>
    public static string ChatCancelled => Get(nameof(ChatCancelled));

    /// <summary>Gets the "ChatNothingToCancel" text (T-10a chat).</summary>
    public static string ChatNothingToCancel => Get(nameof(ChatNothingToCancel));

    /// <summary>Gets the "ChatResume" text (T-10a chat).</summary>
    public static string ChatResume => Get(nameof(ChatResume));

    /// <summary>Gets the "ChatStaleButton" text (T-10a chat).</summary>
    public static string ChatStaleButton => Get(nameof(ChatStaleButton));

    /// <summary>Gets the "ChatTooLong" text (T-10a chat).</summary>
    public static string ChatTooLong => Get(nameof(ChatTooLong));

    /// <summary>Formats the "ChatProblem" text (T-10a chat).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatChatProblem(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ChatProblem"), args);

    /// <summary>Gets the "ChatDriveUnavailable" text (T-10a chat).</summary>
    public static string ChatDriveUnavailable => Get(nameof(ChatDriveUnavailable));

    /// <summary>Gets the "ChatEditPick" text (T-10a chat).</summary>
    public static string ChatEditPick => Get(nameof(ChatEditPick));

    /// <summary>Gets the "ChatNeedsAccess" text (T-10a chat).</summary>
    public static string ChatNeedsAccess => Get(nameof(ChatNeedsAccess));

    /// <summary>Gets the "ChatContinueButton" text (T-10a chat).</summary>
    public static string ChatContinueButton => Get(nameof(ChatContinueButton));

    /// <summary>Gets the "ChatStartOverButton" text (T-10a chat).</summary>
    public static string ChatStartOverButton => Get(nameof(ChatStartOverButton));

    /// <summary>Gets the "ChatMenButton" text (T-10a chat).</summary>
    public static string ChatMenButton => Get(nameof(ChatMenButton));

    /// <summary>Gets the "ChatWomenButton" text (T-10a chat).</summary>
    public static string ChatWomenButton => Get(nameof(ChatWomenButton));

    /// <summary>Gets the "ChatBackButton" text (T-10a chat).</summary>
    public static string ChatBackButton => Get(nameof(ChatBackButton));

    /// <summary>Gets the "ChatCancelButton" text (T-10a chat).</summary>
    public static string ChatCancelButton => Get(nameof(ChatCancelButton));

    /// <summary>Gets the "ChatDoneButton" text (T-10a chat).</summary>
    public static string ChatDoneButton => Get(nameof(ChatDoneButton));

    /// <summary>Gets the "ChatSkipButton" text (T-10a chat).</summary>
    public static string ChatSkipButton => Get(nameof(ChatSkipButton));

    /// <summary>Gets the "ChatCreateButton" text (T-10a chat).</summary>
    public static string ChatCreateButton => Get(nameof(ChatCreateButton));

    /// <summary>Gets the "ChatEditButton" text (T-10a chat).</summary>
    public static string ChatEditButton => Get(nameof(ChatEditButton));

    /// <summary>Gets the "ChatOpenMiniAppButton" text (T-10a chat).</summary>
    public static string ChatOpenMiniAppButton => Get(nameof(ChatOpenMiniAppButton));

    /// <summary>Gets the "ChatFieldCategory" text (T-10a chat).</summary>
    public static string ChatFieldCategory => Get(nameof(ChatFieldCategory));

    /// <summary>Gets the "ChatFieldName" text (T-10a chat).</summary>
    public static string ChatFieldName => Get(nameof(ChatFieldName));

    /// <summary>Gets the "ChatFieldNationality" text (T-10a chat).</summary>
    public static string ChatFieldNationality => Get(nameof(ChatFieldNationality));

    /// <summary>Gets the "ChatFieldPositions" text (T-10a chat).</summary>
    public static string ChatFieldPositions => Get(nameof(ChatFieldPositions));

    /// <summary>Gets the "ChatFieldVolleybox" text (T-10a chat).</summary>
    public static string ChatFieldVolleybox => Get(nameof(ChatFieldVolleybox));

    /// <summary>Gets the "ChatFieldDriveFolder" text (T-10a chat).</summary>
    public static string ChatFieldDriveFolder => Get(nameof(ChatFieldDriveFolder));

    /// <summary>Gets the "ChatFieldPhoto" text (T-10a chat).</summary>
    public static string ChatFieldPhoto => Get(nameof(ChatFieldPhoto));

    /// <summary>Formats the "PlayerPublishedNotice" text (T-8): {0} the player.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatPlayerPublishedNotice(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("PlayerPublishedNotice"), args);

    /// <summary>Formats the "NoticeDigestHeader" text (T-8 grouped notices): {0} how many.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatNoticeDigestHeader(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("NoticeDigestHeader"), args);

    /// <summary>Reads a text.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidOperationException">The key is missing from the resources.</exception>
    private static string Get(string key) =>
        _resources.GetString(key, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException($"Bot text '{key}' is missing.");
}
