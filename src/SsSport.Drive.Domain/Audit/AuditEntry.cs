using System.Text.Json;
using System.Text.RegularExpressions;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Audit;

/// <summary>
/// One sensitive action (role change, link created or revoked, file hidden, deletion, restore): who did what to which
/// target, and when. Entries are append-only and refer to people by id, never by email.
/// </summary>
public sealed partial class AuditEntry : AggregateRoot<Guid>
{
    /// <summary>The longest data payload.</summary>
    private const int MaxDataLength = 4000;

    /// <summary>The longest target id.</summary>
    private const int MaxTargetIdLength = 100;

    /// <summary>Initializes an entry; use <see cref="Record"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="actorUserId">The acting user, or <see langword="null"/> for the system.</param>
    /// <param name="action">The action code.</param>
    /// <param name="targetType">The target's type.</param>
    /// <param name="targetId">The target's id.</param>
    /// <param name="data">Extra details as a JSON object, or <see langword="null"/>.</param>
    /// <param name="at">When it happened (UTC).</param>
    private AuditEntry(Guid id, Guid? actorUserId, string action, string targetType, string targetId, string? data, DateTimeOffset at)
        : base(id)
    {
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Data = data;
        At = at;
    }

    /// <summary>Gets the acting user, or <see langword="null"/> when the system acted (sync, jobs).</summary>
    public Guid? ActorUserId { get; }

    /// <summary>Gets the action code, e.g. <c>user.role_changed</c>.</summary>
    public string Action { get; }

    /// <summary>Gets the target's type, e.g. <c>User</c> or <c>ShareLink</c>.</summary>
    public string TargetType { get; }

    /// <summary>Gets the target's id.</summary>
    public string TargetId { get; }

    /// <summary>Gets extra details as a JSON object, or <see langword="null"/>.</summary>
    public string? Data { get; }

    /// <summary>Gets when it happened (UTC).</summary>
    public DateTimeOffset At { get; }

    /// <summary>Records an action.</summary>
    /// <param name="actorUserId">The acting user, or <see langword="null"/> for the system.</param>
    /// <param name="action">A dot-separated lower-case code, e.g. <c>user.role_changed</c>.</param>
    /// <param name="targetType">The target's type: letters only, e.g. <c>User</c>.</param>
    /// <param name="targetId">The target's id.</param>
    /// <param name="data">Extra details as a JSON object without email addresses, or <see langword="null"/>.</param>
    /// <param name="at">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="DomainException">
    /// <c>audit.action_invalid</c>, <c>audit.target_invalid</c>, <c>audit.data_invalid</c> or <c>audit.data_contains_email</c>.
    /// </exception>
    public static AuditEntry Record(Guid? actorUserId, string action, string targetType, string targetId, string? data, DateTimeOffset at)
    {
        var code = (action ?? string.Empty).Trim();
        if (!ActionPattern().IsMatch(code))
        {
            throw new DomainException("audit.action_invalid", "An action is a dot-separated lower-case code, e.g. user.role_changed.");
        }

        var type = (targetType ?? string.Empty).Trim();
        var id = (targetId ?? string.Empty).Trim();
        if (!TargetTypePattern().IsMatch(type) || id.Length is 0 or > MaxTargetIdLength)
        {
            throw new DomainException("audit.target_invalid", "An audit entry needs a target type (letters only) and a target id.");
        }

        return new AuditEntry(Guid.CreateVersion7(at), actorUserId, code, type, id, ValidData(data), at);
    }

    /// <summary>Validates the data payload: a JSON object, at most 4000 characters, no email addresses.</summary>
    /// <param name="data">The raw data.</param>
    /// <returns>The trimmed data, or <see langword="null"/>.</returns>
    private static string? ValidData(string? data)
    {
        var text = data?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > MaxDataLength || !IsJsonObject(text))
        {
            throw new DomainException("audit.data_invalid", $"Audit data must be a JSON object of at most {MaxDataLength} characters.");
        }

        if (EmailPattern().IsMatch(text))
        {
            throw new DomainException("audit.data_contains_email", "Audit data must refer to people by id, not by email.");
        }

        return text;
    }

    /// <summary>Checks that text parses as a JSON object.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> for a JSON object.</returns>
    private static bool IsJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Two or more lower-case segments joined by dots.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex ActionPattern();

    /// <summary>Letters only, up to 50.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex("^[A-Za-z]{1,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetTypePattern();

    /// <summary>Anything shaped like an email address.</summary>
    /// <returns>The regex.</returns>
    [GeneratedRegex(@"[^\s@""]+@[^\s@""]+\.[^\s@""]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
