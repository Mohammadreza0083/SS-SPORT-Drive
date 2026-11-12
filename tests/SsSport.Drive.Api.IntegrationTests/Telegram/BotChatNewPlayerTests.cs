using System.Net;
using System.Net.Http.Json;
using ImageMagick;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Application.Telegram.Access;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// Boots the API once for the chat tests (T-10a), with Telegram, Drive and storage replaced. Each test uses its own
/// analyst, so the tests share the database and the recorded bot calls without seeing each other.
/// </summary>
public sealed class BotChatFixture : IAsyncLifetime
{
    /// <summary>The bot token.</summary>
    public const string BotToken = "123456:bot-chat-test-token";

    /// <summary>The webhook secret.</summary>
    public const string Secret = "chat_Secret-1";

    /// <summary>The Mini App's address, so the bot offers "Open in Mini App".</summary>
    public const string MiniAppUrl = "https://app.example.test/miniapp";

    /// <summary>The next analyst's Telegram id.</summary>
    private static long _nextTelegramId = 7000;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Gets the recorded bot calls and the files users "sent".</summary>
    internal RecordingMessenger Bot { get; } = new();

    /// <summary>Gets Google Drive's stand-in.</summary>
    public FakeDriveFolderReader Drive { get; } = new();

    /// <summary>Gets the API.</summary>
    public WebApplicationFactory<Program> Api { get; private set; } = null!;

    /// <summary>Gets the database connection string.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Starts PostgreSQL, boots the API and migrates.</summary>
    /// <returns>A task that completes when everything is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:BotToken"] = BotToken,
                ["Telegram:WebhookSecret"] = Secret,
                ["Telegram:Mode"] = "Webhook",
                ["Telegram:MiniAppUrl"] = MiniAppUrl,
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(Bot));
                services.UseInlineBotNotices();
                services.Replace(ServiceDescriptor.Singleton<IDriveFolderReader>(Drive));
                services.Replace(ServiceDescriptor.Singleton<IObjectStorage>(new InMemoryObjectStorage()));
            });
        });
        await DatabaseMigrator.MigrateAsync(Api.Services, TestContext.Current.CancellationToken);
        Bot.Files["good"] = Jpeg(900, 1200);
        Bot.Files["small"] = Jpeg(600, 800);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Stores a new active analyst.</summary>
    /// <returns>The analyst's Telegram id.</returns>
    public async Task<long> NewAnalystAsync()
    {
        var telegramId = Interlocked.Increment(ref _nextTelegramId);
        using var scope = Api.Services.CreateScope();
        var analyst = User.RequestAccess(telegramId, $"chat_{telegramId}", $"Chat {telegramId}", DateTimeOffset.UtcNow);
        analyst.Approve(Role.Analyst);
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().Add(analyst);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return telegramId;
    }

    /// <summary>Stores a new active admin.</summary>
    /// <returns>The admin's Telegram id.</returns>
    public async Task<long> NewAdminAsync()
    {
        var telegramId = Interlocked.Increment(ref _nextTelegramId);
        using var scope = Api.Services.CreateScope();
        var admin = User.CreateFirstAdmin(telegramId, $"Admin {telegramId}", EmailAddress.Create($"admin{telegramId}@ss-sport.co"), DateTimeOffset.UtcNow);
        scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().Add(admin);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return telegramId;
    }

    /// <summary>Builds a JPEG of a size.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Jpeg(int width, int height)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, (uint)width, (uint)height) { Format = MagickFormat.Jpeg };
        return image.ToByteArray();
    }
}

/// <summary>
/// New player in the bot chat (T-10a, owner-approved 2026-11-06): the bot asks step by step (category, name,
/// nationality, positions, Volleybox, Drive folder, photo or Skip), shows a summary, and creates the same Waiting
/// player the Mini App form does. Every step can go Back or Cancel, drafts last 24 hours, and the rules are the Mini
/// App's.
/// </summary>
/// <param name="chat">The shared API.</param>
public sealed class BotChatNewPlayerTests(BotChatFixture chat) : IClassFixture<BotChatFixture>
{
    /// <summary>The update id counter.</summary>
    private static long _updateId = 70_000;

