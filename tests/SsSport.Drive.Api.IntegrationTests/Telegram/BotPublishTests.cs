using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Api.IntegrationTests.Support;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Api.IntegrationTests.Telegram;

/// <summary>
/// T-11 (owner, 2026-11-15): an admin who owns a player publishes it from the bot.
/// <list type="bullet">
/// <item>the progress message of an admin's complete, waiting player says it is ready and carries a <b>Publish</b> button;
/// an analyst's player keeps "Waiting for an admin to publish" and no button;</item>
/// <item>pressing it publishes with the console's rules (complete only, once), audits <c>player.published</c> with
/// the admin as actor, and the progress message turns to published without the button;</item>
/// <item>only active admins may press it; an incomplete or already published player says so and nothing changes.</item>
/// </list>
/// </summary>
public sealed class BotPublishTests : IAsyncLifetime
{
    /// <summary>The webhook secret the test host configures.</summary>
    private const string WebhookSecret = "backend-test-secret";

    /// <summary>The ready line of an admin's complete player.</summary>
    private const string ReadyLine = "⏳ Ready to publish: press Publish below";

    /// <summary>Counts webhook updates so each has its own id.</summary>
    private int _updateId;

    /// <summary>The API and database.</summary>
    private BackendTestHost _host = null!;

    /// <summary>Boots the API.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    public async ValueTask InitializeAsync() => _host = await BackendTestHost.StartAsync();

    /// <summary>Stops the API.</summary>
    /// <returns>A task that completes when it is gone.</returns>
    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    /// <summary>An admin's complete player: the progress message says it is ready and has the Publish button.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_admins_complete_player_gets_the_publish_button()
    {
        var player = await _host.AddWaitingPlayerAsync("Bot Publish Alpha", analystId: _host.AdminId);

        var message = await ProgressMessageAsync(BackendTestHost.AdminTelegramId, "Bot Publish Alpha");

        message.Text.Should().EndWith(ReadyLine);
        message.ButtonData.Should().Equal($"pub:{player:N}");
    }

    /// <summary>An analyst's player keeps waiting for an admin, with no button.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_analysts_player_has_no_button()
    {
        await _host.AddWaitingPlayerAsync("Bot Publish Bravo", analystId: _host.AnalystId);

        var message = await ProgressMessageAsync(BackendTestHost.AnalystTelegramId, "Bot Publish Bravo");

        message.Text.Should().EndWith("⏳ Waiting for an admin to publish");
        message.ButtonData.Should().BeEmpty();
    }

    /// <summary>Pressing Publish publishes, audits with the admin as actor, and the message turns published without the button.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Pressing_publish_publishes_the_player()
    {
        var player = await _host.AddWaitingPlayerAsync("Bot Publish Charlie", analystId: _host.AdminId);
        var sent = await ProgressMessageAsync(BackendTestHost.AdminTelegramId, "Bot Publish Charlie");

        await PressAsync(BackendTestHost.AdminTelegramId, $"pub:{player:N}");

        (await StatusOfAsync(player)).Should().Be(PlayerStatus.Published);
        var audit = await _host.QueryAsync(db => db.Set<AuditEntry>().AsNoTracking()
            .Where(entry => entry.Action == "player.published" && entry.TargetId == player.ToString())
            .ToListAsync(TestContext.Current.CancellationToken));
        audit.Should().ContainSingle().Which.ActorUserId.Should().Be(_host.AdminId);
        _host.Bot.Calls.Should().Contain(call => call.Kind == "Answer" && call.Text == "✅ Published." && !call.ShowAlert);
        var edit = await _host.Bot.WaitForAsync(
            call => call.Kind == "Edit" && call.MessageId == sent.MessageId && call.Text!.Contains("✅ Published by an admin", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        edit.ButtonData.Should().BeEmpty();
    }

    /// <summary>An analyst's press publishes nothing and is told only admins can.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_analysts_press_publishes_nothing()
    {
        var player = await _host.AddWaitingPlayerAsync("Bot Publish Delta", analystId: _host.AdminId);

        await PressAsync(BackendTestHost.AnalystTelegramId, $"pub:{player:N}");

        (await StatusOfAsync(player)).Should().Be(PlayerStatus.Waiting);
        _host.Bot.Calls.Should().Contain(call => call.Kind == "Answer" && call.Text == "Only admins can publish players." && call.ShowAlert);
    }

    /// <summary>An incomplete player is not published; the admin is told what is missing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_incomplete_player_says_what_is_missing()
    {
        var player = await _host.AddWaitingPlayerAsync("Bot Publish Echo", [PlayerDetail.Photo], _host.AdminId);

        await PressAsync(BackendTestHost.AdminTelegramId, $"pub:{player:N}");

        (await StatusOfAsync(player)).Should().Be(PlayerStatus.Waiting);
        _host.Bot.Calls.Should().Contain(call => call.Kind == "Answer" && call.Text == "Can't publish yet. Still missing: Photo" && call.ShowAlert);
    }

    /// <summary>A second press says it is already published and audits nothing more.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_second_press_changes_nothing()
    {
        var player = await _host.AddWaitingPlayerAsync("Bot Publish Foxtrot", analystId: _host.AdminId);
        await PressAsync(BackendTestHost.AdminTelegramId, $"pub:{player:N}");

        await PressAsync(BackendTestHost.AdminTelegramId, $"pub:{player:N}");

        _host.Bot.Calls.Should().Contain(call => call.Kind == "Answer" && call.Text == "Already published.");
        (await _host.QueryAsync(db => db.Set<AuditEntry>().CountAsync(
            entry => entry.Action == "player.published" && entry.TargetId == player.ToString(),
            TestContext.Current.CancellationToken))).Should().Be(1);
    }

    /// <summary>A press for a player that does not exist answers without failing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_unknown_player_is_answered()
    {
        await PressAsync(BackendTestHost.AdminTelegramId, $"pub:{Guid.NewGuid():N}");

        _host.Bot.Calls.Should().Contain(call => call.Kind == "Answer" && call.Text == "This player no longer exists." && call.ShowAlert);
    }

    /// <summary>Waits for the first progress message of a player in a chat.</summary>
    /// <param name="chatId">The owner's chat.</param>
    /// <param name="name">The player's name, the first line of the message.</param>
    /// <returns>The sent message.</returns>
    private Task<BotCall> ProgressMessageAsync(long chatId, string name) =>
        _host.Bot.WaitForAsync(
            call => call.Kind == "Send" && call.ChatId == chatId && call.Text!.Contains(name, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

    /// <summary>Presses a button in the bot through the webhook.</summary>
    /// <param name="fromId">Who presses.</param>
    /// <param name="data">The button data.</param>
    /// <returns>A task that completes when the webhook answered.</returns>
    private async Task PressAsync(long fromId, string data)
    {
        var update = new
        {
            update_id = Interlocked.Increment(ref _updateId),
            callback_query = new
            {
                id = $"cb{Interlocked.Increment(ref _updateId)}",
                from = new { id = fromId, is_bot = false, first_name = "Member", username = $"user_{fromId}" },
                chat_instance = "1",
                data,
                message = new { message_id = 7, date = 1761379200, chat = new { id = fromId, type = "private" }, text = "progress" },
            },
        };
        using var client = _host.Api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/telegram/webhook", UriKind.Relative)) { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", WebhookSecret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Reads a player's status.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The status.</returns>
    private Task<PlayerStatus> StatusOfAsync(Guid player) =>
        _host.QueryAsync(db => db.Set<Player>().AsNoTracking().Where(p => p.Id == player).Select(p => p.Status).SingleAsync(TestContext.Current.CancellationToken));
}
