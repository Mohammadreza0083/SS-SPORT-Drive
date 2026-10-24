namespace SsSport.Drive.Application.Audit;

/// <summary>One audit log entry: who did what to which target, and when.</summary>
public sealed record AuditEntryDto
{
    /// <summary>Gets the entry id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the acting user's id, or <see langword="null"/> for the system.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>Gets the action code, e.g. <c>user.role_changed</c>.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Gets the target's type, e.g. <c>User</c>.</summary>
    public string TargetType { get; init; } = string.Empty;

    /// <summary>Gets the target's id.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Gets extra details as a JSON object, or <see langword="null"/>.</summary>
    public string? Data { get; init; }

    /// <summary>Gets when it happened (UTC).</summary>
    public DateTimeOffset At { get; init; }
}
