using Ardalis.Specification;
using NSubstitute;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Links;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.UnitTests.Links;

/// <summary>
/// L-3b: when an admin keeps a forwarded link, the viewer is emailed that the link is open, through the email queue
/// (Google SMTP, template <c>forward.kept</c>) in the same save as the decision. Block sends nothing, so the blocked
/// person cannot tell; a second press sends nothing either. The queued data names the link, never the email.
/// </summary>
public sealed class ForwardKeptEmailTests
{
    /// <summary>The fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The admin's Telegram id.</summary>
    private const long AdminTelegramId = 802;

    /// <summary>The users.</summary>
    private readonly IRepository<User, Guid> _users = Substitute.For<IRepository<User, Guid>>();

    /// <summary>The forwards.</summary>
    private readonly IRepository<LinkForward, Guid> _forwards = Substitute.For<IRepository<LinkForward, Guid>>();

    /// <summary>The links.</summary>
    private readonly IRepository<ShareLink, Guid> _links = Substitute.For<IRepository<ShareLink, Guid>>();

    /// <summary>The trusted devices.</summary>
    private readonly IRepository<TrustedDevice, Guid> _devices = Substitute.For<IRepository<TrustedDevice, Guid>>();

    /// <summary>The viewer sessions.</summary>
    private readonly IRepository<ViewerSession, Guid> _sessions = Substitute.For<IRepository<ViewerSession, Guid>>();

    /// <summary>The audit log.</summary>
    private readonly IRepository<AuditEntry, Guid> _audit = Substitute.For<IRepository<AuditEntry, Guid>>();

    /// <summary>The email queue.</summary>
    private readonly IRepository<OutgoingEmail, Guid> _emails = Substitute.For<IRepository<OutgoingEmail, Guid>>();

    /// <summary>The link.</summary>
    private readonly ShareLink _link;

    /// <summary>The forward waiting for a decision.</summary>
    private readonly LinkForward _forward;

    /// <summary>Stores an admin, a link and a waiting forward.</summary>
    public ForwardKeptEmailTests()
    {
        var admin = User.CreateFirstAdmin(AdminTelegramId, "Owner", EmailAddress.Create("owner@ss-sport.co"), _now);
        _users.FirstOrDefaultAsync(Arg.Any<ISpecification<User>>(), Arg.Any<CancellationToken>()).Returns(admin);
        _users.GetByIdAsync(admin.Id, Arg.Any<CancellationToken>()).Returns(admin);

        var recipient = Recipient.Create("Marco Rossi", "Lube Volley", null, [EmailAddress.Create("marco@lube.it")], _now);
        _link = ShareLink.Create(ShareLinkCode.Generate(), recipient, [PublishedPlayer()], null, null, ShareLinkSettings.Default, admin.Id, _now);
        _links.GetByIdAsync(_link.Id, Arg.Any<CancellationToken>()).Returns(_link);

        _forward = LinkForward.Record(_link.Id, EmailAddress.Create("scout@other.com"), _now);
        _forwards.GetByIdAsync(_forward.Id, Arg.Any<CancellationToken>()).Returns(_forward);
        _devices.ListAsync(Arg.Any<ISpecification<TrustedDevice>>(), Arg.Any<CancellationToken>()).Returns([]);
        _sessions.ListAsync(Arg.Any<ISpecification<ViewerSession>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    /// <summary>Keep queues one general email to the forwarded address, naming only the link.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Keep_queues_one_email()
    {
        var result = await Handler().Handle(new DecideForwardCommand(AdminTelegramId, _forward.Id, Keep: true), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ForwardDecisionOutcome.Decided);
        var email = (OutgoingEmail)_emails.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0]!;
        email.Kind.Should().Be(EmailKind.General);
        email.Template.Should().Be(EmailTemplates.ForwardKept);
        EmailTemplates.ForwardKept.Should().Be("forward.kept");
        email.To.Value.Should().Be("scout@other.com");
        email.Data.Should().Be($$"""{"shareLinkId":"{{_link.Id:D}}"}""");
        email.Status.Should().Be(EmailStatus.Queued);
    }

    /// <summary>Block sends nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Block_sends_nothing()
    {
        await Handler().Handle(new DecideForwardCommand(AdminTelegramId, _forward.Id, Keep: false), TestContext.Current.CancellationToken);

        _emails.DidNotReceive().Add(Arg.Any<OutgoingEmail>());
    }

    /// <summary>A second press on an already decided forward sends nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_second_press_sends_nothing()
    {
        _forward.Decide(keep: true, Guid.CreateVersion7(), _now);

        var result = await Handler().Handle(new DecideForwardCommand(AdminTelegramId, _forward.Id, Keep: true), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ForwardDecisionOutcome.AlreadyDecided);
        _emails.DidNotReceive().Add(Arg.Any<OutgoingEmail>());
    }

    /// <summary>Builds the handler with the email queue.</summary>
    /// <returns>The handler.</returns>
    private DecideForwardCommandHandler Handler() =>
        new(_users, _forwards, _links, _devices, _sessions, _audit, _emails, new FixedClock(_now));

    /// <summary>A published player.</summary>
    /// <returns>The player.</returns>
    private static Player PublishedPlayer()
    {
        var player = Player.Create(PlayerCategory.Men, _now);
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("IR"));
        player.ChangePositions(PlayerPositions.Create([Position.Setter]));
        player.ChangePhoto(PlayerPhoto.Create($"players/{player.Id}/photo.webp"));
        player.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/arash-tehrani-p81001"));
        player.Publish(_now);
        return player;
    }

    /// <summary>A clock stuck at one moment.</summary>
    /// <param name="now">The moment.</param>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;
    }
}
