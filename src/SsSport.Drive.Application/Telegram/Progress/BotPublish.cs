using System.Globalization;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Telegram.Progress;

/// <summary>
/// The <b>Publish</b> button on the progress message of an admin's complete, waiting player (T-11, owner 2026-11-15).
/// Its data is <c>pub:</c> and the player id in <c>N</c> format (36 bytes, under Telegram's 64).
/// </summary>
public static class PlayerPublishCallbacks
{
    /// <summary>Prefix of Publish buttons.</summary>
    private const string Prefix = "pub:";

    /// <summary>Builds the keyboard with the one Publish button.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The keyboard.</returns>
    public static BotKeyboard Keyboard(Guid playerId) =>
        new([[new BotButton(BotTexts.PublishButton, Prefix + playerId.ToString("N", CultureInfo.InvariantCulture))]]);

    /// <summary>Reads a Publish button.</summary>
    /// <param name="data">The callback data.</param>
    /// <param name="playerId">The player, when it is one.</param>
    /// <returns><see langword="true"/> for a Publish button with a valid id.</returns>
    public static bool TryParse(string data, out Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(data);
        playerId = Guid.Empty;
        return data.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(data[Prefix.Length..], "N", out playerId);
    }

    /// <summary>Words the answer to a press.</summary>
    /// <param name="result">What happened.</param>
    /// <returns>The text, and whether it is shown as a pop-up (every refusal is).</returns>
    public static (string Text, bool Alert) Describe(BotPublishResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            BotPublishOutcome.Published => (BotTexts.PublishDone, false),
            BotPublishOutcome.AlreadyPublished => (BotTexts.PublishAlready, false),
            BotPublishOutcome.Incomplete => (BotTexts.FormatPublishIncomplete(string.Join(", ", result.Missing)), true),
            BotPublishOutcome.NotAllowed => (BotTexts.OnlyAdminsPublish, true),
            _ => (BotTexts.PublishPlayerGone, true),
        };
    }
}

/// <summary>What a press of the bot's Publish button did.</summary>
public enum BotPublishOutcome
{
    /// <summary>The player is now published.</summary>
    Published,

    /// <summary>It was published before; nothing changed.</summary>
    AlreadyPublished,

    /// <summary>Details are still missing; nothing changed.</summary>
    Incomplete,

    /// <summary>The presser is not an active admin; nothing changed.</summary>
    NotAllowed,

    /// <summary>There is no such player.</summary>
    NotFound,
}

/// <summary>The result of a press of the bot's Publish button.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Missing">The missing details, for <see cref="BotPublishOutcome.Incomplete"/>; empty otherwise.</param>
public sealed record BotPublishResult(BotPublishOutcome Outcome, IReadOnlyList<PlayerDetail> Missing)
{
    /// <summary>Builds a result without missing details.</summary>
    /// <param name="outcome">What happened.</param>
    /// <returns>The result.</returns>
    public static BotPublishResult Of(BotPublishOutcome outcome) => new(outcome, []);
}

/// <summary>
/// Publishes a player from the bot's Publish button (T-11). The bot is signed in only by Telegram's webhook secret, so
/// the presser is checked here: only an active admin publishes, with the console's rules (complete only, once; see
/// <see cref="PublishPlayerCommand"/>), and the audit entry names that admin.
/// </summary>
/// <param name="AdminTelegramId">Who pressed the button.</param>
/// <param name="PlayerId">The player.</param>
public sealed record PublishPlayerFromBotCommand(long AdminTelegramId, Guid PlayerId) : ICommand<BotPublishResult>;

/// <summary>Validates <see cref="PublishPlayerFromBotCommand"/> on the server (decisions §1b rule 3).</summary>
public sealed class PublishPlayerFromBotCommandValidator : AbstractValidator<PublishPlayerFromBotCommand>
{
    /// <summary>Builds the rules.</summary>
    public PublishPlayerFromBotCommandValidator()
    {
        RuleFor(command => command.AdminTelegramId).ValidTelegramId().OverridePropertyName("adminTelegramId");
        RuleFor(command => command.PlayerId).ValidId().OverridePropertyName("playerId");
    }
}

/// <summary>Handles <see cref="PublishPlayerFromBotCommand"/>. Publishing raises <c>PlayerPublished</c>, which starts ingest and edits the progress message.</summary>
/// <param name="users">The team.</param>
/// <param name="players">The players.</param>
/// <param name="audit">The audit log.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class PublishPlayerFromBotCommandHandler(
    IRepository<User, Guid> users,
    IRepository<Player, Guid> players,
    IRepository<AuditEntry, Guid> audit,
    TimeProvider timeProvider)
    : ICommandHandler<PublishPlayerFromBotCommand, BotPublishResult>
{
    /// <summary>Checks the admin and the player, then publishes.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened.</returns>
    public async Task<BotPublishResult> Handle(PublishPlayerFromBotCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await users.FirstOrDefaultAsync(new UserByTelegramIdSpec(request.AdminTelegramId), cancellationToken) is not { IsAdmin: true } admin)
        {
            return BotPublishResult.Of(BotPublishOutcome.NotAllowed);
        }

        if (await players.GetByIdAsync(request.PlayerId, cancellationToken) is not { } player)
        {
            return BotPublishResult.Of(BotPublishOutcome.NotFound);
        }

        if (player.IsPublished)
        {
            return BotPublishResult.Of(BotPublishOutcome.AlreadyPublished);
        }

        var missing = player.MissingDetails();
        if (missing.Count > 0)
        {
            return new BotPublishResult(BotPublishOutcome.Incomplete, missing);
        }

        var now = timeProvider.GetUtcNow();
        player.Publish(now);
        audit.Add(AuditEntry.Record(admin.Id, AdminPlayerStatus.PublishedAction, AdminPlayerStatus.PlayerTarget, player.Id.ToString(), null, now));
        return BotPublishResult.Of(BotPublishOutcome.Published);
    }
}
