using Ardalis.Specification;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.Users;

/// <summary>The team member with a Telegram id.</summary>
public sealed class UserByTelegramIdSpec : Specification<User>, ISingleResultSpecification<User>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="telegramId">The Telegram user id.</param>
    public UserByTelegramIdSpec(long telegramId) => Query.Where(user => user.TelegramId == telegramId);
}

/// <summary>Every active admin, in sign-up order (they receive access requests).</summary>
public sealed class ActiveAdminsSpec : Specification<User>
{
    /// <summary>Initializes the specification.</summary>
    public ActiveAdminsSpec() =>
        Query.Where(user => user.Status == UserStatus.Active && user.Role == Role.Admin).OrderBy(user => user.RequestedAt);
}

/// <summary>The latest audit entry with one of the given actions for a target.</summary>
public sealed class LatestAuditEntrySpec : Specification<AuditEntry>, ISingleResultSpecification<AuditEntry>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="targetType">The target type, e.g. <c>User</c>.</param>
    /// <param name="targetId">The target id.</param>
    /// <param name="actions">The action codes to look for.</param>
    public LatestAuditEntrySpec(string targetType, string targetId, params string[] actions) =>
        Query.Where(entry => entry.TargetType == targetType && entry.TargetId == targetId && actions.Contains(entry.Action))
            .OrderByDescending(entry => entry.At)
            .Take(1);
}
