using System.Text;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Telegram.AccessRequests;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Public;

/// <summary>
/// The bot side of the access-request inbox (P-4): the Approve / Decline buttons on the admins' alert, and the email
/// the requester gets with their personal link.
/// </summary>
public sealed class AccessRequestCallbacksTests
{
    /// <summary>A request id.</summary>
    private static readonly Guid _requestId = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c");

    /// <summary>The alert has Approve then Decline on one row; both fit Telegram's 64-byte callback data.</summary>
    [Fact]
    public void The_alert_has_approve_and_decline_on_one_row()
    {
        var keyboard = AccessRequestCallbacks.Keyboard(_requestId);

        var row = keyboard.Rows.Should().ContainSingle().Subject;
        row.Select(button => button.Text).Should().Equal("Approve (30 days)", "Decline");
        row.Select(button => button.CallbackData).Should().Equal("areq:ok:019a3e1c5b2a7f10a4c38d9e0f1a2b3c", "areq:no:019a3e1c5b2a7f10a4c38d9e0f1a2b3c");
        row.Should().OnlyContain(button => Encoding.UTF8.GetByteCount(button.CallbackData) <= 64);
    }

    /// <summary>Each button reads back as its decision and request.</summary>
    [Fact]
    public void A_button_reads_back_as_its_decision()
    {
        var row = AccessRequestCallbacks.Keyboard(_requestId).Rows.Single();

        AccessRequestCallbacks.TryParse(row[0].CallbackData, out var approve, out var id).Should().BeTrue();
        approve.Should().BeTrue();
        id.Should().Be(_requestId);

        AccessRequestCallbacks.TryParse(row[1].CallbackData, out approve, out id).Should().BeTrue();
        approve.Should().BeFalse();
        id.Should().Be(_requestId);
    }

    /// <summary>Other buttons (forward Keep/Block, team access review) and broken ids are not access-request buttons.</summary>
    /// <param name="data">The callback data.</param>
    [Theory]
    [InlineData("fwd:keep:019a3e1c5b2a7f10a4c38d9e0f1a2b3c")]
    [InlineData("access:decline:019a3e1c5b2a7f10a4c38d9e0f1a2b3c")]
    [InlineData("areq:ok:not-a-guid")]
    [InlineData("areq:maybe:019a3e1c5b2a7f10a4c38d9e0f1a2b3c")]
    [InlineData("")]
    public void Other_data_is_not_an_access_request_button(string data) =>
        AccessRequestCallbacks.TryParse(data, out _, out _).Should().BeFalse();

    /// <summary>
    /// The approval email names the players and carries the personal link; the HTML part encodes what it shows, so a
    /// player name cannot inject markup.
    /// </summary>
    [Fact]
    public void The_approval_email_carries_the_link_and_encodes_names()
    {
        var link = new Uri("https://drive.ss-sport.co/r/Kq7mTx");

        var message = EmailContent.AccessApproved(EmailAddress.Create("j.demir@gmail.com"), "Arash <b>Tehrani</b>", link);

        message.To.Value.Should().Be("j.demir@gmail.com");
        message.Subject.Should().Be("Your SS-SPORT Drive access is approved");
        message.Text.Should().Contain("https://drive.ss-sport.co/r/Kq7mTx").And.Contain("Arash <b>Tehrani</b>");
        message.Html.Should().Contain("href=\"https://drive.ss-sport.co/r/Kq7mTx\"")
            .And.Contain("Arash &lt;b&gt;Tehrani&lt;/b&gt;")
            .And.NotContain("<b>Tehrani</b>");
    }
}
