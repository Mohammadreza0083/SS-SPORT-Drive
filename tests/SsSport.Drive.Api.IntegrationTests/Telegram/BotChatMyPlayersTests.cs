using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Application.Telegram.Access;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// My players in the bot chat (T-10b, owner-approved 2026-11-12): the My players button and <c>/players</c> show a
/// paged list with filter chips and a search, a player opens as a card, and the card lists the files in page order.
/// The rules are the Mini App's: an analyst sees only their own players, an admin sees everyone's, and a button from an
/// earlier message is ignored.
/// </summary>
/// <param name="chat">The shared API.</param>
public sealed class BotChatMyPlayersTests(BotChatFixture chat) : IClassFixture<BotChatFixture>
{
    /// <summary>The update id counter.</summary>
    private static long _updateId = 80_000;

    /// <summary>The message id counter for messages users send.</summary>
    private static int _userMessageId = 8000;

    /// <summary>Counts seeded players so every name is its own.</summary>
    private static int _seeded;

    /// <summary>The My players button lists the analyst's own players, one button each, with the Mini App button below.</summary>
    [Fact]
    public async Task Button_lists_own_players_only()
    {
        var analyst = await chat.NewAnalystAsync();
        var other = await chat.NewAnalystAsync();
        var mine = await AddPlayerAsync(analyst);
        var theirs = await AddPlayerAsync(other);

        var list = await PressMenuAsync(analyst);

        list.Text.Should().Contain("My players");
        var buttons = list.Keyboard!.Rows.SelectMany(row => row).ToList();
        buttons.Should().Contain(button => button.Text.Contains(mine.Name, StringComparison.Ordinal));
        buttons.Should().NotContain(button => button.Text.Contains(theirs.Name, StringComparison.Ordinal));
        buttons.Should().Contain(button => button.WebAppUrl == BotChatFixture.MiniAppUrl + "/players");
    }

    /// <summary>The /players command answers with the same list.</summary>
    [Fact]
    public async Task Command_lists_the_players()
    {
        var analyst = await chat.NewAnalystAsync();
        var player = await AddPlayerAsync(analyst);

        await CommandAsync(analyst, "players");

        var list = Last(analyst);
        list.Kind.Should().Be("Send");
        list.Keyboard!.Rows.SelectMany(row => row).Should().Contain(button => button.Text.Contains(player.Name, StringComparison.Ordinal));
    }

    /// <summary>A list with no players says so and still offers the Mini App.</summary>
    [Fact]
    public async Task Empty_list_says_so()
    {
        var analyst = await chat.NewAnalystAsync();

        var list = await PressMenuAsync(analyst);

        list.Text.Should().Contain("No players");
        list.Keyboard!.Rows.SelectMany(row => row).Should().Contain(button => button.WebAppUrl == BotChatFixture.MiniAppUrl + "/players");
    }

    /// <summary>Eight players fill a page; the next page opens in the same message and goes back again.</summary>
    [Fact]
    public async Task Pages_edit_the_same_message()
    {
        var analyst = await chat.NewAnalystAsync();
        var names = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            names.Add((await AddPlayerAsync(analyst)).Name);
        }

        var first = await PressMenuAsync(analyst);

