using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.Audit;

/// <summary>Audit entries record who did which sensitive action to what, with ids only and no email addresses.</summary>
public sealed class AuditEntryTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An actor.</summary>
    private static readonly Guid _actor = Guid.CreateVersion7(_now);

    /// <summary>An entry stores the actor, action, target, data and time.</summary>
    [Fact]
    public void Recording_an_entry()
    {
        var entry = AuditEntry.Record(_actor, "user.role_changed", "User", "0192f1c2-aaaa", """{"from":"Analyst","to":"Admin"}""", _now);

        entry.Id.Version.Should().Be(7);
        entry.ActorUserId.Should().Be(_actor);
        entry.Action.Should().Be("user.role_changed");
        entry.TargetType.Should().Be("User");
        entry.TargetId.Should().Be("0192f1c2-aaaa");
        entry.Data.Should().Be("""{"from":"Analyst","to":"Admin"}""");
        entry.At.Should().Be(_now);
    }

    /// <summary>System actions have no actor, and data is optional.</summary>
    [Fact]
    public void System_entries_have_no_actor()
    {
        var entry = AuditEntry.Record(null, "asset.hidden", "Asset", "a1", null, _now);

        entry.ActorUserId.Should().BeNull();
        entry.Data.Should().BeNull();
    }

    /// <summary>Actions are dot-separated lower-case codes.</summary>
    /// <param name="action">The action.</param>
    [Theory]
    [InlineData("")]
    [InlineData("hidden")]
    [InlineData("User.RoleChanged")]
    [InlineData("user role")]
    public void Malformed_actions_are_rejected(string action)
    {
        var record = () => AuditEntry.Record(_actor, action, "User", "1", null, _now);

        record.Should().Throw<DomainException>().Which.Code.Should().Be("audit.action_invalid");
    }

    /// <summary>The target type and id are required.</summary>
    /// <param name="type">The target type.</param>
    /// <param name="id">The target id.</param>
    [Theory]
    [InlineData("", "1")]
    [InlineData("Share Link", "1")]
    [InlineData("User", " ")]
    public void Target_is_required(string type, string id)
    {
        var record = () => AuditEntry.Record(_actor, "user.removed", type, id, null, _now);

        record.Should().Throw<DomainException>().Which.Code.Should().Be("audit.target_invalid");
    }

    /// <summary>Data must be a JSON object.</summary>
    /// <param name="data">The data.</param>
    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    public void Data_must_be_a_json_object(string data)
    {
        var record = () => AuditEntry.Record(_actor, "user.removed", "User", "1", data, _now);

        record.Should().Throw<DomainException>().Which.Code.Should().Be("audit.data_invalid");
    }

    /// <summary>Data that contains an email address is rejected: entries refer to people by id.</summary>
    [Fact]
    public void Data_with_an_email_is_rejected()
    {
        var record = () => AuditEntry.Record(_actor, "link.created", "ShareLink", "1", """{"recipient":"scout@club.com"}""", _now);

        record.Should().Throw<DomainException>().Which.Code.Should().Be("audit.data_contains_email");
    }

    /// <summary>Data is capped at 4000 characters.</summary>
    [Fact]
    public void Overlong_data_is_rejected()
    {
        var record = () => AuditEntry.Record(_actor, "user.removed", "User", "1", $$"""{"x":"{{new string('a', 4000)}}"}""", _now);

        record.Should().Throw<DomainException>().Which.Code.Should().Be("audit.data_invalid");
    }
}