    /// <summary>The message id counter for messages users send.</summary>
    private static int _userMessageId = 7000;

    /// <summary>The Volleybox id counter, so every test has its own player page.</summary>
    private static int _volleyboxId = 71_000;

    /// <summary>
    /// The whole flow: each answer moves to the next question, and Create makes a Waiting player owned by the analyst,
    /// with the photo; the progress message follows, and the draft is gone.
    /// </summary>
    [Fact]
    public async Task Whole_flow_creates_the_player()
    {
        var analyst = await chat.NewAnalystAsync();
        var volleybox = Interlocked.Increment(ref _volleyboxId);

        var category = await PressMenuAsync(analyst);
        category.Text.Should().Contain("Men or women");
        category.Keyboard!.Rows.SelectMany(row => row).Should().Contain(button => button.WebAppUrl == BotChatFixture.MiniAppUrl + "/new");

        (await PressAsync(analyst, category, "Men")).Text.Should().Contain("full name");
        (await TypeAsync(analyst, "  ali   rezaei ")).Text.Should().Contain("Nationality");
        var countries = await TypeAsync(analyst, "ira");
        countries.Keyboard!.Rows.SelectMany(row => row).Select(button => button.Text).Should().Contain(text => text.Contains("Iran")).And.Contain(text => text.Contains("Iraq"));
        var positions = await PressAsync(analyst, countries, "Iran");
        positions.Text.Should().Contain("Positions");
        positions = await PressAsync(analyst, positions, "OH");
        positions = await PressAsync(analyst, positions, "MB");
        (await PressAsync(analyst, positions, "Done")).Text.Should().Contain("Volleybox");
        var drive = await TypeAsync(analyst, $"https://volleybox.net/ali-rezaei-p{volleybox}");
        drive.Text.Should().Contain("Drive folder");
        var photo = await TypeAsync(analyst, $"https://drive.google.com/drive/folders/chatFolder_{volleybox}");
        photo.Text.Should().Contain($"SS-SPORT chatFolder_{volleybox}").And.Contain("12 videos").And.Contain("photo");
        var summary = await SendPhotoAsync(analyst, "good");
        summary.Text.Should().Contain("Ali Rezaei").And.Contain("Iran").And.Contain("OH / MB").And.Contain($"p{volleybox}");

        var created = await PressAsync(analyst, summary, "Create");

        created.Text.Should().Contain("Ali Rezaei").And.Contain("waits for an admin");
        var player = await PlayerAsync(volleybox);
        player.Should().NotBeNull();
        player!.Value.Status.Should().Be("Waiting");
        player.Value.Name.Should().Be("Ali Rezaei");
        player.Value.Analyst.Should().Be(analyst);
        player.Value.PhotoKey.Should().NotBeNull();
        (await FolderCountAsync(player.Value.Id)).Should().Be(1);
        (await ConversationCountAsync(analyst)).Should().Be(0);
        chat.Bot.ToChat(analyst).Should().Contain(call => call.Kind == "Send" && call.Text!.StartsWith("⏳", StringComparison.Ordinal), "the progress message (T-6a) follows");
    }

    /// <summary>Skip leaves the photo for later: the player is created without one.</summary>
    [Fact]
    public async Task Photo_can_be_skipped()
    {
        var analyst = await chat.NewAnalystAsync();
        var volleybox = await AnswerUpToPhotoAsync(analyst);

        var summary = await PressAsync(analyst, Last(analyst), "Skip");
        summary.Text.Should().Contain("later");
        await PressAsync(analyst, summary, "Create");

        (await PlayerAsync(volleybox))!.Value.PhotoKey.Should().BeNull();
    }

