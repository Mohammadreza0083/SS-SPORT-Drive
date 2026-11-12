using System.Globalization;
using System.Text;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Assets;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Players;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Conversations;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Chat;

/// <summary>
/// The callback data of the My players buttons (T-10b): <c>mp:l:{filter}:{page}:{search}</c> (a list page),
/// <c>mp:c:{id}:{filter}:{page}:{search}</c> (a player card), <c>mp:f:{id}:{page}</c> (a player's files), <c>mp:s</c>
/// (ask for a search), <c>mp:x</c> (cancel the search) and <c>mp:n</c> (the page label, which does nothing). A player id
/// is written as 22 URL-safe base64 characters and the search is cut to <see cref="MaxSearchBytes"/> bytes, so every
/// value fits Telegram's 64 bytes.
/// </summary>
public static class MyPlayersCallbacks
{
    /// <summary>The prefix of every My players button.</summary>
    public const string Prefix = "mp:";

    /// <summary>The data of the button that asks for a search.</summary>
    public const string Search = "mp:s";

    /// <summary>The data of the button that cancels the search question.</summary>
    public const string CancelSearch = "mp:x";

    /// <summary>The data of the page label, which only answers the press.</summary>
    public const string Nothing = "mp:n";

    /// <summary>How many UTF-8 bytes of a search a button keeps.</summary>
    public const int MaxSearchBytes = 24;

    /// <summary>Tells whether callback data belongs to My players.</summary>
    /// <param name="data">The callback data.</param>
    /// <returns><see langword="true"/> for My players buttons.</returns>
    public static bool IsMyPlayers(string data) => data.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Builds the data of a list page.</summary>
    /// <param name="filter">The filter chip.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="search">The search, or <see langword="null"/>.</param>
    /// <returns>The data.</returns>
    public static string List(PlayerListFilter filter, int page, string? search) =>
        $"{Prefix}l:{CodeOf(filter)}:{page.ToString(CultureInfo.InvariantCulture)}:{search}";

    /// <summary>Builds the data that opens a player's card.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="filter">The chip of the list the card was opened from.</param>
    /// <param name="page">The page of that list.</param>
    /// <param name="search">The search of that list, or <see langword="null"/>.</param>
    /// <returns>The data.</returns>
    public static string Card(Guid playerId, PlayerListFilter filter, int page, string? search) =>
        $"{Prefix}c:{ShortId(playerId)}:{CodeOf(filter)}:{page.ToString(CultureInfo.InvariantCulture)}:{search}";

    /// <summary>Builds the data of a player's files page.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="page">The page, from 1.</param>
    /// <returns>The data.</returns>
    public static string Files(Guid playerId, int page) => $"{Prefix}f:{ShortId(playerId)}:{page.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Cuts a search to the length a button keeps, on a character boundary.</summary>
    /// <param name="search">The typed search.</param>
    /// <returns>The trimmed search, at most <see cref="MaxSearchBytes"/> bytes.</returns>
    public static string Shorten(string search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in search.Trim().EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > MaxSearchBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString().Trim();
    }

    /// <summary>Reads a list button's data.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="filter">The filter chip.</param>
    /// <param name="page">The page.</param>
    /// <param name="search">The search, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the data is well formed.</returns>
    public static bool TryParseList(string data, out PlayerListFilter filter, out int page, out string? search)
    {
        var parts = Split(data, "l", 4);
        filter = default;
        page = 0;
        search = null;
        if (parts is null || !TryFilter(parts[1], out filter) || !TryPage(parts[2], out page))
        {
            return false;
        }

        search = parts[3].Length == 0 ? null : parts[3];
        return true;
    }

    /// <summary>Reads a card button's data.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="filter">The filter chip of the list the card was opened from.</param>
    /// <param name="page">The page of that list.</param>
    /// <param name="search">The search of that list, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the data is well formed.</returns>
    public static bool TryParseCard(string data, out Guid playerId, out PlayerListFilter filter, out int page, out string? search)
    {
        var parts = Split(data, "c", 5);
        playerId = Guid.Empty;
        filter = default;
        page = 0;
        search = null;
        if (parts is null || !TryId(parts[1], out playerId) || !TryFilter(parts[2], out filter) || !TryPage(parts[3], out page))
        {
            return false;
        }

        search = parts[4].Length == 0 ? null : parts[4];
        return true;
    }

