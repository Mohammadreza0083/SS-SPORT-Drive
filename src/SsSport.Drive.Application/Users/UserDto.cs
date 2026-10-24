namespace SsSport.Drive.Application.Users;

/// <summary>A team member, for admin-only screens; the email is complete here and masked only in logs.</summary>
public sealed record UserDto
{
    /// <summary>Gets the user id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the Telegram user id.</summary>
    public long TelegramId { get; init; }

    /// <summary>Gets the Telegram username, or <see langword="null"/>.</summary>
    public string? Username { get; init; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the sign-in email, or <see langword="null"/>.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the role name (<c>Analyst</c> or <c>Admin</c>).</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Gets the status (<c>Pending</c>, <c>Active</c>, <c>Declined</c>, <c>Removed</c>).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets when access was last requested (UTC).</summary>
    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>Gets when the user was last active (UTC), or <see langword="null"/>.</summary>
    public DateTimeOffset? LastActiveAt { get; init; }
}