    /// <summary>A wrong answer is explained with the Mini App's own rule, and the same question stays open.</summary>
    [Fact]
    public async Task Wrong_answers_keep_the_step()
    {
        var analyst = await chat.NewAnalystAsync();
        await PressAsync(analyst, await PressMenuAsync(analyst), "Women");

        (await TypeAsync(analyst, "علی رضایی")).Text.Should().Contain("Latin letters");
        (await TypeAsync(analyst, "Sara Ahmadi")).Text.Should().Contain("Nationality");
        (await TypeAsync(analyst, "zzzz")).Text.Should().Contain("No country matches");
        var positions = await PressAsync(analyst, await TypeAsync(analyst, "germ"), "Germany");
        (await TryPressAsync(analyst, positions, "Done")).Should().BeNull("Done needs a position, so only a note is shown");
        LastNote().Should().Be("Pick at least one position.");
        await PressAsync(analyst, await PressAsync(analyst, Last(analyst), "S"), "Done");

        (await TypeAsync(analyst, "https://example.com/not-volleybox")).Text.Should().Contain("Volleybox");
        (await ConversationStepAsync(analyst)).Should().Be("Volleybox");
    }

    /// <summary>A third position is refused with a note, and the two chosen stay.</summary>
    [Fact]
    public async Task At_most_two_positions()
    {
        var analyst = await chat.NewAnalystAsync();
        await AnswerUpToPositionsAsync(analyst);
        var positions = await PressAsync(analyst, Last(analyst), "OH");
        positions = await PressAsync(analyst, positions, "MB");

        (await TryPressAsync(analyst, positions, "L")).Should().BeNull();

        LastNote().Should().Be("At most two positions.");
        (await PressAsync(analyst, Last(analyst), "Done")).Text.Should().Contain("Volleybox");
    }

    /// <summary>Back returns to the previous question, with the earlier answer kept for the summary.</summary>
    [Fact]
    public async Task Back_returns_to_the_previous_question()
    {
        var analyst = await chat.NewAnalystAsync();
        await PressAsync(analyst, await PressMenuAsync(analyst), "Men");
        var nationality = await TypeAsync(analyst, "Reza Karimi");

        var name = await PressAsync(analyst, nationality, "Back");

        name.Text.Should().Contain("full name");
        (await ConversationStepAsync(analyst)).Should().Be("Name");
    }

    /// <summary>Cancel (button or /cancel) drops the draft and saves nothing.</summary>
    [Fact]
    public async Task Cancel_drops_the_draft()
    {
        var analyst = await chat.NewAnalystAsync();
        var name = await PressAsync(analyst, await PressMenuAsync(analyst), "Men");

        (await PressAsync(analyst, name, "Cancel")).Text.Should().Contain("Cancelled");
        (await ConversationCountAsync(analyst)).Should().Be(0);

        await PressAsync(analyst, await PressMenuAsync(analyst), "Men");
        await CommandAsync(analyst, "cancel");
        Last(analyst).Text.Should().Contain("Cancelled");
        (await ConversationCountAsync(analyst)).Should().Be(0);
    }

    /// <summary>Coming back to an unfinished draft asks to continue (at the open question) or start over.</summary>
    [Fact]
    public async Task Unfinished_draft_can_be_continued_or_restarted()
    {
        var analyst = await chat.NewAnalystAsync();
        await PressAsync(analyst, await PressMenuAsync(analyst), "Men");
        await TypeAsync(analyst, "Omid Nazari");

        await CommandAsync(analyst, "new");
        var question = Last(analyst);
        question.Text.Should().Contain("Continue or start over");

        (await PressAsync(analyst, question, "Continue")).Text.Should().Contain("Nationality");

        await CommandAsync(analyst, "new");
        (await PressAsync(analyst, Last(analyst), "Start over")).Text.Should().Contain("Men or women");
        (await ConversationStepAsync(analyst)).Should().Be("Category");
    }

    /// <summary>A draft older than 24 hours is forgotten: the flow starts fresh without asking.</summary>
    [Fact]
    public async Task Expired_draft_starts_fresh()
    {
        var analyst = await chat.NewAnalystAsync();
        await PressAsync(analyst, await PressMenuAsync(analyst), "Men");
        await ExecuteAsync("UPDATE bot_conversations SET expires_at = now() - interval '1 minute' WHERE telegram_user_id = @p", analyst);

        (await PressMenuAsync(analyst)).Text.Should().Contain("Men or women");
    }

