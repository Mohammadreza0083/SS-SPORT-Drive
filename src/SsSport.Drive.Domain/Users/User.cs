using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Users;

/// <summary>Where a team member is in the access flow.</summary>
public enum UserStatus
{
    /// <summary>Asked for access through the bot; waiting for an admin.</summary>
    Pending,

    /// <summary>Approved and working.</summary>
    Active,

    /// <summary>The request was declined.</summary>
    Declined,

    /// <summary>Removed by an admin; the record is kept.</summary>
    Removed,
}

/// <summary>Raised when someone asks for team access (the first time or again).</summary>
/// <param name="UserId">The user.</param>
public sealed record UserRequestedAccess(Guid UserId) : IDomainEvent;

/// <summary>Raised when a user is approved (or the first admin is created).</summary>
/// <param name="UserId">The user.</param>
/// <param name="Role">The role name.</param>
public sealed record UserApproved(Guid UserId, string Role) : IDomainEvent;

/// <summary>Raised when an access request is declined.</summary>
/// <param name="UserId">The user.</param>
public sealed record UserDeclined(Guid UserId) : IDomainEvent;

/// <summary>Raised when an active user is removed; their players are reassigned by the caller.</summary>
/// <param name="UserId">The user.</param>
public sealed record UserRemoved(Guid UserId) : IDomainEvent;

/// <summary>Raised when an active user's role changes.</summary>
/// <param name="UserId">The user.</param>
/// <param name="From">The previous role name.</param>
/// <param name="To">The new role name.</param>
public sealed record UserRoleChanged(Guid UserId, string From, string To) : IDomainEvent;

/// <summary>
/// A team member (analyst or admin), identified by their Telegram id. Viewers are not users; they have their own
/// accounts. An admin needs an email for the console's magic-link sign-in. "Never remove the last admin" needs a count
/// and is enforced by the Application layer.
/// </summary>
public sealed class User : AggregateRoot<Guid>
{
    /// <summary>The longest display name.</summary>
    private const int MaxDisplayNameLength = 100;

    /// <summary>The longest Telegram username.</summary>
    private const int MaxUsernameLength = 64;

    /// <summary>Initializes a user; use <see cref="RequestAccess"/> or <see cref="CreateFirstAdmin"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="telegramId">The Telegram user id.</param>
    /// <param name="displayName">The validated display name.</param>
    /// <param name="requestedAt">When access was requested (UTC).</param>
    private User(Guid id, long telegramId, string displayName, DateTimeOffset requestedAt)
        : base(id)
    {
        TelegramId = telegramId;
        DisplayName = displayName;
        RequestedAt = requestedAt;
    }

    /// <summary>Gets the Telegram user id (unique).</summary>
    public long TelegramId { get; }

    /// <summary>Gets the Telegram username without the leading <c>@</c>, or <see langword="null"/>.</summary>
    public string? Username { get; private set; }

    /// <summary>Gets the name shown in the console and the bot.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Gets the email used for console sign-in, or <see langword="null"/>; required for admins.</summary>
    public EmailAddress? Email { get; private set; }

    /// <summary>Gets the role.</summary>
    public Role Role { get; private set; } = Role.Analyst;

    /// <summary>Gets the status.</summary>
    public UserStatus Status { get; private set; } = UserStatus.Pending;

    /// <summary>Gets when access was last requested (UTC).</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Gets when the user was last active (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? LastActiveAt { get; private set; }

    /// <summary>Gets a value indicating whether the user is active.</summary>
    public bool IsActive => Status == UserStatus.Active;

    /// <summary>Gets a value indicating whether the user is an active admin.</summary>
    public bool IsAdmin => IsActive && Role == Role.Admin;

    /// <summary>Records an access request from the bot: a pending analyst. Raises <see cref="UserRequestedAccess"/>.</summary>
    /// <param name="telegramId">The Telegram user id.</param>
    /// <param name="username">The Telegram username, with or without <c>@</c>.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="requestedAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The user.</returns>
    /// <exception cref="DomainException"><c>user.telegram_id_invalid</c> or <c>user.display_name_invalid</c>.</exception>
    public static User RequestAccess(long telegramId, string? username, string displayName, DateTimeOffset requestedAt)
    {
        var user = new User(Guid.CreateVersion7(requestedAt), ValidTelegramId(telegramId), ValidDisplayName(displayName), requestedAt)
        {
            Username = CleanUsername(username),
        };
        user.RaiseDomainEvent(new UserRequestedAccess(user.Id));
        return user;
    }

    /// <summary>Creates the first admin from configuration: active straight away. Raises <see cref="UserApproved"/>.</summary>
    /// <param name="telegramId">The Telegram user id.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="email">The sign-in email.</param>
    /// <param name="createdAt">The current time (UTC).</param>
    /// <returns>The admin.</returns>
    public static User CreateFirstAdmin(long telegramId, string displayName, EmailAddress email, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(email);
        var user = new User(Guid.CreateVersion7(createdAt), ValidTelegramId(telegramId), ValidDisplayName(displayName), createdAt)
        {
            Email = email,
            Role = Role.Admin,
            Status = UserStatus.Active,
        };
        user.RaiseDomainEvent(new UserApproved(user.Id, user.Role.Name));
        return user;
    }

