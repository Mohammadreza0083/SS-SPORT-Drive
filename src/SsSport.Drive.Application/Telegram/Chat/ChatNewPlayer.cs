using System.Globalization;
using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Images;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Conversations;
using SsSport.Drive.Domain.Countries;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Chat;

/// <summary>What the user did in the new-player chat.</summary>
public enum ChatInputKind
{
    /// <summary>The New player button or <c>/new</c>.</summary>
    Start,

    /// <summary><c>/cancel</c>.</summary>
    Cancel,

    /// <summary>A typed message.</summary>
    Text,

    /// <summary>A photo.</summary>
    Photo,

    /// <summary>One of the chat's buttons.</summary>
    Button,
}

/// <summary>One input to the new-player chat. Typed text is never logged.</summary>
/// <param name="Kind">What the user did.</param>
/// <param name="Text">The typed text, for <see cref="ChatInputKind.Text"/>.</param>
/// <param name="Photo">The photo, for <see cref="ChatInputKind.Photo"/>.</param>
/// <param name="Data">The button data, for <see cref="ChatInputKind.Button"/>.</param>
public sealed record ChatInput(ChatInputKind Kind, string? Text = null, IncomingPhoto? Photo = null, string? Data = null);

/// <summary>
/// What the bot answers: a message (sent, or edited in place for a button press) and/or a short note on the pressed
/// button.
/// </summary>
/// <param name="Text">The message, or <see langword="null"/> when only a note is shown.</param>
/// <param name="Keyboard">The message's buttons.</param>
/// <param name="Toast">A short note for the button press, or <see langword="null"/>.</param>
public sealed record BotChatReply(string? Text, BotKeyboard? Keyboard = null, string? Toast = null);

/// <summary>
/// The callback data of the chat's buttons: <c>np:{step}:{action}[:{argument}]</c>. The step a button belongs to is in
/// its data, so a button from an earlier question is recognised and ignored. Every value fits Telegram's 64 bytes.
/// </summary>
public static class ChatCallbacks
{
    /// <summary>The prefix of every chat button.</summary>
    public const string Prefix = "np:";

    /// <summary>Tells whether callback data belongs to the new-player chat.</summary>
    /// <param name="data">The callback data.</param>
    /// <returns><see langword="true"/> for chat buttons.</returns>
    public static bool IsChat(string data) => data.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Builds a button's data.</summary>
    /// <param name="step">The question the button belongs to.</param>
    /// <param name="action">The action, e.g. <c>cat</c>.</param>
    /// <param name="argument">The argument, e.g. <c>Men</c>, or <see langword="null"/>.</param>
    /// <returns>The data.</returns>
    public static string Of(NewPlayerStep step, string action, string? argument = null) =>
        argument is null ? $"{Prefix}{step}:{action}" : $"{Prefix}{step}:{action}:{argument}";

    /// <summary>Reads a button's data.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="step">The question the button belongs to.</param>
    /// <param name="action">The action.</param>
    /// <param name="argument">The argument, or an empty string.</param>
    /// <returns><see langword="true"/> when the data is well formed.</returns>
    public static bool TryParse(string data, out NewPlayerStep step, out string action, out string argument)
    {
        var parts = IsChat(data) ? data[Prefix.Length..].Split(':', 3) : [];
        action = parts.Length >= 2 ? parts[1] : string.Empty;
        argument = parts.Length == 3 ? parts[2] : string.Empty;
        step = default;
        return parts.Length >= 2 && Enum.TryParse(parts[0], out step) && Enum.IsDefined(step);
    }
}

/// <summary>A user's conversation with the bot, tracked for changes.</summary>
public sealed class BotConversationByTelegramUserSpec : Specification<BotConversation>, ISingleResultSpecification<BotConversation>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="telegramUserId">The user's Telegram id.</param>
    public BotConversationByTelegramUserSpec(long telegramUserId) => Query.Where(conversation => conversation.TelegramUserId == telegramUserId);
}

