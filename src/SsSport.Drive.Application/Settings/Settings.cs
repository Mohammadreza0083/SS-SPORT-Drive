using System.Globalization;
using FluentValidation;
using Riok.Mapperly.Abstractions;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Settings;

namespace SsSport.Drive.Application.Settings;

/// <summary>Reads and stores the global settings row (A-10); a fresh database answers the configured first values.</summary>
public interface IAppSettingsStore
{
    /// <summary>Reads the settings without tracking them; the configured defaults when nothing was saved yet.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings.</returns>
    Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the settings synchronously, for the few callers that cannot await (the notification rules).</summary>
    /// <returns>The settings.</returns>
    AppSettings GetSnapshot();

    /// <summary>Reads the tracked settings row to change; adds the configured defaults as a new row when none exists.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tracked settings.</returns>
    Task<AppSettings> GetForUpdateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Moves the recurring digest job to a new time.</summary>
public interface IDigestSchedule
{
    /// <summary>Schedules the daily digest at a Tehran wall-clock time.</summary>
    /// <param name="digestAt">The time of day in Tehran.</param>
    void Apply(TimeOnly digestAt);
}

/// <summary>Drops what the host cached of the settings, so a change is seen at once.</summary>
public interface IAppSettingsCache
{
    /// <summary>Forgets the cached settings.</summary>
    void Invalidate();
}

/// <summary>The settings as the console shows and edits them.</summary>
public sealed record SettingsDto
{
    /// <summary>Gets the email shown on every player page.</summary>
    public required string ContactEmail { get; init; }

    /// <summary>Gets the daily digest time, <c>HH:mm</c> in Tehran.</summary>
    public required string DigestAt { get; init; }

    /// <summary>Gets when quiet hours start, <c>HH:mm</c> in Tehran.</summary>
    public required string QuietHoursStart { get; init; }

    /// <summary>Gets when quiet hours end, <c>HH:mm</c> in Tehran.</summary>
    public required string QuietHoursEnd { get; init; }

    /// <summary>Gets the expiry the New share link modal preselects, in days.</summary>
    public required int DefaultLinkExpiryDays { get; init; }

    /// <summary>Gets the days a share link may last.</summary>
    public required IReadOnlyList<int> LinkExpiryChoices { get; init; }

    /// <summary>Gets the time zone of the times above.</summary>
    public string TimeZone { get; init; } = SettingsRules.TimeZoneId;

    /// <summary>Gets when the settings last changed (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Rules and formats shared by the settings requests.</summary>
public static class SettingsRules
{
    /// <summary>The time zone the settings' times are written in.</summary>
    public const string TimeZoneId = "Asia/Tehran";

    /// <summary>The audit action of a change.</summary>
    public const string ChangedAction = "settings.changed";

    /// <summary>The audit target type.</summary>
    public const string AuditTarget = nameof(AppSettings);

    /// <summary>The format of the times.</summary>
    public const string TimeFormat = "HH:mm";

    /// <summary>Parses a time written as <c>HH:mm</c>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="time">The time when it parses.</param>
    /// <returns><see langword="true"/> when the text is a valid time.</returns>
    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>Writes a time as <c>HH:mm</c>.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text.</returns>
    public static string FormatTime(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);
}

/// <summary>Maps the settings to its DTO at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
public static partial class SettingsMapper
{
    /// <summary>Maps the settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The DTO.</returns>
    [MapProperty(nameof(AppSettings.ContactEmail), nameof(SettingsDto.ContactEmail), Use = nameof(EmailText))]
    [MapProperty(nameof(AppSettings.DigestAt), nameof(SettingsDto.DigestAt), Use = nameof(TimeText))]
    [MapProperty(nameof(AppSettings.QuietHoursStart), nameof(SettingsDto.QuietHoursStart), Use = nameof(TimeText))]
    [MapProperty(nameof(AppSettings.QuietHoursEnd), nameof(SettingsDto.QuietHoursEnd), Use = nameof(TimeText))]
    [MapValue(nameof(SettingsDto.LinkExpiryChoices), Use = nameof(ExpiryChoices))]
    public static partial SettingsDto ToDto(this AppSettings settings);

    /// <summary>Writes the contact email in full: it is public on every player page.</summary>
    /// <param name="email">The address.</param>
    /// <returns>The address.</returns>
    private static string EmailText(EmailAddress email) => email.Value;

    /// <summary>Gives the days a share link may last.</summary>
    /// <returns>The choices.</returns>
    private static IReadOnlyList<int> ExpiryChoices() => AppSettings.LinkExpiryChoices;

    /// <summary>Writes a time as <c>HH:mm</c>.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text.</returns>
    private static string TimeText(TimeOnly time) => SettingsRules.FormatTime(time);
}

/// <summary>Reads the settings (<c>GET /admin/settings</c>), for admins only.</summary>
public sealed record GetSettingsQuery : IQuery<SettingsDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetSettingsQuery"/>; it carries no input.</summary>
public sealed class GetSettingsQueryValidator : AbstractValidator<GetSettingsQuery>;

/// <summary>Handles <see cref="GetSettingsQuery"/>.</summary>
/// <param name="store">The settings store.</param>
public sealed class GetSettingsQueryHandler(IAppSettingsStore store) : IQueryHandler<GetSettingsQuery, SettingsDto>
{
    /// <summary>Reads the settings.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings.</returns>
    public async Task<SettingsDto> Handle(GetSettingsQuery request, CancellationToken cancellationToken) =>
        (await store.GetAsync(cancellationToken)).ToDto();
}

/// <summary>The body of <c>PUT /admin/settings</c>.</summary>
public sealed record UpdateSettingsDto
{
    /// <summary>Gets the email shown on every player page.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Gets the daily digest time, <c>HH:mm</c> in Tehran.</summary>
    public string? DigestAt { get; init; }

