using SsSport.Drive.Application.Emails;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Emails;

/// <summary>
/// A-6b (found 2026-11-15): the email that brings a link made in the console's New share link modal. It names the
/// players, carries the note and the link, says until when it works, and encodes every value put into HTML.
/// </summary>
public sealed class LinkSharedEmailTests
{
    /// <summary>The link.</summary>
    private static readonly Uri _link = new("https://drive.ss-sport.co/r/Kq7mTx");

    /// <summary>The email names the players and the recipient, and carries the note, the link and the expiry date.</summary>
    [Fact]
    public void It_carries_the_players_the_note_the_link_and_the_expiry()
    {
        var message = EmailContent.LinkShared(
            EmailAddress.Create("marco@lube.it"),
            "Marco Rossi",
            "Arash Tehrani, Ali Hoshmand",
            "Watch the second set.",
            _link,
            new DateTimeOffset(2026, 12, 15, 9, 0, 0, TimeSpan.Zero));

        message.To.Value.Should().Be("marco@lube.it");
        message.Subject.Should().Be("SS-SPORT shared Arash Tehrani, Ali Hoshmand with you");
        message.Text.Should().Contain("Marco Rossi").And.Contain("Arash Tehrani, Ali Hoshmand").And.Contain("Watch the second set.")
            .And.Contain("https://drive.ss-sport.co/r/Kq7mTx").And.Contain("15 December 2026");
        message.Html.Should().Contain("href=\"https://drive.ss-sport.co/r/Kq7mTx\"").And.Contain("Watch the second set.").And.Contain("15 December 2026");
    }

    /// <summary>Names and the note are encoded in the HTML part.</summary>
    [Fact]
    public void Values_are_encoded_in_html()
    {
        var message = EmailContent.LinkShared(
            EmailAddress.Create("marco@lube.it"), "<i>Marco</i>", "Arash <b>Tehrani</b>", "<script>x</script>", _link, null);

        message.Html.Should().Contain("Arash &lt;b&gt;Tehrani&lt;/b&gt;").And.Contain("&lt;script&gt;").And.Contain("&lt;i&gt;Marco&lt;/i&gt;")
            .And.NotContain("<b>Tehrani</b>").And.NotContain("<script>").And.NotContain("<i>Marco</i>");
    }

    /// <summary>Without a note there is no empty note block.</summary>
    [Fact]
    public void Without_a_note_nothing_is_left_empty()
    {
        var message = EmailContent.LinkShared(EmailAddress.Create("marco@lube.it"), "Marco Rossi", "Arash Tehrani", null, _link, null);

        message.Text.Should().NotContain("\n\n\n");
        message.Html.Should().NotContain("<blockquote");
    }
}