    /// <summary>A button from an earlier question does nothing but say so.</summary>
    [Fact]
    public async Task Old_buttons_are_ignored()
    {
        var analyst = await chat.NewAnalystAsync();
        var category = await PressMenuAsync(analyst);
        await PressAsync(analyst, category, "Men");

        (await TryPressAsync(analyst, category, "Women")).Should().BeNull();

        LastNote().Should().Be("This button is no longer active.");
        (await ConversationStepAsync(analyst)).Should().Be("Name");
    }

    /// <summary>From the summary, one field can be changed; the answer returns to the summary.</summary>
    [Fact]
    public async Task Summary_field_can_be_edited()
    {
        var analyst = await chat.NewAnalystAsync();
        await AnswerUpToPhotoAsync(analyst);
        var summary = await PressAsync(analyst, Last(analyst), "Skip");

        var fields = await PressAsync(analyst, summary, "Edit a field");
        (await PressAsync(analyst, fields, "Name")).Text.Should().Contain("full name");
        var updated = await TypeAsync(analyst, "Changed Name");

        updated.Text.Should().Contain("Changed Name").And.Contain("Create");
    }

    /// <summary>
    /// A Volleybox link of a known player offers the T-5 choices; adding a folder links the next Drive link to that
    /// player instead of creating one.
    /// </summary>
    [Fact]
    public async Task Known_player_gets_a_folder()
    {
        var first = await chat.NewAnalystAsync();
        var volleybox = await AnswerUpToPhotoAsync(first);
        await PressAsync(first, await PressAsync(first, Last(first), "Skip"), "Create");
        var existing = (await PlayerAsync(volleybox))!.Value;

        var second = await chat.NewAnalystAsync();
        await AnswerUpToPositionsAsync(second);
        await PressAsync(second, await PressAsync(second, Last(second), "OH"), "Done");
        var choices = await TypeAsync(second, $"https://volleybox.net/someone-p{volleybox}");
        choices.Text.Should().Contain(existing.Name);
        choices.Keyboard!.Rows.SelectMany(row => row).Should().Contain(button => button.WebAppUrl == $"{BotChatFixture.MiniAppUrl}/players/{existing.Id}");

        (await PressAsync(second, choices, "Add a Drive folder")).Text.Should().Contain("Drive folder");
        var added = await TypeAsync(second, $"https://drive.google.com/drive/folders/extraFolder_{volleybox}");

        added.Text.Should().Contain($"SS-SPORT extraFolder_{volleybox}").And.Contain(existing.Name);
        (await FolderCountAsync(existing.Id)).Should().Be(2);
        (await ConversationCountAsync(second)).Should().Be(0);
    }

    /// <summary>A Drive folder that already belongs to a player is refused, naming the player.</summary>
    [Fact]
    public async Task Taken_folder_is_refused()
    {
        var first = await chat.NewAnalystAsync();
        var volleybox = await AnswerUpToPhotoAsync(first);
        await PressAsync(first, await PressAsync(first, Last(first), "Skip"), "Create");

        var second = await chat.NewAnalystAsync();
        await AnswerUpToVolleyboxAsync(second);
        await TypeAsync(second, $"https://volleybox.net/other-p{Interlocked.Increment(ref _volleyboxId)}");
        var refused = await TypeAsync(second, $"https://drive.google.com/drive/folders/chatFolder_{volleybox}");

        refused.Text.Should().Contain("already belongs to").And.Contain("Test Player");
        (await ConversationStepAsync(second)).Should().Be("DriveFolder");
    }

    /// <summary>A photo under 800 px is refused at the photo step, which stays open.</summary>
    [Fact]
    public async Task Small_photo_is_refused()
    {
        var analyst = await chat.NewAnalystAsync();
        await AnswerUpToPhotoAsync(analyst);

        (await SendPhotoAsync(analyst, "small")).Text.Should().Contain("800");
        (await ConversationStepAsync(analyst)).Should().Be("Photo");
    }

