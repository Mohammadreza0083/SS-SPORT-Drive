using Microsoft.Extensions.Logging.Testing;
using SsSport.Drive.Application.Telegram;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>
/// An incoming Telegram update is recorded once; a repeat delivery (Telegram retries) is recognised and skipped.
/// The log carries ids only, never message text.
/// </summary>
public sealed class HandleTelegramUpdateTests
{
    /// <summary>The recorded update ids.</summary>
    private readonly InMemoryInbox _inbox = new();

    /// <summary>The handler's log.</summary>
    private readonly FakeLogger<HandleTelegramUpdateCommandHandler> _logger = new();

    /// <summary>A new update is accepted and logged with its id, kind and sender.</summary>
    [Fact]
    public async Task A_new_update_is_accepted()
    {
        var outcome = await Handler().Handle(new HandleTelegramUpdateCommand(1001, "Message", 42), TestContext.Current.CancellationToken);

        outcome.Should().Be(TelegramUpdateOutcome.Accepted);
        _inbox.Ids.Should().Equal(1001);
        var entry = _logger.LatestRecord;
        entry.Message.Should().Contain("1001").And.Contain("Message").And.Contain("42");
    }

    /// <summary>The same update id again is a duplicate and is not processed twice.</summary>
    [Fact]
    public async Task A_repeated_update_is_a_duplicate()
    {
        var handler = Handler();
        await handler.Handle(new HandleTelegramUpdateCommand(2002, "CallbackQuery", 7), TestContext.Current.CancellationToken);

        var outcome = await handler.Handle(new HandleTelegramUpdateCommand(2002, "CallbackQuery", 7), TestContext.Current.CancellationToken);

        outcome.Should().Be(TelegramUpdateOutcome.Duplicate);
        _inbox.Ids.Should().Equal(2002);
    }

    /// <summary>The validator rejects negative ids and a missing kind.</summary>
    [Fact]
    public void Validator_rejects_bad_updates()
    {
        var validator = new HandleTelegramUpdateCommandValidator();

        validator.Validate(new HandleTelegramUpdateCommand(-1, "Message", null)).IsValid.Should().BeFalse();
        validator.Validate(new HandleTelegramUpdateCommand(1, " ", null)).IsValid.Should().BeFalse();
        validator.Validate(new HandleTelegramUpdateCommand(1, "Message", null)).IsValid.Should().BeTrue();
    }

    /// <summary>Builds the handler under test.</summary>
    /// <returns>The handler.</returns>
    private HandleTelegramUpdateCommandHandler Handler() => new(_inbox, TimeProvider.System, _logger);

    /// <summary>An in-memory inbox that records ids once.</summary>
    private sealed class InMemoryInbox : ITelegramUpdateInbox
    {
        /// <summary>Gets the recorded ids, in order.</summary>
        public List<long> Ids { get; } = [];

        /// <summary>Records an id unless it is already there.</summary>
        /// <param name="updateId">The update id.</param>
        /// <param name="receivedAt">Unused.</param>
        /// <param name="cancellationToken">Unused.</param>
        /// <returns><see langword="true"/> for a new id.</returns>
        public Task<bool> TryRecordAsync(long updateId, DateTimeOffset receivedAt, CancellationToken cancellationToken)
        {
            if (Ids.Contains(updateId))
            {
                return Task.FromResult(false);
            }

            Ids.Add(updateId);
            return Task.FromResult(true);
        }
    }
}