    /// <summary>Approves a pending user with a role and raises <see cref="UserApproved"/>.</summary>
    /// <param name="role">The role to grant.</param>
    /// <exception cref="DomainException"><c>user.invalid_state</c> or <c>user.admin_needs_email</c>.</exception>
    public void Approve(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureStatus(UserStatus.Pending);
        EnsureEmailFor(role, Email);
        Role = role;
        Status = UserStatus.Active;
        RaiseDomainEvent(new UserApproved(Id, role.Name));
    }

    /// <summary>Declines a pending user and raises <see cref="UserDeclined"/>.</summary>
    public void Decline()
    {
        EnsureStatus(UserStatus.Pending);
        Status = UserStatus.Declined;
        RaiseDomainEvent(new UserDeclined(Id));
    }

    /// <summary>Removes an active user (the record stays) and raises <see cref="UserRemoved"/>.</summary>
    public void Remove()
    {
        EnsureStatus(UserStatus.Active);
        Status = UserStatus.Removed;
        RaiseDomainEvent(new UserRemoved(Id));
    }

    /// <summary>Puts a declined or removed user back on the pending list when they write to the bot again.</summary>
    /// <param name="requestedAt">The current time (UTC).</param>
    public void RequestAgain(DateTimeOffset requestedAt)
    {
        if (Status is not (UserStatus.Declined or UserStatus.Removed))
        {
            return;
        }

        Status = UserStatus.Pending;
        RequestedAt = requestedAt;
        RaiseDomainEvent(new UserRequestedAccess(Id));
    }

    /// <summary>Changes an active user's role and raises <see cref="UserRoleChanged"/>; the same role changes nothing.</summary>
    /// <param name="role">The new role.</param>
    /// <exception cref="DomainException"><c>user.invalid_state</c> or <c>user.admin_needs_email</c>.</exception>
    public void ChangeRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureStatus(UserStatus.Active);
        if (role == Role)
        {
            return;
        }

        EnsureEmailFor(role, Email);
        var previous = Role;
        Role = role;
        RaiseDomainEvent(new UserRoleChanged(Id, previous.Name, role.Name));
    }

    /// <summary>Sets or clears the sign-in email; an admin's email cannot be cleared.</summary>
    /// <param name="email">The email, or <see langword="null"/>.</param>
    /// <exception cref="DomainException"><c>user.admin_needs_email</c>.</exception>
    public void ChangeEmail(EmailAddress? email)
    {
        EnsureEmailFor(Role, email);
        Email = email;
    }

    /// <summary>Takes the latest Telegram profile.</summary>
    /// <param name="username">The Telegram username, or <see langword="null"/>.</param>
    /// <param name="displayName">The display name.</param>
    public void UpdateProfile(string? username, string displayName)
    {
        DisplayName = ValidDisplayName(displayName);
        Username = CleanUsername(username);
    }

    /// <summary>Records activity (a bot message, a console request).</summary>
    /// <param name="at">The current time (UTC).</param>
    public void RecordActivity(DateTimeOffset at) => LastActiveAt = at;

    /// <summary>Throws when the role is Admin and there is no email.</summary>
    /// <param name="role">The role.</param>
    /// <param name="email">The email.</param>
    private static void EnsureEmailFor(Role role, EmailAddress? email)
    {
        if (role == Role.Admin && email is null)
        {
            throw new DomainException("user.admin_needs_email", "An admin needs an email address to sign in to the console.");
        }
    }

    /// <summary>Validates a Telegram id.</summary>
    /// <param name="telegramId">The id.</param>
    /// <returns>The id.</returns>
    private static long ValidTelegramId(long telegramId) =>
        telegramId > 0 ? telegramId : throw new DomainException("user.telegram_id_invalid", "The Telegram user id must be positive.");

    /// <summary>Validates a display name.</summary>
    /// <param name="displayName">The raw name.</param>
    /// <returns>The trimmed name.</returns>
    private static string ValidDisplayName(string? displayName)
    {
        var text = (displayName ?? string.Empty).Trim();
        return text.Length is > 0 and <= MaxDisplayNameLength
            ? text
            : throw new DomainException("user.display_name_invalid", $"A display name needs 1 to {MaxDisplayNameLength} characters.");
    }

    /// <summary>Trims a username and drops the leading <c>@</c>; blank becomes <see langword="null"/>.</summary>
    /// <param name="username">The raw username.</param>
    /// <returns>The username, or <see langword="null"/>.</returns>
    private static string? CleanUsername(string? username)
    {
        var text = (username ?? string.Empty).Trim().TrimStart('@');
        return text.Length == 0 ? null : text[..Math.Min(text.Length, MaxUsernameLength)];
    }

    /// <summary>Throws unless the user has the given status.</summary>
    /// <param name="required">The required status.</param>
    private void EnsureStatus(UserStatus required)
    {
        if (Status != required)
        {
            throw new DomainException("user.invalid_state", $"This is only possible for {required} users; this user is {Status}.");
        }
    }
}
