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

    /// <summary>Gets the "AccessRequestApproveButton" text.</summary>
    public static string AccessRequestApproveButton => Get(nameof(AccessRequestApproveButton));

    /// <summary>Gets the "AccessRequestDeclineButton" text.</summary>
    public static string AccessRequestDeclineButton => Get(nameof(AccessRequestDeclineButton));

    /// <summary>Gets the "AccessRequestGone" text.</summary>
    public static string AccessRequestGone => Get(nameof(AccessRequestGone));

    /// <summary>Gets the "OnlyAdminsAccessRequests" text.</summary>
    public static string OnlyAdminsAccessRequests => Get(nameof(OnlyAdminsAccessRequests));

    /// <summary>Formats the "AccessRequestAlert" text: player, email, description.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatAccessRequestAlert(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("AccessRequestAlert"), args);

    /// <summary>Formats the "AccessRequestApproved" text: email, player, admin, link code.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatAccessRequestApproved(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("AccessRequestApproved"), args);

    /// <summary>Formats the "AccessRequestDeclined" text: email, player, admin.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatAccessRequestDeclined(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("AccessRequestDeclined"), args);

    /// <summary>Formats the "AccessRequestAlreadyDecided" text: email, player, approved or declined, admin.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatAccessRequestAlreadyDecided(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("AccessRequestAlreadyDecided"), args);

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

    /// <summary>Formats the "ProgressCopy" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressCopy(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressCopy"), args);

    /// <summary>Formats the "ProgressTranscode" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressTranscode(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressTranscode"), args);

    /// <summary>Formats the "ProgressPages" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressPages(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressPages"), args);

    /// <summary>Gets the "ProgressFailedOne" text (T-6b build progress).</summary>
    public static string ProgressFailedOne => Get(nameof(ProgressFailedOne));

    /// <summary>Formats the "ProgressFailedMany" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressFailedMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressFailedMany"), args);

    /// <summary>Formats the "ProgressBar" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressBar(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressBar"), args);

    /// <summary>Gets the "ProgressEstimating" text (T-6b build progress).</summary>
    public static string ProgressEstimating => Get(nameof(ProgressEstimating));

    /// <summary>Formats the "ProgressTimeLeftMinutes" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressTimeLeftMinutes(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressTimeLeftMinutes"), args);

    /// <summary>Formats the "ProgressTimeLeftHours" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressTimeLeftHours(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressTimeLeftHours"), args);

    /// <summary>Formats the "ProgressTimeLeftHoursMinutes" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressTimeLeftHoursMinutes(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressTimeLeftHoursMinutes"), args);

    /// <summary>Formats the "ProgressLive" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressLive(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressLive"), args);

    /// <summary>Gets the "ProgressLiveVideoOne" text (T-6b build progress).</summary>
    public static string ProgressLiveVideoOne => Get(nameof(ProgressLiveVideoOne));

    /// <summary>Formats the "ProgressLiveVideoMany" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressLiveVideoMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressLiveVideoMany"), args);

    /// <summary>Gets the "ProgressLiveDocumentOne" text (T-6b build progress).</summary>
    public static string ProgressLiveDocumentOne => Get(nameof(ProgressLiveDocumentOne));

    /// <summary>Formats the "ProgressLiveDocumentMany" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressLiveDocumentMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressLiveDocumentMany"), args);

    /// <summary>Formats the "ProgressLink" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressLink(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressLink"), args);

    /// <summary>Formats the "ProgressQrCaption" text (T-6b build progress).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatProgressQrCaption(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("ProgressQrCaption"), args);

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

    /// <summary>Formats the "MyPlayersHeader" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersHeader(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersHeader"), args);

    /// <summary>Formats the "MyPlayersSearching" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersSearching(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersSearching"), args);

    /// <summary>Formats the "MyPlayersEmpty" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersEmpty(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersEmpty"), args);

    /// <summary>Formats the "MyPlayersNoMatch" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersNoMatch(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersNoMatch"), args);

    /// <summary>Gets the "MyPlayersOnePlayer" text (T-10b chat).</summary>
    public static string MyPlayersOnePlayer => Get(nameof(MyPlayersOnePlayer));

    /// <summary>Formats the "MyPlayersManyPlayers" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersManyPlayers(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersManyPlayers"), args);

    /// <summary>Gets the "MyPlayersOneFile" text (T-10b chat).</summary>
    public static string MyPlayersOneFile => Get(nameof(MyPlayersOneFile));

    /// <summary>Formats the "MyPlayersManyFiles" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersManyFiles(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersManyFiles"), args);

    /// <summary>Gets the "MyPlayersFilterAll" text (T-10b chat).</summary>
    public static string MyPlayersFilterAll => Get(nameof(MyPlayersFilterAll));

    /// <summary>Gets the "MyPlayersFilterLive" text (T-10b chat).</summary>
    public static string MyPlayersFilterLive => Get(nameof(MyPlayersFilterLive));

    /// <summary>Gets the "MyPlayersFilterProcessing" text (T-10b chat).</summary>
    public static string MyPlayersFilterProcessing => Get(nameof(MyPlayersFilterProcessing));

    /// <summary>Gets the "MyPlayersFilterAttention" text (T-10b chat).</summary>
    public static string MyPlayersFilterAttention => Get(nameof(MyPlayersFilterAttention));

    /// <summary>Gets the "MyPlayersSearchButton" text (T-10b chat).</summary>
    public static string MyPlayersSearchButton => Get(nameof(MyPlayersSearchButton));

    /// <summary>Gets the "MyPlayersClearSearchButton" text (T-10b chat).</summary>
    public static string MyPlayersClearSearchButton => Get(nameof(MyPlayersClearSearchButton));

    /// <summary>Gets the "MyPlayersSearchPrompt" text (T-10b chat).</summary>
    public static string MyPlayersSearchPrompt => Get(nameof(MyPlayersSearchPrompt));

    /// <summary>Gets the "MyPlayersSearchTooLong" text (T-10b chat).</summary>
    public static string MyPlayersSearchTooLong => Get(nameof(MyPlayersSearchTooLong));

    /// <summary>Gets the "MyPlayersSearchBusy" text (T-10b chat).</summary>
    public static string MyPlayersSearchBusy => Get(nameof(MyPlayersSearchBusy));

    /// <summary>Gets the "MyPlayersNotFound" text (T-10b chat).</summary>
    public static string MyPlayersNotFound => Get(nameof(MyPlayersNotFound));

    /// <summary>Gets the "MyPlayersFilesButton" text (T-10b chat).</summary>
    public static string MyPlayersFilesButton => Get(nameof(MyPlayersFilesButton));

    /// <summary>Gets the "MyPlayersEditButton" text (T-10b chat).</summary>
    public static string MyPlayersEditButton => Get(nameof(MyPlayersEditButton));

    /// <summary>Gets the "MyPlayersBackToListButton" text (T-10b chat).</summary>
    public static string MyPlayersBackToListButton => Get(nameof(MyPlayersBackToListButton));

    /// <summary>Gets the "MyPlayersBackButton" text (T-10b chat).</summary>
    public static string MyPlayersBackButton => Get(nameof(MyPlayersBackButton));

    /// <summary>Formats the "MyPlayersCard" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersCard(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersCard"), args);

    /// <summary>Formats the "MyPlayersCardProcessing" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersCardProcessing(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersCardProcessing"), args);

    /// <summary>Formats the "MyPlayersCardFailed" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersCardFailed(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersCardFailed"), args);

    /// <summary>Formats the "MyPlayersCardFolders" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersCardFolders(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersCardFolders"), args);

    /// <summary>Gets the "MyPlayersCardNoFolders" text (T-10b chat).</summary>
    public static string MyPlayersCardNoFolders => Get(nameof(MyPlayersCardNoFolders));

    /// <summary>Formats the "MyPlayersCardVolleybox" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersCardVolleybox(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersCardVolleybox"), args);

    /// <summary>Formats the "MyPlayersCardAttention" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersCardAttention(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersCardAttention"), args);

    /// <summary>Formats the "MyPlayersCardMissing" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersCardMissing(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersCardMissing"), args);

    /// <summary>Gets the "MyPlayersCardFailedFiles" text (T-10b chat).</summary>
    public static string MyPlayersCardFailedFiles => Get(nameof(MyPlayersCardFailedFiles));

    /// <summary>Gets the "MyPlayersCardLostFolder" text (T-10b chat).</summary>
    public static string MyPlayersCardLostFolder => Get(nameof(MyPlayersCardLostFolder));

    /// <summary>Formats the "MyPlayersFilesHeader" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersFilesHeader(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersFilesHeader"), args);

    /// <summary>Formats the "MyPlayersFilesEmpty" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersFilesEmpty(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersFilesEmpty"), args);

    /// <summary>Formats the "MyPlayersFileLine" text (T-10b chat).</summary>
    /// <param name="args">The values to insert.</param>
    /// <returns>The text.</returns>
    public static string FormatMyPlayersFileLine(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("MyPlayersFileLine"), args);

    /// <summary>Gets the "MyPlayersFileHidden" text (T-10b chat).</summary>
    public static string MyPlayersFileHidden => Get(nameof(MyPlayersFileHidden));

    /// <summary>Formats the "LinkOpenedAlert" text: code, recipient, email, player names.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatLinkOpenedAlert(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("LinkOpenedAlert"), args);

    /// <summary>Gets the "LinkOpenedForwardSuffix" text.</summary>
    public static string LinkOpenedForwardSuffix => Get(nameof(LinkOpenedForwardSuffix));

    /// <summary>Formats the "DigestHeader" text: the day.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestHeader(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestHeader"), args);

    /// <summary>Gets the "DigestNoActivity" text.</summary>
    public static string DigestNoActivity => Get(nameof(DigestNoActivity));

    /// <summary>Formats the "DigestActivity" text: links, viewers, plays, minutes (each already worded).</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestActivity(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestActivity"), args);

    /// <summary>Formats the "DigestLinksOpenedOne" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestLinksOpenedOne(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestLinksOpenedOne"), args);

    /// <summary>Formats the "DigestLinksOpenedMany" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestLinksOpenedMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestLinksOpenedMany"), args);

    /// <summary>Formats the "DigestViewersOne" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestViewersOne(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestViewersOne"), args);

    /// <summary>Formats the "DigestViewersMany" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestViewersMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestViewersMany"), args);

    /// <summary>Formats the "DigestPlaysOne" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestPlaysOne(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestPlaysOne"), args);

    /// <summary>Formats the "DigestPlaysMany" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestPlaysMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestPlaysMany"), args);

    /// <summary>Formats the "DigestMinutes" text: minutes.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestMinutes(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestMinutes"), args);

    /// <summary>Formats the "DigestTopPlayers" text: the players, already worded.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestTopPlayers(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestTopPlayers"), args);

    /// <summary>Formats the "DigestTopPlayer" text: name, minutes.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestTopPlayer(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestTopPlayer"), args);

    /// <summary>Formats the "DigestWaiting" text: the waiting parts, already worded.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestWaiting(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestWaiting"), args);

    /// <summary>Gets the "DigestNothingWaiting" text.</summary>
    public static string DigestNothingWaiting => Get(nameof(DigestNothingWaiting));

    /// <summary>Formats the "DigestForwardsOne" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestForwardsOne(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestForwardsOne"), args);

    /// <summary>Formats the "DigestForwardsMany" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestForwardsMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestForwardsMany"), args);

    /// <summary>Formats the "DigestWaitingPlayersOne" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestWaitingPlayersOne(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestWaitingPlayersOne"), args);

    /// <summary>Formats the "DigestWaitingPlayersMany" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestWaitingPlayersMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestWaitingPlayersMany"), args);

    /// <summary>Formats the "DigestAccessRequestsOne" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestAccessRequestsOne(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestAccessRequestsOne"), args);

    /// <summary>Formats the "DigestAccessRequestsMany" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestAccessRequestsMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestAccessRequestsMany"), args);

    /// <summary>Formats the "DigestFailedEmailsOne" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestFailedEmailsOne(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestFailedEmailsOne"), args);

    /// <summary>Formats the "DigestFailedEmailsMany" text: count.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatDigestFailedEmailsMany(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("DigestFailedEmailsMany"), args);

    /// <summary>Gets the "ProgressReadyToPublish" text (T-11 progress message, admin owner).</summary>
    public static string ProgressReadyToPublish => Get(nameof(ProgressReadyToPublish));

    /// <summary>Gets the "PublishButton" text (T-11 Publish button).</summary>
    public static string PublishButton => Get(nameof(PublishButton));

    /// <summary>Gets the "PublishDone" text (T-11 answer to Publish).</summary>
    public static string PublishDone => Get(nameof(PublishDone));

    /// <summary>Gets the "PublishAlready" text (T-11 answer to Publish).</summary>
    public static string PublishAlready => Get(nameof(PublishAlready));

    /// <summary>Gets the "PublishPlayerGone" text (T-11 answer to Publish).</summary>
    public static string PublishPlayerGone => Get(nameof(PublishPlayerGone));

    /// <summary>Gets the "OnlyAdminsPublish" text (T-11 answer to Publish).</summary>
    public static string OnlyAdminsPublish => Get(nameof(OnlyAdminsPublish));

    /// <summary>Formats the "PublishIncomplete" text (T-11 answer to Publish): the missing details.</summary>
    /// <param name="args">The values for its placeholders.</param>
    /// <returns>The text.</returns>
    public static string FormatPublishIncomplete(params object[] args) => string.Format(CultureInfo.InvariantCulture, Get("PublishIncomplete"), args);

    /// <summary>Reads a text.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidOperationException">The key is missing from the resources.</exception>
    private static string Get(string key) =>
        _resources.GetString(key, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException($"Bot text '{key}' is missing.");
}
