using SsSport.Drive.Domain.Conversations;

namespace SsSport.Drive.Domain.UnitTests.Conversations;

/// <summary>
/// A bot conversation (T-10a) keeps one user's unfinished chat flow: the step it waits at and the draft so far, for
/// 24 hours after the last answer.
/// </summary>
public sealed class BotConversationTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A new conversation starts at the category with an empty draft and lives 24 hours.</summary>
    [Fact]
    public void Starts_at_the_category_with_an_empty_draft()
    {
        var conversation = BotConversation.StartNewPlayer(901, _now);

        conversation.TelegramUserId.Should().Be(901);
        conversation.Step.Should().Be(NewPlayerStep.Category);
        conversation.Draft.Should().Be(new NewPlayerDraft());
        conversation.ExpiresAt.Should().Be(_now.AddHours(24));
        conversation.IsExpired(_now.AddHours(23)).Should().BeFalse();
        conversation.IsExpired(_now.AddHours(24)).Should().BeTrue();
    }

    /// <summary>Each answer moves to a step with the new draft and starts the 24 hours again.</summary>
    [Fact]
    public void Moving_on_keeps_the_draft_and_extends_the_life()
    {
        var conversation = BotConversation.StartNewPlayer(901, _now);
        var draft = conversation.Draft with { Category = "Men", Name = "Ali Rezaei" };

        conversation.MoveTo(NewPlayerStep.Nationality, draft, _now.AddHours(20));

        conversation.Step.Should().Be(NewPlayerStep.Nationality);
        conversation.Draft.Name.Should().Be("Ali Rezaei");
        conversation.UpdatedAt.Should().Be(_now.AddHours(20));
        conversation.IsExpired(_now.AddHours(30)).Should().BeFalse();
    }

    /// <summary>Drafts compare by value, positions included, so an unchanged answer is recognised.</summary>
    [Fact]
    public void Drafts_compare_by_value()
    {
        var first = new NewPlayerDraft { Positions = ["OH", "MB"] };
        var second = new NewPlayerDraft { Positions = ["OH", "MB"] };

        first.Should().Be(second);
        first.HasAnswers.Should().BeTrue();
        new NewPlayerDraft().HasAnswers.Should().BeFalse();
    }
}
