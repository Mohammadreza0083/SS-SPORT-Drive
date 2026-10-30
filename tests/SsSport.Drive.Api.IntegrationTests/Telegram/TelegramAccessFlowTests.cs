using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// The bot's access flow end to end (T-2): through the real webhook, MediatR pipeline, unit of work, domain events and
/// PostgreSQL, with only Telegram itself replaced by a recording messenger.
/// </summary>
public sealed class TelegramAccessFlowTests : IAsyncLifetime
{
    /// <summary>The webhook secret.</summary>
    private const string Secret = "flow_Secret-1";

    /// <summary>The configured first admin.</summary>
    private const long OwnerId = 111;

    /// <summary>The update id counter, unique per test class run.</summary>
    private static long _updateId = 10_000;

    /// <summary>The throwaway database.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The bot's outgoing calls.</summary>
    private readonly RecordingMessenger _bot = new();

    /// <summary>The API under test.</summary>
    private WebApplicationFactory<Program> _api = null!;

    /// <summary>Starts PostgreSQL, migrates it and boots the API with the owner configured as first admin.</summary>
    /// <returns>A task that completes when the API is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Telegram:WebhookSecret"] = Secret,
                ["Telegram:AdminUserId"] = OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Telegram:AdminEmail"] = "owner@ss-sport.co",
                ["Telegram:Mode"] = "Webhook",
            }));
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ITelegramMessenger>(_bot)));
        });
        await DatabaseMigrator.MigrateAsync(_api.Services, TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the API and the database.</summary>
    /// <returns>A task that completes when both are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>An unknown user's /start gets the welcome with their Telegram id and a Request access button.</summary>
    [Fact]
    public async Task Unknown_user_is_offered_access()
    {
        await MessageAsync(201, "/start");

        var reply = _bot.ToChat(201).Single();
        reply.Kind.Should().Be("Send");
        reply.Text.Should().Contain("201");
        reply.ButtonData.Should().Equal("access:request");
        (await UserStatusAsync(201)).Should().BeNull("nobody is created before they ask");
    }

    /// <summary>The configured owner's first /start makes them the active first admin and welcomes them.</summary>
    [Fact]
    public async Task Owner_becomes_the_first_admin()
    {
        await MessageAsync(OwnerId, "/start");

        (await UserStatusAsync(OwnerId)).Should().Be(("Active", "Admin"));
        _bot.ToChat(OwnerId).Should().ContainSingle(call => call.Kind == "Send")
            .Which.ButtonData.Should().Contain(["menu:new", "menu:players", "menu:help"]);
    }

    /// <summary>
    /// Request → every admin is asked with three buttons → the owner approves as analyst → the analyst is welcomed with
    /// the menu, the owner's message is updated, and the decision is audited.
    /// </summary>
    [Fact]
    public async Task Request_and_approve_as_analyst()
    {
        await MessageAsync(OwnerId, "/start");
        await MessageAsync(202, "/start");
        var offer = _bot.ToChat(202).Last();

        await CallbackAsync(202, "access:request", offer.MessageId!.Value);

        (await UserStatusAsync(202)).Should().Be(("Pending", "Analyst"));
        _bot.ToChat(202).Last().Should().Match<BotCall>(call => call.Kind == "Edit" && call.MessageId == offer.MessageId && call.Keyboard == null);
        var request = _bot.ToChat(OwnerId).Last(call => call.ButtonData.Any(data => data.StartsWith("access:", StringComparison.Ordinal)));
        request.Text.Should().Contain("Reza").And.Contain("@reza_202").And.Contain("202");
        request.ButtonData.Should().HaveCount(3).And.Contain(data => data.StartsWith("access:approve:analyst:", StringComparison.Ordinal))
            .And.Contain(data => data.StartsWith("access:approve:admin:", StringComparison.Ordinal))
            .And.Contain(data => data.StartsWith("access:decline:", StringComparison.Ordinal));

        await CallbackAsync(OwnerId, request.ButtonData.First(data => data.StartsWith("access:approve:analyst:", StringComparison.Ordinal)), request.MessageId!.Value);

        (await UserStatusAsync(202)).Should().Be(("Active", "Analyst"));
        _bot.ToChat(202).Last().ButtonData.Should().Contain(["menu:new", "menu:players", "menu:help"]);
        _bot.ToChat(OwnerId).Last().Should().Match<BotCall>(call => call.Kind == "Edit" && call.MessageId == request.MessageId && call.Text!.Contains("approved", StringComparison.OrdinalIgnoreCase));
        (await AuditCountAsync("user.approved")).Should().BeGreaterThan(0);
    }

    /// <summary>A declined user is told so, and cannot ask again for 24 hours.</summary>
    [Fact]
    public async Task Declined_user_waits_before_asking_again()
    {
        await MessageAsync(OwnerId, "/start");
        await MessageAsync(203, "/start");
        await CallbackAsync(203, "access:request", _bot.ToChat(203).Last().MessageId!.Value);
        var request = _bot.ToChat(OwnerId).Last(call => call.ButtonData.Any(data => data.Contains(":decline:", StringComparison.Ordinal)));

        await CallbackAsync(OwnerId, request.ButtonData.First(data => data.StartsWith("access:decline:", StringComparison.Ordinal)), request.MessageId!.Value);

        (await UserStatusAsync(203)).Should().Be(("Declined", "Analyst"));
        _bot.ToChat(203).Last().Text.Should().Contain("24");
        (await AuditCountAsync("user.declined")).Should().BeGreaterThan(0);

        await CallbackAsync(203, "access:request", 1);

        (await UserStatusAsync(203)).Should().Be(("Declined", "Analyst"));
        _bot.ToChat(203).Last().Text.Should().Contain("again");
    }

    /// <summary>A non-admin cannot review requests: they get a pop-up and nothing changes.</summary>
    [Fact]
    public async Task Non_admins_cannot_review()
    {
        await MessageAsync(OwnerId, "/start");
        await MessageAsync(204, "/start");
        await CallbackAsync(204, "access:request", _bot.ToChat(204).Last().MessageId!.Value);
        var request = _bot.ToChat(OwnerId).Last(call => call.ButtonData.Any(data => data.Contains(":analyst:", StringComparison.Ordinal)));

        await CallbackAsync(205, request.ButtonData.First(data => data.Contains(":analyst:", StringComparison.Ordinal)), request.MessageId!.Value);

        _bot.Calls.Last().Should().Match<BotCall>(call => call.Kind == "Answer" && call.ShowAlert);
        (await UserStatusAsync(204)).Should().Be(("Pending", "Analyst"));
    }

    /// <summary>Pressing a button on a request that was already handled says who handled it.</summary>
    [Fact]
    public async Task Handled_requests_say_who_handled_them()
    {
        await MessageAsync(OwnerId, "/start");
        await MessageAsync(206, "/start");
        await CallbackAsync(206, "access:request", _bot.ToChat(206).Last().MessageId!.Value);
        var request = _bot.ToChat(OwnerId).Last(call => call.ButtonData.Any(data => data.Contains(":analyst:", StringComparison.Ordinal)));
        var approve = request.ButtonData.First(data => data.Contains(":analyst:", StringComparison.Ordinal));
        await CallbackAsync(OwnerId, approve, request.MessageId!.Value);

        await CallbackAsync(OwnerId, request.ButtonData.First(data => data.StartsWith("access:decline:", StringComparison.Ordinal)), request.MessageId!.Value);

        _bot.ToChat(OwnerId).Last().Text.Should().Contain("already").And.Contain("Owner");
        (await UserStatusAsync(206)).Should().Be(("Active", "Analyst"));
    }

    /// <summary>Approving as admin someone without an email shows a pop-up and leaves the request pending.</summary>
    [Fact]
    public async Task Admin_approval_needs_an_email()
    {
        await MessageAsync(OwnerId, "/start");
        await MessageAsync(207, "/start");
        await CallbackAsync(207, "access:request", _bot.ToChat(207).Last().MessageId!.Value);
        var request = _bot.ToChat(OwnerId).Last(call => call.ButtonData.Any(data => data.Contains(":admin:", StringComparison.Ordinal)));

        await CallbackAsync(OwnerId, request.ButtonData.First(data => data.Contains(":admin:", StringComparison.Ordinal)), request.MessageId!.Value);

        _bot.Calls.Last().Should().Match<BotCall>(call => call.Kind == "Answer" && call.ShowAlert && call.Text!.Contains("email", StringComparison.OrdinalIgnoreCase));
        (await UserStatusAsync(207)).Should().Be(("Pending", "Analyst"));
    }

    /// <summary>/help answers with the help text; without a Mini App URL the menu buttons say the Mini App is not set up.</summary>
    [Fact]
    public async Task Help_and_mini_app_not_set_up()
    {
        await MessageAsync(208, "/help");
        _bot.ToChat(208).Last().Text.Should().Contain("/start");

        await CallbackAsync(208, "menu:players", 5);
        _bot.Calls.Last().Should().Match<BotCall>(call => call.Kind == "Answer" && call.Text!.Contains("not set up", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Posts a text message update from a user.</summary>
    /// <param name="fromId">The sender's Telegram id.</param>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the API answered 200.</returns>
    private Task MessageAsync(long fromId, string text) => PostAsync(new
    {
        update_id = Interlocked.Increment(ref _updateId),
        message = new
        {
            message_id = 1,
            date = 1761379200,
            chat = new { id = fromId, type = "private" },
            from = new { id = fromId, is_bot = false, first_name = fromId == OwnerId ? "Owner" : "Reza", username = $"reza_{fromId}" },
            text,
        },
    });

    /// <summary>Posts a button press from a user on one of the bot's messages.</summary>
    /// <param name="fromId">The presser's Telegram id.</param>
    /// <param name="data">The button's callback data.</param>
    /// <param name="messageId">The message the button is on.</param>
    /// <returns>A task that completes when the API answered 200.</returns>
    private Task CallbackAsync(long fromId, string data, int messageId) => PostAsync(new
    {
        update_id = Interlocked.Increment(ref _updateId),
        callback_query = new
        {
            id = $"cb{Interlocked.Increment(ref _updateId)}",
            from = new { id = fromId, is_bot = false, first_name = fromId == OwnerId ? "Owner" : "Reza", username = $"reza_{fromId}" },
            chat_instance = "1",
            data,
            message = new { message_id = messageId, date = 1761379200, chat = new { id = fromId, type = "private" }, text = "…" },
        },
    });

    /// <summary>Posts an update to the webhook and expects 200.</summary>
    /// <param name="update">The update.</param>
    /// <returns>A task that completes when the API answered.</returns>
    private async Task PostAsync(object update)
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/telegram/webhook", UriKind.Relative)) { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Reads a user's status and role from the database.</summary>
    /// <param name="telegramId">The Telegram id.</param>
    /// <returns>The status and role, or <see langword="null"/> when there is no user.</returns>
    private async Task<(string Status, string Role)?> UserStatusAsync(long telegramId)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT status, role FROM users WHERE telegram_id = @id", connection);
        command.Parameters.AddWithValue("id", telegramId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        return await reader.ReadAsync(TestContext.Current.CancellationToken) ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    /// <summary>Counts audit entries with an action.</summary>
    /// <param name="action">The action code.</param>
    /// <returns>The count.</returns>
    private async Task<long> AuditCountAsync(string action)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit_entries WHERE action = @action", connection);
        command.Parameters.AddWithValue("action", action);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