    /// <summary>Gets when quiet hours start, <c>HH:mm</c> in Tehran.</summary>
    public string? QuietHoursStart { get; init; }

    /// <summary>Gets when quiet hours end, <c>HH:mm</c> in Tehran.</summary>
    public string? QuietHoursEnd { get; init; }

    /// <summary>Gets the default link expiry: 7, 30 or 90 days.</summary>
    public int? DefaultLinkExpiryDays { get; init; }

    /// <summary>Builds the command.</summary>
    /// <returns>The command.</returns>
    public UpdateSettingsCommand ToCommand() => new(ContactEmail, DigestAt, QuietHoursStart, QuietHoursEnd, DefaultLinkExpiryDays);
}

/// <summary>Changes the settings (<c>PUT /admin/settings</c>), for admins only.</summary>
/// <param name="ContactEmail">The contact email.</param>
/// <param name="DigestAt">The digest time, <c>HH:mm</c>.</param>
/// <param name="QuietHoursStart">The quiet hours start, <c>HH:mm</c>.</param>
/// <param name="QuietHoursEnd">The quiet hours end, <c>HH:mm</c>.</param>
/// <param name="DefaultLinkExpiryDays">The default link expiry in days.</param>
public sealed record UpdateSettingsCommand(string? ContactEmail, string? DigestAt, string? QuietHoursStart, string? QuietHoursEnd, int? DefaultLinkExpiryDays)
    : ICommand<SettingsDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="UpdateSettingsCommand"/> on the server (decisions §1b rule 3); malformed input is a 400 per field.</summary>
public sealed class UpdateSettingsCommandValidator : AbstractValidator<UpdateSettingsCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpdateSettingsCommandValidator()
    {
        RuleFor(command => command.ContactEmail)
            .Must(text => !string.IsNullOrWhiteSpace(text) && text.Length <= 254 && IsEmail(text))
            .WithMessage("Use a valid email address.")
            .OverridePropertyName("contactEmail");
        RuleFor(command => command.DigestAt).Must(IsTime).WithMessage("Use a time as HH:mm.").OverridePropertyName("digestAt");
        RuleFor(command => command.QuietHoursStart).Must(IsTime).WithMessage("Use a time as HH:mm.").OverridePropertyName("quietHoursStart");
        RuleFor(command => command.QuietHoursEnd).Must(IsTime).WithMessage("Use a time as HH:mm.").OverridePropertyName("quietHoursEnd");
        RuleFor(command => command.DefaultLinkExpiryDays)
            .Must(ShareLinkRules.IsExpiryChoice)
            .WithMessage(ShareLinkRules.ExpiryMessage)
            .OverridePropertyName("defaultLinkExpiryDays");
    }

    /// <summary>Checks that a text is an email address the domain accepts.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is valid.</returns>
    private static bool IsEmail(string text)
    {
        try
        {
            _ = EmailAddress.Create(text);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    /// <summary>Checks that a text is a whole-minute time as <c>HH:mm</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is valid.</returns>
    private static bool IsTime(string? text) => SettingsRules.TryParseTime(text, out _);
}

/// <summary>Handles <see cref="UpdateSettingsCommand"/>: stores the values and audits the names of the changed fields.</summary>
/// <param name="store">The settings store.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class UpdateSettingsCommandHandler(
    IAppSettingsStore store,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<UpdateSettingsCommand, SettingsDto>
{
    /// <summary>Changes the settings.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The settings after the change.</returns>
    public async Task<SettingsDto> Handle(UpdateSettingsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = await store.GetForUpdateAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var actor = ShareLinkRules.ActorId(currentUser);
        SettingsRules.TryParseTime(request.DigestAt, out var digestAt);
        SettingsRules.TryParseTime(request.QuietHoursStart, out var quietStart);
        SettingsRules.TryParseTime(request.QuietHoursEnd, out var quietEnd);
        var changed = settings.Update(EmailAddress.Create(request.ContactEmail), digestAt, quietStart, quietEnd, request.DefaultLinkExpiryDays!.Value, actor, now);
        if (changed.Count > 0)
        {
            var fields = string.Join(",", changed.Select(field => $"\"{field}\""));
            audit.Add(AuditEntry.Record(actor, SettingsRules.ChangedAction, SettingsRules.AuditTarget, settings.Id.ToString(), $$"""{"fields":[{{fields}}]}""", now));
        }

        return settings.ToDto();
    }
}

/// <summary>After the settings were saved: drops the cached copy and moves the digest job when its time changed.</summary>
/// <param name="store">The settings store.</param>
/// <param name="cache">The settings cache.</param>
/// <param name="schedule">The digest schedule.</param>
public sealed class ApplySettingsChange(IAppSettingsStore store, IAppSettingsCache cache, IDigestSchedule schedule) : IDomainEventHandler<AppSettingsChanged>
{
    /// <summary>Applies the change.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the change is applied.</returns>
    public async Task HandleAsync(AppSettingsChanged domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cache.Invalidate();
        if (domainEvent.ChangedFields.Contains("digestAt"))
        {
            schedule.Apply((await store.GetAsync(cancellationToken)).DigestAt);
        }
    }
}