/// <summary>
/// One step of the new-player chat (T-10a, owner-approved 2026-11-06): the bot asks category, name, nationality,
/// positions, Volleybox, Drive folder and photo one by one, then shows a summary and creates the same Waiting player
/// the Mini App form does. The answer is <see langword="null"/> when the input is not for the chat (a message while no
/// question is open), so the bot handles it as before.
/// </summary>
/// <param name="From">The sender.</param>
/// <param name="Input">What the sender did.</param>
public sealed record ChatNewPlayerCommand(TelegramSender From, ChatInput Input) : ICommand<BotChatReply?>
{
    /// <summary>The longest typed answer the chat reads.</summary>
    public const int MaxTextLength = 500;
}

/// <summary>Bounds the chat's input; Telegram allows at most 4096 characters per message.</summary>
public sealed class ChatNewPlayerCommandValidator : AbstractValidator<ChatNewPlayerCommand>
{
    /// <summary>Builds the rules.</summary>
    public ChatNewPlayerCommandValidator()
    {
        RuleFor(command => command.Input.Text).MaximumLength(4096);
        RuleFor(command => command.Input.Data).MaximumLength(64);
    }
}

/// <summary>Handles <see cref="ChatNewPlayerCommand"/>; the transaction behaviour saves the draft and the new player together.</summary>
/// <param name="users">The team.</param>
/// <param name="conversations">The open conversations.</param>
/// <param name="players">The players.</param>
/// <param name="folders">The linked Drive folders.</param>
/// <param name="drive">Reads Drive folders with the service account.</param>
/// <param name="bot">Downloads photos.</param>
/// <param name="processor">Checks and converts photos.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="miniApp">Links to Mini App pages.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ChatNewPlayerCommandHandler(
    IRepository<User, Guid> users,
    IRepository<BotConversation, Guid> conversations,
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> folders,
    IDriveFolderReader drive,
    ITelegramMessenger bot,
    IPhotoProcessor processor,
    IObjectStorage storage,
    IMiniAppLinks miniApp,
    TimeProvider timeProvider) : ICommandHandler<ChatNewPlayerCommand, BotChatReply?>
{
    /// <summary>How many countries one search shows.</summary>
    private const int MaxCountries = 8;

    /// <summary>The positions in button order.</summary>
    private static readonly string[] _positionCodes = ["S", "OH", "OPP", "MB", "L", "DS"];

    /// <summary>The fields the summary can change, in order.</summary>
    private static readonly NewPlayerStep[] _editableFields =
        [NewPlayerStep.Category, NewPlayerStep.Name, NewPlayerStep.Nationality, NewPlayerStep.Positions, NewPlayerStep.Volleybox, NewPlayerStep.DriveFolder, NewPlayerStep.Photo];

    /// <summary>Runs one step of the chat.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The bot's answer, or <see langword="null"/> when the input is not for the chat.</returns>
    public async Task<BotChatReply?> Handle(ChatNewPlayerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = request.Input;
        var user = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(request.From.UserId), cancellationToken);
        if (user is not { IsActive: true })
        {
            return input.Kind is ChatInputKind.Text or ChatInputKind.Photo ? null : new BotChatReply(BotTexts.ChatNeedsAccess);
        }

        var now = timeProvider.GetUtcNow();
        var conversation = await conversations.FirstOrDefaultAsync(new BotConversationByTelegramUserSpec(request.From.UserId), cancellationToken);
        if (conversation is not null && conversation.IsExpired(now))
        {
            conversations.Remove(conversation);
            conversation = null;
        }

        return input.Kind switch
        {
            ChatInputKind.Start => Start(request.From.UserId, conversation, now),
            ChatInputKind.Cancel => Cancel(conversation),
            ChatInputKind.Button => await OnButtonAsync(user, conversation, input.Data ?? string.Empty, now, cancellationToken),
            ChatInputKind.Photo when conversation?.Step == NewPlayerStep.Photo => await OnPhotoAsync(conversation, input.Photo!, now, cancellationToken),
            ChatInputKind.Text when conversation is not null => await OnTextAsync(conversation, input.Text ?? string.Empty, now, cancellationToken),
            _ => null,
        };
    }

    /// <summary>Starts the chat, or asks whether to continue an unfinished draft.</summary>
    /// <param name="telegramUserId">The user.</param>
    /// <param name="conversation">The open conversation, if any.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The first question, or the continue question.</returns>
    private BotChatReply Start(long telegramUserId, BotConversation? conversation, DateTimeOffset now)
    {
        if (conversation is { Draft.HasAnswers: true })
        {
            return new BotChatReply(BotTexts.ChatResume, BotKeyboard.Column(
                new BotButton(BotTexts.ChatContinueButton, ChatCallbacks.Of(conversation.Step, "resume")),
                new BotButton(BotTexts.ChatStartOverButton, ChatCallbacks.Of(conversation.Step, "restart"))));
        }

        if (conversation is null)
        {
            conversation = BotConversation.StartNewPlayer(telegramUserId, now);
            conversations.Add(conversation);
        }
        else
        {
            conversation.MoveTo(NewPlayerStep.Category, new NewPlayerDraft(), now);
        }

        return Prompt(conversation);
    }

    /// <summary>Drops the draft.</summary>
    /// <param name="conversation">The open conversation, if any.</param>
    /// <returns>The confirmation.</returns>
    private BotChatReply Cancel(BotConversation? conversation)
    {
        if (conversation is null)
        {
            return new BotChatReply(BotTexts.ChatNothingToCancel);
        }

        conversations.Remove(conversation);
        return new BotChatReply(BotTexts.ChatCancelled);
    }

    /// <summary>Handles a chat button.</summary>
    /// <param name="user">The team member.</param>
    /// <param name="conversation">The open conversation, if any.</param>
    /// <param name="data">The button data.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The answer.</returns>
    private async Task<BotChatReply> OnButtonAsync(User user, BotConversation? conversation, string data, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!ChatCallbacks.TryParse(data, out var step, out var action, out var argument) || conversation is null)
        {
            return new BotChatReply(null, Toast: BotTexts.ChatStaleButton);
        }

        if (action == "cancel")
        {
            return Cancel(conversation);
        }

        if (step != conversation.Step)
        {
            return new BotChatReply(null, Toast: BotTexts.ChatStaleButton);
        }

        var draft = conversation.Draft;
        switch (action)
        {
            case "resume":
                return Prompt(conversation);
            case "restart":
                conversation.MoveTo(NewPlayerStep.Category, new NewPlayerDraft(), now);
                return Prompt(conversation);
            case "back":
                var previous = Previous(conversation.Step, draft);
                return MoveAndPrompt(conversation, previous, draft with { ReturnToSummary = false }, now);
            case "cat" when step == NewPlayerStep.Category && argument is "Men" or "Women":
                return Answered(conversation, draft with { Category = argument }, NewPlayerStep.Name, now);
            case "nat" when step == NewPlayerStep.Nationality && CountryCatalogue.TryFind(argument, out _):
                return Answered(conversation, draft with { NationalityCode = Nationality.Create(argument).Code }, NewPlayerStep.Positions, now);
            case "pos" when step == NewPlayerStep.Positions && _positionCodes.Contains(argument):
                return TogglePosition(conversation, argument, now);
            case "done" when step == NewPlayerStep.Positions:
                return draft.Positions.Count == 0
                    ? new BotChatReply(null, Toast: BotTexts.ChatPositionsNone)
                    : Answered(conversation, draft, NewPlayerStep.Volleybox, now);
            case "addfolder" when step == NewPlayerStep.ExistingPlayer:
                return MoveAndPrompt(conversation, NewPlayerStep.DriveFolder, draft, now);
            case "otherlink" when step == NewPlayerStep.ExistingPlayer:
                return MoveAndPrompt(conversation, NewPlayerStep.Volleybox, draft with { ExistingPlayerId = null, ExistingPlayerName = null }, now);
            case "skip" when step == NewPlayerStep.Photo:
                return Answered(conversation, draft with { PhotoFileId = null }, NewPlayerStep.Summary, now);
            case "edit" when step == NewPlayerStep.Summary:
                return MoveAndPrompt(conversation, NewPlayerStep.EditField, draft, now);
            case "field" when step == NewPlayerStep.EditField && Enum.TryParse<NewPlayerStep>(argument, out var field) && _editableFields.Contains(field):
                return MoveAndPrompt(conversation, field, draft with { ReturnToSummary = true }, now);
            case "create" when step == NewPlayerStep.Summary:
                return await CreateAsync(user, conversation, cancellationToken);
            default:
                return new BotChatReply(null, Toast: BotTexts.ChatStaleButton);
        }
    }

    /// <summary>Handles a typed answer.</summary>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="text">The text.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The answer.</returns>
    private async Task<BotChatReply> OnTextAsync(BotConversation conversation, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (text.Length > ChatNewPlayerCommand.MaxTextLength)
        {
            return new BotChatReply(BotTexts.ChatTooLong);
        }

        var draft = conversation.Draft;
        try
        {
            switch (conversation.Step)
            {
                case NewPlayerStep.Name:
                    return Answered(conversation, draft with { Name = FullName.Create(text).Value }, NewPlayerStep.Nationality, now);
                case NewPlayerStep.Nationality:
                    return Countries(text);
                case NewPlayerStep.Volleybox:
                    return await OnVolleyboxAsync(conversation, text, now, cancellationToken);
                case NewPlayerStep.DriveFolder:
                    return await OnDriveFolderAsync(conversation, text, now, cancellationToken);
                case NewPlayerStep.Photo:
                    return new BotChatReply(BotTexts.ChatSendPhotoOrSkip);
                default:
                    return new BotChatReply(BotTexts.ChatUseButtons);
            }
        }
        catch (DomainException exception)
        {
            return new BotChatReply(BotTexts.FormatChatProblem(exception.Message));
        }
        catch (ServiceUnavailableException)
        {
            return new BotChatReply(BotTexts.ChatDriveUnavailable);
        }
    }

    /// <summary>Checks a Volleybox link; a known player gets the T-5 choices.</summary>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="text">The link.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The next question.</returns>
    private async Task<BotChatReply> OnVolleyboxAsync(BotConversation conversation, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var check = await PlayerUniqueness.CheckVolleyboxAsync(players, text, cancellationToken);
        var draft = conversation.Draft;
        if (check.ExistingPlayerId is { } existingId)
        {
            return MoveAndPrompt(conversation, NewPlayerStep.ExistingPlayer, draft with { ExistingPlayerId = existingId, ExistingPlayerName = check.ExistingPlayerName ?? BotTexts.ProgressUnnamed }, now);
        }

        return Answered(conversation, draft with { VolleyboxUrl = check.Url, ExistingPlayerId = null, ExistingPlayerName = null }, NewPlayerStep.DriveFolder, now);
    }

    /// <summary>Checks a Drive folder link; adds it to a known player, or keeps it for the new one.</summary>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="text">The link.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The next question, or the confirmation for a known player.</returns>
    private async Task<BotChatReply> OnDriveFolderAsync(BotConversation conversation, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var check = await DriveFolderChecks.CheckAsync(folders, players, drive, text, cancellationToken);
        if (check.ExistingPlayerId is not null)
        {
            return new BotChatReply(BotTexts.FormatChatDriveTaken(check.ExistingPlayerName ?? BotTexts.ProgressUnnamed));
        }

        var draft = conversation.Draft;
        if (draft.ExistingPlayerId is { } playerId)
        {
            var folder = await PlayerFolders.AddAsync(players, folders, drive, playerId, text, now, cancellationToken);
            conversations.Remove(conversation);
            return new BotChatReply(BotTexts.FormatChatFolderAdded(draft.ExistingPlayerName ?? BotTexts.ProgressUnnamed, folder.Name));
        }

        var found = BotTexts.FormatChatDriveFound(check.Name, check.VideoCount, check.DocumentCount, check.ScoutCount, FormatSize(check.TotalBytes));
        var next = Answered(conversation, draft with { DriveFolderUrl = text.Trim(), DriveFolderName = check.Name }, NewPlayerStep.Photo, now);
        return next with { Text = found + "\n\n" + next.Text };
    }

    /// <summary>Checks a photo and keeps it for the new player.</summary>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="photo">The photo.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The summary, or why the photo cannot be used.</returns>
    private async Task<BotChatReply> OnPhotoAsync(BotConversation conversation, IncomingPhoto photo, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (photo.FileSize > UploadPlayerPhotoCommand.MaxBytes)
        {
            return new BotChatReply(BotTexts.PhotoTooLarge);
        }

        var content = await bot.DownloadFileAsync(photo.FileId, cancellationToken);
        if (content.Length > UploadPlayerPhotoCommand.MaxBytes)
        {
            return new BotChatReply(BotTexts.PhotoTooLarge);
        }

        try
        {
            processor.Process(new MemoryStream(content, writable: false));
        }
        catch (DomainException exception) when (exception.Code is "player.photo_too_small" or "player.photo_unreadable")
        {
            return new BotChatReply(exception.Code == "player.photo_too_small" ? BotTexts.PhotoTooSmall : BotTexts.PhotoUnreadable);
        }

        return Answered(conversation, conversation.Draft with { PhotoFileId = photo.FileId }, NewPlayerStep.Summary, now);
    }

    /// <summary>Creates the player from the draft, with the photo when one was given, and closes the conversation.</summary>
    /// <param name="user">The analyst.</param>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The confirmation, or why the player cannot be created yet.</returns>
    private async Task<BotChatReply> CreateAsync(User user, BotConversation conversation, CancellationToken cancellationToken)
    {
        var draft = conversation.Draft;
        var form = new CreatePlayerCommand(draft.Category, draft.Name, draft.NationalityCode, draft.Positions, draft.VolleyboxUrl, draft.DriveFolderUrl);
        if (!new CreatePlayerCommandValidator().Validate(form).IsValid)
        {
            return new BotChatReply(BotTexts.ChatIncomplete);
        }

        Player player;
        try
        {
            player = await PlayerCreation.CreateAsync(form, user.Id, players, folders, drive, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (DomainException exception)
        {
            return new BotChatReply(BotTexts.FormatChatProblem(exception.Message), SummaryKeyboard());
        }
        catch (ServiceUnavailableException)
        {
            return new BotChatReply(BotTexts.ChatDriveUnavailable, SummaryKeyboard());
        }

        if (draft.PhotoFileId is { } fileId)
        {
            var content = await bot.DownloadFileAsync(fileId, cancellationToken);
            await PlayerPhotos.SaveAsync(player, content, processor, storage, timeProvider, cancellationToken);
        }

        conversations.Remove(conversation);
        return new BotChatReply(BotTexts.FormatChatCreated(player.Name!.Value));
    }

    /// <summary>Adds or removes a position, at most two, keeping the order they were picked in (primary first).</summary>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="code">The position code.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The positions question with the new choice.</returns>
    private BotChatReply TogglePosition(BotConversation conversation, string code, DateTimeOffset now)
    {
        var chosen = conversation.Draft.Positions.ToList();
        if (!chosen.Remove(code))
        {
            if (chosen.Count >= PlayerPositions.MaxCount)
            {
                return new BotChatReply(null, Toast: BotTexts.ChatPositionsMax);
            }

            chosen.Add(code);
        }

        return MoveAndPrompt(conversation, NewPlayerStep.Positions, conversation.Draft with { Positions = chosen }, now);
    }

    /// <summary>Saves an answer and moves to the next question, or back to the summary when the answer was an edit.</summary>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="draft">The draft with the answer.</param>
    /// <param name="next">The next question in the flow.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The next question.</returns>
    private BotChatReply Answered(BotConversation conversation, NewPlayerDraft draft, NewPlayerStep next, DateTimeOffset now) =>
        draft.ReturnToSummary
            ? MoveAndPrompt(conversation, NewPlayerStep.Summary, draft with { ReturnToSummary = false }, now)
            : MoveAndPrompt(conversation, next, draft, now);

    /// <summary>Moves to a question and asks it.</summary>
    /// <param name="conversation">The open conversation.</param>
    /// <param name="step">The question.</param>
    /// <param name="draft">The answers so far.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The question.</returns>
    private BotChatReply MoveAndPrompt(BotConversation conversation, NewPlayerStep step, NewPlayerDraft draft, DateTimeOffset now)
    {
        conversation.MoveTo(step, draft, now);
        return Prompt(conversation);
    }

    /// <summary>Asks the question the conversation waits at.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <returns>The question and its buttons.</returns>
    private BotChatReply Prompt(BotConversation conversation)
    {
        var step = conversation.Step;
        var draft = conversation.Draft;
        var navigation = NavigationRow(step, draft);
        return step switch
        {
            NewPlayerStep.Category => new BotChatReply(BotTexts.ChatCategoryPrompt, CategoryKeyboard(draft)),
            NewPlayerStep.Name => new BotChatReply(BotTexts.ChatNamePrompt, new BotKeyboard([navigation])),
            NewPlayerStep.Nationality => new BotChatReply(BotTexts.ChatNationalityPrompt, new BotKeyboard([navigation])),
            NewPlayerStep.Positions => new BotChatReply(
                BotTexts.FormatChatPositionsPrompt(draft.Positions.Count == 0 ? BotTexts.ChatNoneChosen : string.Join(" / ", draft.Positions)),
                PositionsKeyboard(draft, navigation)),
            NewPlayerStep.Volleybox => new BotChatReply(BotTexts.ChatVolleyboxPrompt, new BotKeyboard([navigation])),
            NewPlayerStep.ExistingPlayer => new BotChatReply(BotTexts.FormatChatVolleyboxExisting(draft.ExistingPlayerName!), ExistingPlayerKeyboard(draft)),
            NewPlayerStep.DriveFolder => new BotChatReply(
                draft.ExistingPlayerId is null ? BotTexts.ChatDrivePrompt : BotTexts.FormatChatDriveForExistingPrompt(draft.ExistingPlayerName!),
                new BotKeyboard([navigation])),
            NewPlayerStep.Photo => new BotChatReply(BotTexts.ChatPhotoPrompt, new BotKeyboard([[new BotButton(BotTexts.ChatSkipButton, ChatCallbacks.Of(step, "skip"))], navigation])),
            NewPlayerStep.Summary => new BotChatReply(Summary(draft), SummaryKeyboard()),
            _ => new BotChatReply(BotTexts.ChatEditPick, EditFieldKeyboard(navigation)),
        };
    }

    /// <summary>Lists the countries whose name or code matches a typed search.</summary>
    /// <param name="search">The typed text.</param>
    /// <returns>The country buttons, or a note when nothing matches.</returns>
    private static BotChatReply Countries(string search)
    {
        var term = search.Trim();
        var matches = CountryCatalogue.All
            .Where(country => country.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || country.Code.Equals(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(country => country.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) || country.Code.Equals(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(country => country.Name, StringComparer.Ordinal)
            .Take(MaxCountries)
            .ToList();
        if (term.Length == 0 || matches.Count == 0)
        {
            return new BotChatReply(BotTexts.FormatChatNationalityNone(term));
        }

        var rows = matches
            .Select(country => new BotButton($"{Flag(country.Code)} {country.Name}", ChatCallbacks.Of(NewPlayerStep.Nationality, "nat", country.Code)))
            .Chunk(2)
            .Select(pair => (IReadOnlyList<BotButton>)pair)
            .ToList();
        rows.Add([new BotButton(BotTexts.ChatBackButton, ChatCallbacks.Of(NewPlayerStep.Nationality, "back")), CancelButton(NewPlayerStep.Nationality)]);
        return new BotChatReply(BotTexts.ChatNationalityPick, new BotKeyboard(rows));
    }

    /// <summary>Builds the summary of the draft.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The text.</returns>
    private static string Summary(NewPlayerDraft draft) => BotTexts.FormatChatSummary(
        draft.Category ?? "-",
        draft.Name ?? "-",
        draft.NationalityCode is { } code && CountryCatalogue.TryFind(code, out var country) ? $"{Flag(code)} {country!.Name}" : "-",
        draft.Positions.Count == 0 ? "-" : string.Join(" / ", draft.Positions),
        draft.VolleyboxUrl ?? "-",
        draft.DriveFolderName ?? "-",
        draft.PhotoFileId is null ? BotTexts.ChatPhotoLater : BotTexts.ChatPhotoAdded);

    /// <summary>Tells which question Back returns to.</summary>
    /// <param name="step">The current question.</param>
    /// <param name="draft">The draft.</param>
    /// <returns>The previous question.</returns>
    private static NewPlayerStep Previous(NewPlayerStep step, NewPlayerDraft draft) => draft.ReturnToSummary ? NewPlayerStep.Summary : step switch
    {
        NewPlayerStep.Name => NewPlayerStep.Category,
        NewPlayerStep.Nationality => NewPlayerStep.Name,
        NewPlayerStep.Positions => NewPlayerStep.Nationality,
        NewPlayerStep.Volleybox => NewPlayerStep.Positions,
        NewPlayerStep.ExistingPlayer => NewPlayerStep.Volleybox,
        NewPlayerStep.DriveFolder when draft.ExistingPlayerId is not null => NewPlayerStep.ExistingPlayer,
        NewPlayerStep.DriveFolder => NewPlayerStep.Volleybox,
        NewPlayerStep.Photo => NewPlayerStep.DriveFolder,
        NewPlayerStep.EditField => NewPlayerStep.Summary,
        NewPlayerStep.Summary => NewPlayerStep.Photo,
        _ => NewPlayerStep.Category,
    };

    /// <summary>Builds the Back and Cancel row of a question.</summary>
    /// <param name="step">The question.</param>
    /// <param name="draft">The draft.</param>
    /// <returns>The row.</returns>
    private static IReadOnlyList<BotButton> NavigationRow(NewPlayerStep step, NewPlayerDraft draft) =>
        step == NewPlayerStep.Category && !draft.ReturnToSummary
            ? [CancelButton(step)]
            : [new BotButton(BotTexts.ChatBackButton, ChatCallbacks.Of(step, "back")), CancelButton(step)];

    /// <summary>Builds the Cancel button of a question.</summary>
    /// <param name="step">The question.</param>
    /// <returns>The button.</returns>
    private static BotButton CancelButton(NewPlayerStep step) => new(BotTexts.ChatCancelButton, ChatCallbacks.Of(step, "cancel"));

    /// <summary>Builds the category buttons, with "Open in Mini App" when the Mini App is set up.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The keyboard.</returns>
    private BotKeyboard CategoryKeyboard(NewPlayerDraft draft)
    {
        List<IReadOnlyList<BotButton>> rows =
        [
            [
                new BotButton(BotTexts.ChatMenButton, ChatCallbacks.Of(NewPlayerStep.Category, "cat", "Men")),
                new BotButton(BotTexts.ChatWomenButton, ChatCallbacks.Of(NewPlayerStep.Category, "cat", "Women")),
            ],
            NavigationRow(NewPlayerStep.Category, draft),
        ];
        if (miniApp.PageUrl("new") is { } url)
        {
            rows.Add([BotButton.OpenMiniApp(BotTexts.ChatOpenMiniAppButton, url)]);
        }

        return new BotKeyboard(rows);
    }

    /// <summary>Builds the position toggles (chosen ones ticked), Done, then Back and Cancel.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="navigation">The Back and Cancel row.</param>
    /// <returns>The keyboard.</returns>
    private static BotKeyboard PositionsKeyboard(NewPlayerDraft draft, IReadOnlyList<BotButton> navigation)
    {
        var toggles = _positionCodes
            .Select(code => new BotButton(draft.Positions.Contains(code) ? $"✅ {code}" : code, ChatCallbacks.Of(NewPlayerStep.Positions, "pos", code)))
            .Chunk(3)
            .Select(row => (IReadOnlyList<BotButton>)row);
        return new BotKeyboard([.. toggles, [new BotButton(BotTexts.ChatDoneButton, ChatCallbacks.Of(NewPlayerStep.Positions, "done"))], navigation]);
    }

    /// <summary>Builds the choices for a Volleybox link of a known player (T-5).</summary>
    /// <param name="draft">The draft, with the known player.</param>
    /// <returns>The keyboard.</returns>
    private BotKeyboard ExistingPlayerKeyboard(NewPlayerDraft draft)
    {
        const NewPlayerStep step = NewPlayerStep.ExistingPlayer;
        List<IReadOnlyList<BotButton>> rows = [[new BotButton(BotTexts.FormatChatAddFolderButton(draft.ExistingPlayerName!), ChatCallbacks.Of(step, "addfolder"))]];
        if (miniApp.PageUrl($"players/{draft.ExistingPlayerId}") is { } url)
        {
            rows.Add([BotButton.OpenMiniApp(BotTexts.ChatUpdateInMiniAppButton, url)]);
        }

        rows.Add([new BotButton(BotTexts.ChatOtherLinkButton, ChatCallbacks.Of(step, "otherlink"))]);
        rows.Add([CancelButton(step)]);
        return new BotKeyboard(rows);
    }

    /// <summary>Builds the summary's buttons.</summary>
    /// <returns>The keyboard.</returns>
    private static BotKeyboard SummaryKeyboard() => BotKeyboard.Column(
        new BotButton(BotTexts.ChatCreateButton, ChatCallbacks.Of(NewPlayerStep.Summary, "create")),
        new BotButton(BotTexts.ChatEditButton, ChatCallbacks.Of(NewPlayerStep.Summary, "edit")),
        CancelButton(NewPlayerStep.Summary));

    /// <summary>Builds the field buttons for changing one answer from the summary.</summary>
    /// <param name="navigation">The Back and Cancel row.</param>
    /// <returns>The keyboard.</returns>
    private static BotKeyboard EditFieldKeyboard(IReadOnlyList<BotButton> navigation)
    {
        var fields = _editableFields
            .Select(field => new BotButton(FieldLabel(field), ChatCallbacks.Of(NewPlayerStep.EditField, "field", field.ToString())))
            .Chunk(2)
            .Select(row => (IReadOnlyList<BotButton>)row);
        return new BotKeyboard([.. fields, navigation]);
    }

    /// <summary>Names a field on its edit button.</summary>
    /// <param name="field">The field's question.</param>
    /// <returns>The label.</returns>
    private static string FieldLabel(NewPlayerStep field) => field switch
    {
        NewPlayerStep.Category => BotTexts.ChatFieldCategory,
        NewPlayerStep.Name => BotTexts.ChatFieldName,
        NewPlayerStep.Nationality => BotTexts.ChatFieldNationality,
        NewPlayerStep.Positions => BotTexts.ChatFieldPositions,
        NewPlayerStep.Volleybox => BotTexts.ChatFieldVolleybox,
        NewPlayerStep.DriveFolder => BotTexts.ChatFieldDriveFolder,
        _ => BotTexts.ChatFieldPhoto,
    };

    /// <summary>Builds a country's flag from its two-letter code (regional indicator symbols).</summary>
    /// <param name="code">The code.</param>
    /// <returns>The flag.</returns>
    private static string Flag(string code) =>
        string.Concat(code.ToUpperInvariant().Select(letter => char.ConvertFromUtf32(0x1F1E6 + (letter - 'A'))));

    /// <summary>Formats a size in bytes for people (GB, MB or KB).</summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The text.</returns>
    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Format(CultureInfo.InvariantCulture, "{0:0.0} GB", bytes / (double)(1L << 30)),
        >= 1L << 20 => string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / (double)(1L << 20)),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:0} KB", bytes / 1024.0),
    };
}