    /// <summary>Reads a files button's data.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="page">The page.</param>
    /// <returns><see langword="true"/> when the data is well formed.</returns>
    public static bool TryParseFiles(string data, out Guid playerId, out int page)
    {
        var parts = Split(data, "f", 3);
        playerId = Guid.Empty;
        page = 0;
        return parts is not null && TryId(parts[1], out playerId) && TryPage(parts[2], out page);
    }

    /// <summary>Gets the one-letter code of a filter chip.</summary>
    /// <param name="filter">The chip.</param>
    /// <returns><c>a</c>, <c>l</c>, <c>p</c> or <c>n</c>.</returns>
    private static string CodeOf(PlayerListFilter filter) => filter switch
    {
        PlayerListFilter.Live => "l",
        PlayerListFilter.Processing => "p",
        PlayerListFilter.NeedsAttention => "n",
        _ => "a",
    };

    /// <summary>Reads a filter chip from its code.</summary>
    /// <param name="code">The code.</param>
    /// <param name="filter">The chip.</param>
    /// <returns><see langword="true"/> for a known code.</returns>
    private static bool TryFilter(string code, out PlayerListFilter filter)
    {
        filter = code switch
        {
            "a" => PlayerListFilter.All,
            "l" => PlayerListFilter.Live,
            "p" => PlayerListFilter.Processing,
            "n" => PlayerListFilter.NeedsAttention,
            _ => (PlayerListFilter)(-1),
        };
        return code is "a" or "l" or "p" or "n";
    }

    /// <summary>Reads a page number.</summary>
    /// <param name="text">The text.</param>
    /// <param name="page">The page, from 1.</param>
    /// <returns><see langword="true"/> for a number from 1 to 9999.</returns>
    private static bool TryPage(string text, out int page) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page is >= 1 and <= 9999;

    /// <summary>Splits a button's data into its parts when it has the action and the part count.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="action">The action letter.</param>
    /// <param name="count">How many parts, the action included, the data has at most.</param>
    /// <returns>The parts, or <see langword="null"/> when the data is not for the action or is cut short.</returns>
    private static string[]? Split(string data, string action, int count)
    {
        if (!IsMyPlayers(data))
        {
            return null;
        }

        var parts = data[Prefix.Length..].Split(':', count);
        return parts.Length == count && parts[0] == action ? parts : null;
    }

    /// <summary>Writes a player id as 22 URL-safe base64 characters.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The short id.</returns>
    private static string ShortId(Guid id) => Convert.ToBase64String(id.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Reads a short player id.</summary>
    /// <param name="text">The short id.</param>
    /// <param name="id">The id.</param>
    /// <returns><see langword="true"/> for a valid short id.</returns>
    private static bool TryId(string text, out Guid id)
    {
        id = Guid.Empty;
        if (text.Length != 22)
        {
            return false;
        }

        var bytes = new byte[16];
        if (!Convert.TryFromBase64String(text.Replace('-', '+').Replace('_', '/') + "==", bytes, out var written) || written != 16)
        {
            return false;
        }

        id = new Guid(bytes);
        return true;
    }
}

/// <summary>
/// One My players input from the bot chat (T-10b): the My players button or <c>/players</c> (<see cref="ChatInputKind.Start"/>),
/// one of the list's buttons, or the typed search. The answer is <see langword="null"/> when the input is not for My players.
/// </summary>
/// <param name="From">The sender.</param>
/// <param name="Input">What the sender did.</param>
public sealed record ChatMyPlayersCommand(TelegramSender From, ChatInput Input) : ICommand<BotChatReply?>
{
    /// <summary>The longest search text.</summary>
    public const int MaxSearchLength = GetMyPlayersQuery.MaxSearchLength;

