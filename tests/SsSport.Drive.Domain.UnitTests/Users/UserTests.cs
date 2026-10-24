using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Domain.UnitTests.Users;

/// <summary>
/// Team members (analysts and admins), identified by Telegram id: request → approve / decline, remove,
/// come back, change role. Admins need an email for the console's magic-link sign-in.
/// </summary>
public sealed class UserTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An email address.</summary>
    private static readonly EmailAddress _email = EmailAddress.Create("coach@ss-sport.co");

    /// <summary>Requesting access creates a pending analyst and raises UserRequestedAccess.</summary>
    [Fact]
    public void Requesting_access_creates_a_pending_analyst()
    {
        var user = User.RequestAccess(123456789, "@reza_analyst", " Reza ", _now);

        user.Id.Version.Should().Be(7);
        user.TelegramId.Should().Be(123456789);
        user.Username.Should().Be("reza_analyst");
        user.DisplayName.Should().Be("Reza");
        user.Email.Should().BeNull();
        user.Role.Should().Be(Role.Analyst);
        user.Status.Should().Be(UserStatus.Pending);
        user.IsActive.Should().BeFalse();
        user.RequestedAt.Should().Be(_now);
        user.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserRequestedAccess(user.Id));
    }

    /// <summary>Telegram ids must be positive and display names are required.</summary>
    [Fact]
    public void Telegram_id_and_display_name_are_validated()
    {
        var badId = () => User.RequestAccess(0, null, "Reza", _now);
        var badName = () => User.RequestAccess(1, null, "  ", _now);
        var longName = () => User.RequestAccess(1, null, new string('a', 101), _now);

        badId.Should().Throw<DomainException>().Which.Code.Should().Be("user.telegram_id_invalid");
        badName.Should().Throw<DomainException>().Which.Code.Should().Be("user.display_name_invalid");
        longName.Should().Throw<DomainException>().Which.Code.Should().Be("user.display_name_invalid");
    }

    /// <summary>The first admin, from configuration, starts active with an email.</summary>
    [Fact]
    public void The_first_admin_starts_active()
    {
        var admin = User.CreateFirstAdmin(42, "Owner", _email, _now);

        admin.Status.Should().Be(UserStatus.Active);
        admin.Role.Should().Be(Role.Admin);
        admin.IsAdmin.Should().BeTrue();
        admin.Email.Should().Be(_email);
        admin.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserApproved(admin.Id, "Admin"));
    }

    /// <summary>Approving activates the user with the chosen role.</summary>
    [Fact]
    public void Approving_activates_the_user()
    {
        var user = Pending();
        user.ClearDomainEvents();

        user.Approve(Role.Analyst);

        user.Status.Should().Be(UserStatus.Active);
        user.IsActive.Should().BeTrue();
        user.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserApproved(user.Id, "Analyst"));
    }

    /// <summary>An admin needs an email: approving or promoting without one fails, and an admin's email cannot be cleared.</summary>
    [Fact]
    public void Admins_need_an_email()
    {
        var pending = Pending();
        var analyst = Active();

        var approve = () => pending.Approve(Role.Admin);
        var promote = () => analyst.ChangeRole(Role.Admin);

        approve.Should().Throw<DomainException>().Which.Code.Should().Be("user.admin_needs_email");
        promote.Should().Throw<DomainException>().Which.Code.Should().Be("user.admin_needs_email");

        analyst.ChangeEmail(_email);
        analyst.ChangeRole(Role.Admin);
        var clear = () => analyst.ChangeEmail(null);

        clear.Should().Throw<DomainException>().Which.Code.Should().Be("user.admin_needs_email");
    }

    /// <summary>Declining a pending user raises UserDeclined; only pending users can be approved or declined.</summary>
    [Fact]
    public void Declining_a_pending_user()
    {
        var user = Pending();
        user.ClearDomainEvents();

        user.Decline();

        user.Status.Should().Be(UserStatus.Declined);
        user.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserDeclined(user.Id));

        var approve = () => user.Approve(Role.Analyst);
        approve.Should().Throw<DomainException>().Which.Code.Should().Be("user.invalid_state");
    }

    /// <summary>Removing an active user keeps the record and raises UserRemoved; only active users can be removed.</summary>
    [Fact]
    public void Removing_an_active_user()
    {
        var user = Active();
        user.ClearDomainEvents();

        user.Remove();

        user.Status.Should().Be(UserStatus.Removed);
        user.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserRemoved(user.Id));

        var removeAgain = () => user.Remove();
        removeAgain.Should().Throw<DomainException>().Which.Code.Should().Be("user.invalid_state");
    }

    /// <summary>A declined or removed user who writes to the bot again goes back to pending.</summary>
    [Fact]
    public void Declined_or_removed_users_can_request_again()
    {
        var declined = Pending();
        declined.Decline();
        var removed = Active();
        removed.Remove();
        declined.ClearDomainEvents();

        declined.RequestAgain(_now.AddDays(1));
        removed.RequestAgain(_now.AddDays(1));

        declined.Status.Should().Be(UserStatus.Pending);
        removed.Status.Should().Be(UserStatus.Pending);
        declined.RequestedAt.Should().Be(_now.AddDays(1));
        declined.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserRequestedAccess(declined.Id));
    }

    /// <summary>Requesting again while pending or active changes nothing.</summary>
    [Fact]
    public void Requesting_again_while_pending_or_active_changes_nothing()
    {
        var pending = Pending();
        var active = Active();
        pending.ClearDomainEvents();
        active.ClearDomainEvents();

        pending.RequestAgain(_now.AddDays(1));
        active.RequestAgain(_now.AddDays(1));

        pending.RequestedAt.Should().Be(_now);
        active.Status.Should().Be(UserStatus.Active);
        pending.DomainEvents.Should().BeEmpty();
        active.DomainEvents.Should().BeEmpty();
    }

    /// <summary>Changing the role of an active user raises UserRoleChanged; the same role raises nothing.</summary>
    [Fact]
    public void Changing_the_role()
    {
        var user = Active();
        user.ChangeEmail(_email);
        user.ClearDomainEvents();

        user.ChangeRole(Role.Analyst);
        user.ChangeRole(Role.Admin);

        user.Role.Should().Be(Role.Admin);
        user.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserRoleChanged(user.Id, "Analyst", "Admin"));
    }

    /// <summary>Only active users can change role.</summary>
    [Fact]
    public void Only_active_users_change_role()
    {
        var user = Pending();

        var change = () => user.ChangeRole(Role.Analyst);

        change.Should().Throw<DomainException>().Which.Code.Should().Be("user.invalid_state");
    }

    /// <summary>The Telegram profile and the last activity time can be updated.</summary>
    [Fact]
    public void Profile_and_activity_are_updated()
    {
        var user = Active();

        user.UpdateProfile(null, "Reza M.");
        user.RecordActivity(_now.AddHours(3));

        user.Username.Should().BeNull();
        user.DisplayName.Should().Be("Reza M.");
        user.LastActiveAt.Should().Be(_now.AddHours(3));
    }

    /// <summary>Builds a pending user.</summary>
    /// <returns>The user.</returns>
    private static User Pending() => User.RequestAccess(1001, "reza", "Reza", _now);

    /// <summary>Builds an active analyst.</summary>
    /// <returns>The user.</returns>
    private static User Active()
    {
        var user = Pending();
        user.Approve(Role.Analyst);
        return user;
    }
}