        PlayerButtons(first).Should().HaveCount(8);
        var second = await PressAsync(analyst, first, "▶");
        second.Kind.Should().Be("Edit");
        second.MessageId.Should().Be(first.MessageId);
        PlayerButtons(second).Should().HaveCount(2);
        PlayerButtons(first).Concat(PlayerButtons(second)).Select(button => button.Text).Should().OnlyHaveUniqueItems();
        var back = await PressAsync(analyst, second, "◀");
        PlayerButtons(back).Should().HaveCount(8);
    }

    /// <summary>The Processing chip keeps only players with a file in progress.</summary>
    [Fact]
    public async Task Processing_filter_keeps_players_with_files_in_progress()
    {
        var analyst = await chat.NewAnalystAsync();
        var busy = await AddPlayerAsync(analyst);
        var idle = await AddPlayerAsync(analyst);
        await AddAssetAsync(busy.Id, "highlights.mp4", 0);

        var all = await PressMenuAsync(analyst);
        var processing = await PressAsync(analyst, all, "Processing");

        processing.Kind.Should().Be("Edit");
        PlayerButtons(processing).Should().ContainSingle().Which.Text.Should().Contain(busy.Name);
        PlayerButtons(processing).Should().NotContain(button => button.Text.Contains(idle.Name, StringComparison.Ordinal));
    }

    /// <summary>The Live chip shows nothing while no player is published.</summary>
    [Fact]
    public async Task Live_filter_is_empty_while_nothing_is_published()
    {
        var analyst = await chat.NewAnalystAsync();
        await AddPlayerAsync(analyst);

        var live = await PressAsync(analyst, await PressMenuAsync(analyst), "Live");

        PlayerButtons(live).Should().BeEmpty();
        live.Text.Should().Contain("No players");
    }

    /// <summary>Search asks for the text, then lists the players whose name has it; the question is gone afterwards.</summary>
    [Fact]
    public async Task Search_lists_the_matching_players()
    {
        var analyst = await chat.NewAnalystAsync();
        var wanted = await AddPlayerAsync(analyst);
        var unwanted = await AddPlayerAsync(analyst);

        var prompt = await PressAsync(analyst, await PressMenuAsync(analyst), "Search");
        prompt.Kind.Should().Be("Edit");
        prompt.Text.Should().Contain("name");
        (await ConversationStepAsync(analyst)).Should().Be("PlayersSearch");
        var result = await TypeAsync(analyst, wanted.Name.ToUpperInvariant());

        result.Kind.Should().Be("Send");
        PlayerButtons(result).Should().ContainSingle().Which.Text.Should().Contain(wanted.Name);
        PlayerButtons(result).Should().NotContain(button => button.Text.Contains(unwanted.Name, StringComparison.Ordinal));
        result.Keyboard!.Rows.SelectMany(row => row).Should().Contain(button => button.Text.Contains("Clear search", StringComparison.Ordinal));
        (await ConversationCountAsync(analyst)).Should().Be(0);
    }

    /// <summary>Clearing the search shows the whole list again.</summary>
    [Fact]
    public async Task Clear_search_shows_every_player()
    {
        var analyst = await chat.NewAnalystAsync();
        await AddPlayerAsync(analyst);
        var wanted = await AddPlayerAsync(analyst);
        await PressAsync(analyst, await PressMenuAsync(analyst), "Search");
        var result = await TypeAsync(analyst, wanted.Name);

        var cleared = await PressAsync(analyst, result, "Clear search");

        PlayerButtons(cleared).Should().HaveCount(2);
    }

    /// <summary>A search the analyst cancels leaves no open question.</summary>
    [Fact]
    public async Task Cancelled_search_returns_to_the_list()
    {
        var analyst = await chat.NewAnalystAsync();
        await AddPlayerAsync(analyst);
        var prompt = await PressAsync(analyst, await PressMenuAsync(analyst), "Search");

        var list = await PressAsync(analyst, prompt, "Cancel");

        PlayerButtons(list).Should().HaveCount(1);
        (await ConversationCountAsync(analyst)).Should().Be(0);
    }

    /// <summary>A search over 100 characters is refused and the bot keeps waiting for a shorter one.</summary>
    [Fact]
    public async Task Long_search_is_refused()
    {
        var analyst = await chat.NewAnalystAsync();
        await AddPlayerAsync(analyst);
        await PressAsync(analyst, await PressMenuAsync(analyst), "Search");

        var answer = await TypeAsync(analyst, new string('a', 101));

        answer.Text.Should().Contain("too long");
        (await ConversationStepAsync(analyst)).Should().Be("PlayersSearch");
    }

    /// <summary>Search never replaces an unfinished new player: the press only gets a note.</summary>
    [Fact]
    public async Task Search_is_refused_while_a_new_player_is_unfinished()
    {
        var analyst = await chat.NewAnalystAsync();
        await AddPlayerAsync(analyst);
        var list = await PressMenuAsync(analyst);
        await PostUpdateAsync(Callback(analyst, BotCallbacks.NewPlayer, 1));
        var category = Last(analyst);
        var name = await PressAsync(analyst, category, "Men");
        name.Text.Should().Contain("full name");
        await TypeAsync(analyst, "Draft Person");

        var searchButton = list.Keyboard!.Rows.SelectMany(row => row).First(button => button.Text.Contains("Search", StringComparison.Ordinal));
        await PostUpdateAsync(Callback(analyst, searchButton.CallbackData, list.MessageId!.Value));

        LastNote().Should().Contain("new player");
        (await ConversationStepAsync(analyst)).Should().Be("Nationality");
    }

    /// <summary>An admin sees other analysts' players.</summary>
    [Fact]
    public async Task Admin_sees_every_analysts_players()
    {
        var analyst = await chat.NewAnalystAsync();
        var admin = await chat.NewAdminAsync();
        var player = await AddPlayerAsync(analyst);

        await PressAsync(admin, await PressMenuAsync(admin), "Search");
        var result = await TypeAsync(admin, player.Name);

        PlayerButtons(result).Should().ContainSingle().Which.Text.Should().Contain(player.Name);
    }

    /// <summary>A player opens as a card with its details, file counts and the buttons.</summary>
    [Fact]
    public async Task Card_shows_the_player()
    {
        var analyst = await chat.NewAnalystAsync();
        var player = await AddPlayerAsync(analyst);
        await AddAssetAsync(player.Id, "highlights.mp4", 0);
        await AddAssetAsync(player.Id, "full match.mp4", 1);

        var card = await PressAsync(analyst, await PressMenuAsync(analyst), player.Name);

        card.Kind.Should().Be("Edit");
        card.Text.Should().Contain(player.Name).And.Contain("Iran").And.Contain("OH").And.Contain("Waiting").And.Contain("2 files");
        var buttons = card.Keyboard!.Rows.SelectMany(row => row).ToList();
        buttons.Should().Contain(button => button.Text.Contains("Files", StringComparison.Ordinal));
        buttons.Should().Contain(button => button.Text.Contains("Back", StringComparison.Ordinal));
        buttons.Should().Contain(button => button.WebAppUrl == $"{BotChatFixture.MiniAppUrl}/players/{player.Id}");
    }

    /// <summary>Back on the card returns to the list at the filter and page it came from.</summary>
    [Fact]
    public async Task Card_back_returns_to_the_same_page()
    {
        var analyst = await chat.NewAnalystAsync();
        for (var i = 0; i < 9; i++)
        {
            await AddPlayerAsync(analyst);
        }

        var second = await PressAsync(analyst, await PressMenuAsync(analyst), "▶");
        var openedPlayer = PlayerButtons(second).Single();
        var card = await PressAsync(analyst, second, openedPlayer.Text[..openedPlayer.Text.IndexOf(" · ", StringComparison.Ordinal)]);

        var back = await PressAsync(analyst, card, "Back");

        PlayerButtons(back).Should().ContainSingle().Which.Text.Should().Be(openedPlayer.Text);
    }

    /// <summary>The files list shows the files in page order with the hero starred, and goes back to the card.</summary>
    [Fact]
    public async Task Files_are_listed_in_page_order_with_the_hero()
    {
        var analyst = await chat.NewAnalystAsync();
        var player = await AddPlayerAsync(analyst);
        await AddAssetAsync(player.Id, "highlights.mp4", 0);
        var second = await AddAssetAsync(player.Id, "full match.mp4", 1);
        await SetHeroAsync(player.Id, second);
        var card = await PressAsync(analyst, await PressMenuAsync(analyst), player.Name);

        var files = await PressAsync(analyst, card, "Files");

        files.Kind.Should().Be("Edit");
        files.Text.Should().Contain(player.Name);
        var lines = files.Text!.Split('\n').Where(line => line.Contains(".mp4", StringComparison.Ordinal) || line.Contains("highlights", StringComparison.OrdinalIgnoreCase) || line.Contains("full match", StringComparison.OrdinalIgnoreCase)).ToList();
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("highlights").And.NotContain("★");
        lines[1].Should().Contain("full match").And.Contain("★");
        var backToCard = await PressAsync(analyst, files, "Back");
        backToCard.Text.Should().Contain(player.Name).And.Contain("2 files");
    }

    /// <summary>A player with no files says so.</summary>
    [Fact]
    public async Task Files_of_a_player_without_files()
    {
        var analyst = await chat.NewAnalystAsync();
        var player = await AddPlayerAsync(analyst);
        var card = await PressAsync(analyst, await PressMenuAsync(analyst), player.Name);

        var files = await PressAsync(analyst, card, "Files");

        files.Text.Should().Contain("No files");
    }

    /// <summary>A card button for a player that does not exist gets a note and changes nothing.</summary>
    [Fact]
    public async Task Unknown_player_card_gets_a_note()
    {
        var analyst = await chat.NewAnalystAsync();
        var list = await PressMenuAsync(analyst);

        await PostUpdateAsync(Callback(analyst, "mp:c:" + MyPlayersId(Guid.NewGuid()) + ":a:1:", list.MessageId!.Value));

        LastNote().Should().Contain("not found");
    }

    /// <summary>Data that is not a My players button, or is cut short, is ignored with a note.</summary>
    /// <param name="data">The callback data.</param>
    [Theory]
    [InlineData("mp:")]
    [InlineData("mp:zzz")]
    [InlineData("mp:l:x:1:")]
    [InlineData("mp:l:a:0:")]
    [InlineData("mp:c:notanid:a:1:")]
    public async Task Malformed_buttons_get_the_stale_note(string data)
    {
        var analyst = await chat.NewAnalystAsync();
        var list = await PressMenuAsync(analyst);

        await PostUpdateAsync(Callback(analyst, data, list.MessageId!.Value));

        LastNote().Should().Contain("no longer active");
    }

    /// <summary>Someone who is not an active team member gets no list from the button or the command.</summary>
    [Fact]
    public async Task Strangers_get_no_list()
    {
        const long stranger = 7_999_001;

        await PostUpdateAsync(Callback(stranger, BotCallbacks.MyPlayers, 1));
        await CommandAsync(stranger, "players");

        chat.Bot.ToChat(stranger).Where(call => call.Kind == "Send" || call.Kind == "Edit")
            .Should().OnlyContain(call => call.Keyboard == null || !call.Keyboard.Rows.SelectMany(row => row).Any(button => button.CallbackData.StartsWith("mp:", StringComparison.Ordinal)));
    }

    /// <summary>A button of the list is a short callback, well under Telegram's 64 bytes, even with a long search.</summary>
    [Fact]
    public async Task Callback_data_fits_telegram_limit()
    {
        var analyst = await chat.NewAnalystAsync();
        await AddPlayerAsync(analyst);
        await PressAsync(analyst, await PressMenuAsync(analyst), "Search");
        var result = await TypeAsync(analyst, "Zed " + new string('a', 80));
        var list = await PressMenuAsync(analyst);

        foreach (var data in result.ButtonData.Concat(list.ButtonData))
        {
            System.Text.Encoding.UTF8.GetByteCount(data).Should().BeLessThanOrEqualTo(64);
        }
    }

    /// <summary>Gets a player's buttons: the ones that open a card.</summary>
    /// <param name="message">The list message.</param>
    /// <returns>The buttons.</returns>
    private static List<BotButton> PlayerButtons(BotCall message) =>
        [.. message.Keyboard!.Rows.SelectMany(row => row).Where(button => button.CallbackData.StartsWith("mp:c:", StringComparison.Ordinal))];

    /// <summary>Encodes a player id as the buttons do: 22 URL-safe base64 characters.</summary>
    /// <param name="id">The player id.</param>
    /// <returns>The short id.</returns>
    private static string MyPlayersId(Guid id) =>
        Convert.ToBase64String(id.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Turns a number into letters, because names hold no digits.</summary>
    /// <param name="number">The number.</param>
    /// <returns>Letters.</returns>
    private static string Letters(int number)
    {
        var text = string.Empty;
        do
        {
            text = (char)('a' + (number % 26)) + text;
            number /= 26;
        }
        while (number > 0);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Stores a Waiting player of an analyst with a name, nationality and one position.</summary>
    /// <param name="telegramId">The owning analyst.</param>
    /// <returns>The player's id and name.</returns>
    private async Task<(Guid Id, string Name)> AddPlayerAsync(long telegramId)
    {
        var name = $"Lister {Letters(Interlocked.Increment(ref _seeded) + 100)}";
        using var scope = chat.Api.Services.CreateScope();
        var owner = await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().FirstOrDefaultAsync(new UserByTelegramIdSpec(telegramId), TestContext.Current.CancellationToken);
        var player = Player.Create(PlayerCategory.Men, DateTimeOffset.UtcNow);
        player.Rename(FullName.Create(name), Slug.Create(name.ToLowerInvariant().Replace(' ', '-')));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter]));
        player.AssignAnalyst(owner!.Id);
        scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().Add(player);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return (player.Id, name);
    }

    /// <summary>Stores a file of a player, as ingest will.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fileName">The file name; its words give the tags.</param>
    /// <param name="sortOrder">Its position on the page.</param>
    /// <returns>The asset id.</returns>
    private async Task<Guid> AddAssetAsync(Guid playerId, string fileName, int sortOrder)
    {
        using var scope = chat.Api.Services.CreateScope();
        var tags = AssetTagger.Classify([], fileName).Tags;
        var asset = Asset.Add(playerId, null, AssetSource.Drive, null, AssetFile.Create(fileName, Guid.NewGuid().ToString("N"), 2048), Path.GetFileNameWithoutExtension(fileName), tags, sortOrder, DateTimeOffset.UtcNow);
        scope.ServiceProvider.GetRequiredService<IRepository<Asset, Guid>>().Add(asset);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return asset.Id;
    }

    /// <summary>Makes a file the player's hero video.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="assetId">The file.</param>
    /// <returns>A task that completes when it is saved.</returns>
    private async Task SetHeroAsync(Guid playerId, Guid assetId)
    {
        using var scope = chat.Api.Services.CreateScope();
        var player = await scope.ServiceProvider.GetRequiredService<IRepository<Player, Guid>>().GetByIdAsync(playerId, TestContext.Current.CancellationToken);
        var asset = await scope.ServiceProvider.GetRequiredService<IRepository<Asset, Guid>>().GetByIdAsync(assetId, TestContext.Current.CancellationToken);
        player!.SetHero(asset!);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Presses the menu's My players button.</summary>
    /// <param name="telegramId">The user.</param>
    /// <returns>The bot's answer.</returns>
    private async Task<BotCall> PressMenuAsync(long telegramId)
    {
        await PostUpdateAsync(Callback(telegramId, BotCallbacks.MyPlayers, 1));
        return Last(telegramId);
    }

    /// <summary>Presses the button with a label on a bot message, expecting a new or edited message.</summary>
    /// <param name="telegramId">The user.</param>
    /// <param name="message">The bot message with the button.</param>
    /// <param name="label">Part of the button's label.</param>
    /// <returns>The bot's new or edited message.</returns>
    private async Task<BotCall> PressAsync(long telegramId, BotCall message, string label)
    {
        var button = message.Keyboard!.Rows.SelectMany(row => row).First(button => button.Text.Contains(label, StringComparison.Ordinal) && button.CallbackData.Length > 0);
        var before = chat.Bot.ToChat(telegramId).Count(call => call.Kind is "Send" or "Edit");
        await PostUpdateAsync(Callback(telegramId, button.CallbackData, message.MessageId!.Value));
        var after = chat.Bot.ToChat(telegramId).Where(call => call.Kind is "Send" or "Edit").ToList();
        return after.Count > before ? after[^1] : throw new InvalidOperationException($"Pressing '{label}' got no message.");
    }

    /// <summary>Types a message.</summary>
    /// <param name="telegramId">The user.</param>
    /// <param name="text">The text.</param>
    /// <returns>The bot's answer.</returns>
    private async Task<BotCall> TypeAsync(long telegramId, string text)
    {
        await PostUpdateAsync(Message(telegramId, new Dictionary<string, object?> { ["text"] = text }));
        return Last(telegramId);
    }

    /// <summary>Sends a bot command such as <c>/players</c>.</summary>
    /// <param name="telegramId">The user.</param>
    /// <param name="command">The command without slash.</param>
    /// <returns>A task that completes when the API answered.</returns>
    private Task CommandAsync(long telegramId, string command) =>
        PostUpdateAsync(Message(telegramId, new Dictionary<string, object?>
        {
            ["text"] = "/" + command,
            ["entities"] = new[] { new { type = "bot_command", offset = 0, length = command.Length + 1 } },
        }));

    /// <summary>Gets the bot's latest message sent to or edited in a chat.</summary>
    /// <param name="telegramId">The chat.</param>
    /// <returns>The call.</returns>
    private BotCall Last(long telegramId) => chat.Bot.ToChat(telegramId).Last(call => call.Kind is "Send" or "Edit");

    /// <summary>Gets the latest note on a button press (the tests of one class run one at a time).</summary>
    /// <returns>The note text.</returns>
    private string? LastNote() => chat.Bot.Calls.Last(call => call.Kind == "Answer").Text;

    /// <summary>Builds a message update.</summary>
    /// <param name="telegramId">The sender.</param>
    /// <param name="content">The message fields besides ids.</param>
    /// <returns>The update.</returns>
    private static Dictionary<string, object?> Message(long telegramId, Dictionary<string, object?> content)
    {
        content["message_id"] = Interlocked.Increment(ref _userMessageId);
        content["date"] = 1762430400;
        content["chat"] = new { id = telegramId, type = "private" };
        content["from"] = new { id = telegramId, is_bot = false, first_name = $"User {telegramId}" };
        return new Dictionary<string, object?> { ["update_id"] = Interlocked.Increment(ref _updateId), ["message"] = content };
    }

    /// <summary>Builds a button press on a bot message.</summary>
    /// <param name="telegramId">The presser.</param>
    /// <param name="data">The button data.</param>
    /// <param name="messageId">The bot message the button is on.</param>
    /// <returns>The update.</returns>
    private static Dictionary<string, object?> Callback(long telegramId, string data, int messageId) => new()
    {
        ["update_id"] = Interlocked.Increment(ref _updateId),
        ["callback_query"] = new Dictionary<string, object?>
        {
            ["id"] = $"cb-{Interlocked.Increment(ref _updateId)}",
            ["from"] = new { id = telegramId, is_bot = false, first_name = $"User {telegramId}" },
            ["chat_instance"] = "chat",
            ["data"] = data,
            ["message"] = new { message_id = messageId, date = 1762430400, chat = new { id = telegramId, type = "private" }, text = "…" },
        },
    };

    /// <summary>Posts an update to the webhook.</summary>
    /// <param name="update">The update.</param>
    /// <returns>A task that completes when the API answered.</returns>
    private async Task PostUpdateAsync(object update)
    {
        using var client = chat.Api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/telegram/webhook", UriKind.Relative)) { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", BotChatFixture.Secret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Counts a user's open conversations.</summary>
    /// <param name="telegramId">The user.</param>
    /// <returns>The count.</returns>
    private async Task<long> ConversationCountAsync(long telegramId) =>
        (long)(await ScalarAsync("SELECT count(*) FROM bot_conversations WHERE telegram_user_id = @p", telegramId))!;

    /// <summary>Reads the step a user's conversation waits at.</summary>
    /// <param name="telegramId">The user.</param>
    /// <returns>The step name, or <see langword="null"/>.</returns>
    private async Task<string?> ConversationStepAsync(long telegramId) =>
        await ScalarAsync("SELECT step FROM bot_conversations WHERE telegram_user_id = @p", telegramId) as string;

    /// <summary>Runs a query with one parameter, returning one value.</summary>
    /// <param name="sql">The query, using <c>@p</c>.</param>
    /// <param name="value">The parameter.</param>
    /// <returns>The value.</returns>
    private async Task<object?> ScalarAsync(string sql, object value)
    {
        await using var connection = new NpgsqlConnection(chat.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p", value);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