    /// <summary>When Drive cannot be reached at Create, nothing is saved and the summary stays for another try.</summary>
    [Fact]
    public async Task Drive_down_at_create_keeps_the_draft()
    {
        var analyst = await chat.NewAnalystAsync();
        var volleybox = await AnswerUpToPhotoAsync(analyst);
        var summary = await PressAsync(analyst, Last(analyst), "Skip");

        chat.Drive.Unavailable = true;
        try
        {
            (await PressAsync(analyst, summary, "Create")).Text.Should().Contain("Google Drive cannot be reached");
        }
        finally
        {
            chat.Drive.Unavailable = false;
        }

        (await PlayerAsync(volleybox)).Should().BeNull();
        (await ConversationStepAsync(analyst)).Should().Be("Summary");
    }

    /// <summary>Typed text over 500 characters is refused without being stored.</summary>
    [Fact]
    public async Task Long_text_is_refused()
    {
        var analyst = await chat.NewAnalystAsync();
        await PressAsync(analyst, await PressMenuAsync(analyst), "Men");

        (await TypeAsync(analyst, new string('a', 501))).Text.Should().Contain("500");
        (await ConversationStepAsync(analyst)).Should().Be("Name");
    }

    /// <summary>Text without an open question still opens the menu, as before.</summary>
    [Fact]
    public async Task Text_without_a_question_opens_the_menu()
    {
        var analyst = await chat.NewAnalystAsync();

        (await TypeAsync(analyst, "hello")).Text.Should().StartWith("Welcome back");
    }

    /// <summary>Someone outside the team cannot start the flow.</summary>
    [Fact]
    public async Task Stranger_cannot_start()
    {
        var stranger = 7999L;

        await CommandAsync(stranger, "new");

        Last(stranger).Text.Should().NotContain("Men or women");
        (await ConversationCountAsync(stranger)).Should().Be(0);
    }

    /// <summary>Answers category, name and nationality.</summary>
    /// <param name="analyst">The analyst.</param>
    /// <returns>A task that completes at the positions question.</returns>
    private async Task AnswerUpToPositionsAsync(long analyst)
    {
        await PressAsync(analyst, await PressMenuAsync(analyst), "Men");
        await TypeAsync(analyst, "Test Player");
        await PressAsync(analyst, await TypeAsync(analyst, "iran"), "Iran");
    }

    /// <summary>Answers up to the Volleybox question.</summary>
    /// <param name="analyst">The analyst.</param>
    /// <returns>A task that completes at the Volleybox question.</returns>
    private async Task AnswerUpToVolleyboxAsync(long analyst)
    {
        await AnswerUpToPositionsAsync(analyst);
        await PressAsync(analyst, await PressAsync(analyst, Last(analyst), "OPP"), "Done");
    }

    /// <summary>Answers up to the photo question with a fresh Volleybox id and folder.</summary>
    /// <param name="analyst">The analyst.</param>
    /// <returns>The Volleybox id used.</returns>
    private async Task<int> AnswerUpToPhotoAsync(long analyst)
    {
        var volleybox = Interlocked.Increment(ref _volleyboxId);
        await AnswerUpToVolleyboxAsync(analyst);
        await TypeAsync(analyst, $"https://volleybox.net/test-player-p{volleybox}");
        (await TypeAsync(analyst, $"https://drive.google.com/drive/folders/chatFolder_{volleybox}")).Text.Should().Contain("photo");
        return volleybox;
    }

    /// <summary>Presses the menu's New player button.</summary>
    /// <param name="telegramId">The user.</param>
    /// <returns>The bot's answer.</returns>
    private async Task<BotCall> PressMenuAsync(long telegramId)
    {
        await PostUpdateAsync(Callback(telegramId, BotCallbacks.NewPlayer, 1));
        return Last(telegramId);
    }

    /// <summary>Presses the button with a label on a bot message, expecting a new or edited message.</summary>
    /// <param name="telegramId">The user.</param>
    /// <param name="message">The bot message with the button.</param>
    /// <param name="label">Part of the button's label.</param>
    /// <returns>The bot's new or edited message.</returns>
    private async Task<BotCall> PressAsync(long telegramId, BotCall message, string label) =>
        await TryPressAsync(telegramId, message, label) ?? throw new InvalidOperationException($"Pressing '{label}' got no message.");