    /// <summary>How many players or files one page holds; a button each, well under Telegram's limit.</summary>
    public const int PageSize = 8;
}

/// <summary>Bounds the input; Telegram allows at most 4096 characters per message and 64 bytes per button.</summary>
public sealed class ChatMyPlayersCommandValidator : AbstractValidator<ChatMyPlayersCommand>
{
    /// <summary>Builds the rules.</summary>
    public ChatMyPlayersCommandValidator()
    {
        RuleFor(command => command.Input.Text).MaximumLength(4096);
        RuleFor(command => command.Input.Data).MaximumLength(64);
    }
}

/// <summary>
/// Handles <see cref="ChatMyPlayersCommand"/> (T-10b, owner-approved 2026-11-12): a paged list of the analyst's players
/// (every player for an admin) with filter chips and a search, a player card, and the player's files in page order. The
/// rules are the Mini App's: the list is read by the same <see cref="IPlayerListReader"/> and the files by the same
/// specification, and every page is read in SQL (decisions §2b). A search waits for typed text in a
/// <see cref="BotConversation"/> at <see cref="NewPlayerStep.PlayersSearch"/>, but never replaces an unfinished new player.
/// </summary>
/// <param name="users">The team.</param>
/// <param name="conversations">The open conversations.</param>
/// <param name="reader">The player list read model.</param>
/// <param name="players">The players.</param>
/// <param name="folders">The linked Drive folders.</param>
/// <param name="pages">Reads pages of files in the database.</param>
/// <param name="miniApp">Links to Mini App pages.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ChatMyPlayersCommandHandler(
    IRepository<User, Guid> users,
    IRepository<BotConversation, Guid> conversations,
    IPlayerListReader reader,
    IRepository<Player, Guid> players,
    IRepository<DriveFolder, Guid> folders,
    IPagedReader pages,
    IMiniAppLinks miniApp,
    TimeProvider timeProvider) : ICommandHandler<ChatMyPlayersCommand, BotChatReply?>
{
    /// <summary>How many folder names a card lists.</summary>
    private const int CardFolders = 3;

    /// <summary>Handles one input.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The bot's answer, or <see langword="null"/> when the input is not for My players.</returns>
    public async Task<BotChatReply?> Handle(ChatMyPlayersCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = request.Input;
        var user = await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(request.From.UserId), cancellationToken);
        if (user is not { IsActive: true })
        {
            return input.Kind == ChatInputKind.Text ? null : new BotChatReply(BotTexts.ChatNeedsAccess);
        }

        var now = timeProvider.GetUtcNow();
        var conversation = await conversations.FirstOrDefaultAsync(new BotConversationByTelegramUserSpec(request.From.UserId), cancellationToken);
        if (conversation is not null && conversation.IsExpired(now))
        {
            conversations.Remove(conversation);
            conversation = null;
        }

        var searching = conversation?.Step == NewPlayerStep.PlayersSearch;
        switch (input.Kind)
        {
            case ChatInputKind.Text:
                return searching ? await OnSearchTextAsync(user, conversation!, input.Text ?? string.Empty, cancellationToken) : null;
            case ChatInputKind.Start:
                CloseSearch(conversation, searching);
                return await ListAsync(user, PlayerListFilter.All, 1, null, cancellationToken);
            case ChatInputKind.Button:
                return await OnButtonAsync(user, conversation, searching, input.Data ?? string.Empty, now, cancellationToken);
            default:
                return null;
        }
    }

    /// <summary>Handles a My players button.</summary>
    /// <param name="user">The team member.</param>
    /// <param name="conversation">The open conversation, if any.</param>
    /// <param name="searching">Whether the conversation waits for a search.</param>
    /// <param name="data">The button data.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The answer.</returns>
    private async Task<BotChatReply> OnButtonAsync(User user, BotConversation? conversation, bool searching, string data, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (data == MyPlayersCallbacks.Nothing)
        {
            return new BotChatReply(null);
        }

        if (data == MyPlayersCallbacks.Search)
        {
            return AskSearch(user, conversation, now);
        }

        if (data == MyPlayersCallbacks.CancelSearch)
        {
            CloseSearch(conversation, searching);
            return await ListAsync(user, PlayerListFilter.All, 1, null, cancellationToken);
        }

        if (MyPlayersCallbacks.TryParseList(data, out var filter, out var page, out var search))
        {
            CloseSearch(conversation, searching);
            return await ListAsync(user, filter, page, search, cancellationToken);
        }

        if (MyPlayersCallbacks.TryParseCard(data, out var playerId, out var cardFilter, out var cardPage, out var cardSearch))
        {
            CloseSearch(conversation, searching);
            return await CardAsync(playerId, cardFilter, cardPage, cardSearch, cancellationToken);
        }

        if (MyPlayersCallbacks.TryParseFiles(data, out var filesPlayerId, out var filesPage))
        {
            CloseSearch(conversation, searching);
            return await FilesAsync(filesPlayerId, filesPage, cancellationToken);
        }

        return new BotChatReply(null, Toast: BotTexts.ChatStaleButton);
    }

    /// <summary>Asks for the search text, unless a new player is unfinished.</summary>
    /// <param name="user">The team member.</param>
    /// <param name="conversation">The open conversation, if any.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The question, or a note that the new player comes first.</returns>
    private BotChatReply AskSearch(User user, BotConversation? conversation, DateTimeOffset now)
    {
        if (conversation is { Step: not NewPlayerStep.PlayersSearch })
        {
            return new BotChatReply(null, Toast: BotTexts.MyPlayersSearchBusy);
        }

        if (conversation is null)
        {
            conversations.Add(BotConversation.StartPlayersSearch(user.TelegramId, now));
        }
        else
        {
            conversation.MoveTo(NewPlayerStep.PlayersSearch, new NewPlayerDraft(), now);
        }

        return new BotChatReply(BotTexts.MyPlayersSearchPrompt, BotKeyboard.Column(new BotButton(BotTexts.ChatCancelButton, MyPlayersCallbacks.CancelSearch)));
    }

    /// <summary>Takes the typed search and lists the matching players.</summary>
    /// <param name="user">The team member.</param>
    /// <param name="conversation">The conversation that waits for the search.</param>
    /// <param name="text">The typed text.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The first page of the matches, or why the search was not taken.</returns>
    private async Task<BotChatReply> OnSearchTextAsync(User user, BotConversation conversation, string text, CancellationToken cancellationToken)
    {
        if (text.Length > ChatMyPlayersCommand.MaxSearchLength)
        {
            return new BotChatReply(BotTexts.MyPlayersSearchTooLong);
        }

        conversations.Remove(conversation);
        var search = MyPlayersCallbacks.Shorten(text);
        return await ListAsync(user, PlayerListFilter.All, 1, search.Length == 0 ? null : search, cancellationToken);
    }

    /// <summary>Forgets an open search question, because the user went on to something else.</summary>
    /// <param name="conversation">The open conversation, if any.</param>
    /// <param name="searching">Whether it waits for a search.</param>
    private void CloseSearch(BotConversation? conversation, bool searching)
    {
        if (searching && conversation is not null)
        {
            conversations.Remove(conversation);
        }
    }

    /// <summary>Builds one page of the list.</summary>
    /// <param name="user">The team member: an analyst sees their own players, an admin everyone's.</param>
    /// <param name="filter">The filter chip.</param>
    /// <param name="page">The page, from 1; a page past the end shows the last one.</param>
    /// <param name="search">The search, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The list message.</returns>
    private async Task<BotChatReply> ListAsync(User user, PlayerListFilter filter, int page, string? search, CancellationToken cancellationToken)
    {
        var criteria = new PlayerListCriteria(user.IsAdmin ? null : user.Id, search, filter);
        var result = await reader.PageAsync(criteria, new PageRequest(page, ChatMyPlayersCommand.PageSize), cancellationToken);
        if (result.Items.Count == 0 && result.TotalCount > 0)
        {
            page = result.TotalPages;
            result = await reader.PageAsync(criteria, new PageRequest(page, ChatMyPlayersCommand.PageSize), cancellationToken);
        }

        var filterName = FilterName(filter);
        var text = result.TotalCount switch
        {
            0 when search is not null => BotTexts.FormatMyPlayersNoMatch(filterName, search),
            0 => BotTexts.FormatMyPlayersEmpty(filterName),
            _ => BotTexts.FormatMyPlayersHeader(filterName, Plural(result.TotalCount, BotTexts.MyPlayersOnePlayer, BotTexts.FormatMyPlayersManyPlayers(result.TotalCount)), page, result.TotalPages)
                + (search is null ? string.Empty : "\n" + BotTexts.FormatMyPlayersSearching(search)),
        };

        var rows = new List<IReadOnlyList<BotButton>>();
        foreach (var item in result.Items)
        {
            rows.Add([new BotButton(PlayerLabel(item), MyPlayersCallbacks.Card(item.Id, filter, page, search))]);
        }

        if (result.TotalPages > 1)
        {
            var pager = new List<BotButton>();
            if (page > 1)
            {
                pager.Add(new BotButton("◀", MyPlayersCallbacks.List(filter, page - 1, search)));
            }

            pager.Add(new BotButton($"{page}/{result.TotalPages}", MyPlayersCallbacks.Nothing));
            if (page < result.TotalPages)
            {
                pager.Add(new BotButton("▶", MyPlayersCallbacks.List(filter, page + 1, search)));
            }

            rows.Add(pager);
        }

        rows.Add([Chip(PlayerListFilter.All, filter, search), Chip(PlayerListFilter.Live, filter, search)]);
        rows.Add([Chip(PlayerListFilter.Processing, filter, search), Chip(PlayerListFilter.NeedsAttention, filter, search)]);
        var searchRow = new List<BotButton> { new(BotTexts.MyPlayersSearchButton, MyPlayersCallbacks.Search) };
        if (search is not null)
        {
            searchRow.Add(new BotButton(BotTexts.MyPlayersClearSearchButton, MyPlayersCallbacks.List(filter, 1, null)));
        }

        rows.Add(searchRow);
        if (miniApp.PageUrl("players") is { } url)
        {
            rows.Add([BotButton.OpenMiniApp(BotTexts.OpenMyPlayersButton, url)]);
        }

        return new BotChatReply(text, new BotKeyboard(rows));
    }

    /// <summary>Builds a player's card.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="filter">The chip of the list the card was opened from.</param>
    /// <param name="page">The page of that list.</param>
    /// <param name="search">The search of that list.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The card, or a note when the player does not exist.</returns>
    private async Task<BotChatReply> CardAsync(Guid playerId, PlayerListFilter filter, int page, string? search, CancellationToken cancellationToken)
    {
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(playerId), cancellationToken);
        var row = (await reader.PageAsync(new PlayerListCriteria(null, null, PlayerListFilter.All, playerId), new PageRequest(1, 1), cancellationToken)).Items.FirstOrDefault();
        if (player is null || row is null)
        {
            return new BotChatReply(null, Toast: BotTexts.MyPlayersNotFound);
        }

        var details = player.ToDetailsDto();
        var folderSpec = new PlayerFoldersSpec(playerId, CardFolders);
        var linked = await folders.ListAsync(folderSpec, cancellationToken);
        var folderCount = await folders.CountAsync(new PlayerFoldersSpec(playerId, int.MaxValue), cancellationToken);

        var lines = new List<string>();
        var profile = new List<string>();
        if (row.NationalityName is not null)
        {
            profile.Add(row.NationalityName);
        }

        if (row.Positions.Count > 0)
        {
            profile.Add(string.Join(" / ", row.Positions));
        }

        profile.Insert(0, row.Category);
        var counts = new List<string> { Plural(row.FileCount, BotTexts.MyPlayersOneFile, BotTexts.FormatMyPlayersManyFiles(row.FileCount)) };
        if (row.ProcessingCount > 0)
        {
            counts.Add(BotTexts.FormatMyPlayersCardProcessing(row.ProcessingCount));
        }

        if (row.FailedCount > 0)
        {
            counts.Add(BotTexts.FormatMyPlayersCardFailed(row.FailedCount));
        }

        lines.Add(counts[0] + (counts.Count > 1 ? " (" + string.Join(", ", counts.Skip(1)) + ")" : string.Empty));
        lines.Add(details.VolleyboxUrl is { } volleybox ? BotTexts.FormatMyPlayersCardVolleybox(volleybox) : BotTexts.FormatMyPlayersCardVolleybox("—"));
        lines.Add(folderCount == 0
            ? BotTexts.MyPlayersCardNoFolders
            : BotTexts.FormatMyPlayersCardFolders(string.Join(", ", linked.Select(folder => folder.Name)) + (folderCount > linked.Count ? $" +{folderCount - linked.Count}" : string.Empty)));
        var attention = Attention(row, details);
        if (attention.Length > 0)
        {
            lines.Add(BotTexts.FormatMyPlayersCardAttention(attention));
        }

        var text = BotTexts.FormatMyPlayersCard(row.Name ?? BotTexts.ProgressUnnamed, string.Join(" · ", profile), row.Status, string.Join("\n", lines));
        var rows = new List<IReadOnlyList<BotButton>> { new[] { new BotButton(BotTexts.MyPlayersFilesButton, MyPlayersCallbacks.Files(playerId, 1)) } };
        if (miniApp.PageUrl($"players/{playerId}") is { } url)
        {
            rows.Add([BotButton.OpenMiniApp(BotTexts.MyPlayersEditButton, url)]);
        }

        rows.Add([new BotButton(BotTexts.MyPlayersBackToListButton, MyPlayersCallbacks.List(filter, page, search))]);
        return new BotChatReply(text, new BotKeyboard(rows));
    }

    /// <summary>Builds one page of a player's files, in page order.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="page">The page, from 1; a page past the end shows the last one.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The files message, or a note when the player does not exist.</returns>
    private async Task<BotChatReply> FilesAsync(Guid playerId, int page, CancellationToken cancellationToken)
    {
        var player = await players.FirstOrDefaultAsync(new PlayerByIdReadOnlySpec(playerId), cancellationToken);
        if (player is null)
        {
            return new BotChatReply(null, Toast: BotTexts.MyPlayersNotFound);
        }

        var name = player.Name?.Value ?? BotTexts.ProgressUnnamed;
        var spec = new PlayerFilesInPageOrderSpec(playerId);
        var request = new PageRequest(page, ChatMyPlayersCommand.PageSize);
        Func<Domain.Assets.Asset, PlayerFileDto> map = asset => asset.ToFileDto() with { IsHero = asset.Id == player.HeroAssetId };
        var result = await pages.PageAsync(spec, request, map, cancellationToken);
        if (result.Items.Count == 0 && result.TotalCount > 0)
        {
            page = result.TotalPages;
            result = await pages.PageAsync(spec, new PageRequest(page, ChatMyPlayersCommand.PageSize), map, cancellationToken);
        }

        var back = new BotButton(BotTexts.MyPlayersBackButton, MyPlayersCallbacks.Card(playerId, PlayerListFilter.All, 1, null));
        if (result.TotalCount == 0)
        {
            return new BotChatReply(BotTexts.FormatMyPlayersFilesEmpty(name), BotKeyboard.Column(back));
        }

        var lines = new List<string> { BotTexts.FormatMyPlayersFilesHeader(name, page, result.TotalPages), string.Empty };
        for (var i = 0; i < result.Items.Count; i++)
        {
            var file = result.Items[i];
            var details = new List<string>();
            if (file.Tags.Count > 0)
            {
                details.Add(string.Join(", ", file.Tags));
            }

            details.Add(file.Visibility == "Hidden" ? file.State + " (" + BotTexts.MyPlayersFileHidden + ")" : file.State);
            lines.Add(BotTexts.FormatMyPlayersFileLine((page - 1) * ChatMyPlayersCommand.PageSize + i + 1, file.IsHero ? "★ " : string.Empty, file.Title, file.Kind, string.Join(" · ", details)));
        }

        var rows = new List<IReadOnlyList<BotButton>>();
        if (result.TotalPages > 1)
        {
            var pager = new List<BotButton>();
            if (page > 1)
            {
                pager.Add(new BotButton("◀", MyPlayersCallbacks.Files(playerId, page - 1)));
            }

            pager.Add(new BotButton($"{page}/{result.TotalPages}", MyPlayersCallbacks.Nothing));
            if (page < result.TotalPages)
            {
                pager.Add(new BotButton("▶", MyPlayersCallbacks.Files(playerId, page + 1)));
            }

            rows.Add(pager);
        }

        rows.Add([back]);
        return new BotChatReply(string.Join("\n", lines), new BotKeyboard(rows));
    }

    /// <summary>Builds a filter chip, marking the one in use.</summary>
    /// <param name="chip">The chip.</param>
    /// <param name="current">The chip in use.</param>
    /// <param name="search">The search, kept when the chip changes.</param>
    /// <returns>The button.</returns>
    private static BotButton Chip(PlayerListFilter chip, PlayerListFilter current, string? search) =>
        new((chip == current ? "• " : string.Empty) + FilterName(chip), MyPlayersCallbacks.List(chip, 1, search));

    /// <summary>Gets a filter chip's label.</summary>
    /// <param name="filter">The chip.</param>
    /// <returns>The label.</returns>
    private static string FilterName(PlayerListFilter filter) => filter switch
    {
        PlayerListFilter.Live => BotTexts.MyPlayersFilterLive,
        PlayerListFilter.Processing => BotTexts.MyPlayersFilterProcessing,
        PlayerListFilter.NeedsAttention => BotTexts.MyPlayersFilterAttention,
        _ => BotTexts.MyPlayersFilterAll,
    };

    /// <summary>Picks the singular text for one and the plural text otherwise.</summary>
    /// <param name="count">The count.</param>
    /// <param name="one">The text for one.</param>
    /// <param name="many">The text for any other count.</param>
    /// <returns>The text.</returns>
    private static string Plural(int count, string one, string many) => count == 1 ? one : many;

    /// <summary>Builds a player's button label: a status icon, the name, the country code, the positions and the file count.</summary>
    /// <param name="item">The player row.</param>
    /// <returns>The label.</returns>
    private static string PlayerLabel(PlayerListItemDto item)
    {
        var icon = item.NeedsAttention ? "⚠️" : item.ProcessingCount > 0 ? "⏳" : item.Status == nameof(PlayerStatus.Published) ? "🟢" : "⚪";
        var parts = new List<string> { item.Name ?? BotTexts.ProgressUnnamed };
        if (item.NationalityCode is not null)
        {
            parts.Add(item.NationalityCode);
        }

        if (item.Positions.Count > 0)
        {
            parts.Add(string.Join("/", item.Positions));
        }

        parts.Add(Plural(item.FileCount, BotTexts.MyPlayersOneFile, BotTexts.FormatMyPlayersManyFiles(item.FileCount)));
        return icon + " " + string.Join(" · ", parts);
    }

    /// <summary>Lists what a player still needs: missing details, a failed file or a Drive folder that lost access.</summary>
    /// <param name="row">The player row.</param>
    /// <param name="details">The player details, for the missing ones.</param>
    /// <returns>The reasons, or an empty text when nothing needs attention.</returns>
    private static string Attention(PlayerListItemDto row, PlayerDetailsDto details)
    {
        var reasons = new List<string>();
        if (details.MissingDetails.Count > 0)
        {
            reasons.Add(BotTexts.FormatMyPlayersCardMissing(string.Join(", ", details.MissingDetails.Select(missing => missing.ToLowerInvariant()))));
        }

        if (row.FailedCount > 0)
        {
            reasons.Add(BotTexts.MyPlayersCardFailedFiles);
        }

        if (row.HasLostFolder)
        {
            reasons.Add(BotTexts.MyPlayersCardLostFolder);
        }

        return string.Join("; ", reasons);
    }
}