    /// <summary>Presses the button with a label on a bot message.</summary>
    /// <param name="telegramId">The user.</param>
    /// <param name="message">The bot message with the button.</param>
    /// <param name="label">Part of the button's label.</param>
    /// <returns>The bot's new or edited message, or <see langword="null"/> when the press only got a note.</returns>
    private async Task<BotCall?> TryPressAsync(long telegramId, BotCall message, string label)
    {
        var button = message.Keyboard!.Rows.SelectMany(row => row).First(button => button.Text.Contains(label, StringComparison.Ordinal) && button.CallbackData.Length > 0);
        var before = chat.Bot.ToChat(telegramId).Count(call => call.Kind is "Send" or "Edit");
        await PostUpdateAsync(Callback(telegramId, button.CallbackData, message.MessageId!.Value));
        var after = chat.Bot.ToChat(telegramId).Where(call => call.Kind is "Send" or "Edit").ToList();
        return after.Count > before ? after[^1] : null;
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

    /// <summary>Sends a bot command such as <c>/new</c>.</summary>
    /// <param name="telegramId">The user.</param>
    /// <param name="command">The command without slash.</param>
    /// <returns>A task that completes when the API answered.</returns>
    private Task CommandAsync(long telegramId, string command) =>
        PostUpdateAsync(Message(telegramId, new Dictionary<string, object?>
        {
            ["text"] = "/" + command,
            ["entities"] = new[] { new { type = "bot_command", offset = 0, length = command.Length + 1 } },
        }));

    /// <summary>Sends a photo.</summary>
    /// <param name="telegramId">The user.</param>
    /// <param name="fileId">The file id the recording messenger serves.</param>
    /// <returns>The bot's answer.</returns>
    private async Task<BotCall> SendPhotoAsync(long telegramId, string fileId)
    {
        await PostUpdateAsync(Message(telegramId, new Dictionary<string, object?>
        {
            ["photo"] = new[] { new { file_id = fileId, file_unique_id = fileId + "-u", width = 900, height = 1200, file_size = 200_000 } },
        }));
        return Last(telegramId);
    }

    /// <summary>Gets the bot's latest message sent to or edited in a chat.</summary>
    /// <param name="telegramId">The chat.</param>
    /// <returns>The call.</returns>
    private BotCall Last(long telegramId) => chat.Bot.ToChat(telegramId).Last(call => call.Kind is "Send" or "Edit");

    /// <summary>
    /// Gets the latest note on a button press. Notes carry no chat, but the tests of one class run one at a time, so
    /// the latest one is the press just made.
    /// </summary>
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

    /// <summary>Reads the player with a Volleybox id.</summary>
    /// <param name="volleyboxId">The Volleybox id.</param>
    /// <returns>The player, or <see langword="null"/>.</returns>
    private async Task<(Guid Id, string Name, string Status, long Analyst, string? PhotoKey)?> PlayerAsync(int volleyboxId)
    {
        await using var connection = new NpgsqlConnection(chat.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT p.id, p.full_name, p.status, u.telegram_id, p.photo_key FROM players p JOIN users u ON u.id = p.analyst_id WHERE p.volleybox_player_id = @p", connection);
        command.Parameters.AddWithValue("p", volleyboxId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        if (!await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            return null;
        }

        return (reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    /// <summary>Counts a player's linked Drive folders.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The count.</returns>
    private async Task<long> FolderCountAsync(Guid playerId) =>
        (long)(await ScalarAsync("SELECT count(*) FROM drive_folders WHERE player_id = @p", playerId))!;

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

    /// <summary>Runs a statement with one parameter.</summary>
    /// <param name="sql">The statement, using <c>@p</c>.</param>
    /// <param name="value">The parameter.</param>
    /// <returns>A task that completes when it has run.</returns>
    private async Task ExecuteAsync(string sql, object value)
    {
        await using var connection = new NpgsqlConnection(chat.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p", value);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

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
