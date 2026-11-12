using System.Text.Json;
using Ardalis.Specification;
using Microsoft.Extensions.Logging;
using Riok.Mapperly.Abstractions;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Emails;

namespace SsSport.Drive.Application.Emails;

/// <summary>The template keys of the queued emails (L-4).</summary>
public static class EmailTemplates
{
    /// <summary>The gate code of a personal link (L-2).</summary>
    public const string GateCode = "gate.code";

    /// <summary>The "your link is open" email of a kept forward (L-3b).</summary>
    public const string ForwardKept = "forward.kept";

    /// <summary>The admin console's sign-in link (A-1).</summary>
    public const string AdminSignIn = "auth.admin_sign_in";

    /// <summary>The viewer sign-in link (P-5).</summary>
    public const string ViewerSignIn = "auth.viewer_sign_in";
}

/// <summary>The data of a queued email, read back by its composer.</summary>
public static class EmailData
{
    /// <summary>Reads a GUID property of the email's data JSON.</summary>
    /// <param name="email">The queued email.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The GUID, or <see langword="null"/> when the data or the property is missing or malformed.</returns>
    public static Guid? GuidOf(OutgoingEmail email, string name)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (email.Data is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(email.Data);
        return document.RootElement.TryGetProperty(name, out var value) && value.TryGetGuid(out var id) ? id : null;
    }
}

/// <summary>
/// The email job (L-4): composes a queued email from its template, sends it through the providers of its kind and
/// records the outcome.
/// </summary>
/// <param name="emails">The queue.</param>
/// <param name="unitOfWork">Saves the outcome.</param>
/// <param name="composers">Every template's composer.</param>
/// <param name="dispatcher">Sends through the providers.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class SendQueuedEmail(
    IRepository<OutgoingEmail, Guid> emails,
    IUnitOfWork unitOfWork,
    IEnumerable<IEmailComposer> composers,
    IEmailDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<SendQueuedEmail> logger) : IBackgroundEventHandler<EmailQueued>
{
    /// <summary>
    /// Composes the queued email from its template, sends it and records the outcome. A failure is recorded and thrown so
    /// Hangfire retries the job; after the last allowed attempt the email is marked failed and nothing is thrown.
    /// </summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="context">Who caused it (nobody: emails are sent by the system).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the email was sent, skipped or given up.</returns>
    public async Task HandleAsync(EmailQueued domainEvent, EventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var email = await emails.GetByIdAsync(domainEvent.EmailId, cancellationToken);
        if (email is null || email.Status != EmailStatus.Queued)
        {
            return;
        }

        var composer = composers.FirstOrDefault(item => item.Template == email.Template);
        if (composer is null)
        {
            email.Skip(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogNoComposer(logger, email.Id, email.Template);
            return;
        }

        try
        {
            var message = await composer.ComposeAsync(email, cancellationToken);
            if (message is null)
            {
                email.Skip(timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            var provider = await dispatcher.SendAsync(email.Kind, message, cancellationToken);
            email.RecordSent(provider, timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the exception type is stored and logged: its message could hold an address.
            email.RecordFailure(exception.GetType().Name, timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogFailed(logger, email.Id, email.Template, email.Attempts, exception.GetType().Name);
            if (email.Status == EmailStatus.Queued)
            {
                throw;
            }
        }
    }

    /// <summary>Logs an email whose template has no composer: a bug.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="EmailId">The email.</param>
    /// <param name="Template">The template key.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "No composer for email template {Template}; email {EmailId} was skipped")]
    private static partial void LogNoComposer(ILogger logger, Guid EmailId, string Template);

    /// <summary>Logs a failed attempt.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="EmailId">The email.</param>
    /// <param name="Template">The template key.</param>
    /// <param name="Attempts">The attempts so far.</param>
    /// <param name="ErrorType">The failure's type.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailId} ({Template}) failed on attempt {Attempts} with {ErrorType}")]
    private static partial void LogFailed(ILogger logger, Guid EmailId, string Template, int Attempts, string ErrorType);
}

/// <summary>One row of the admin email queue.</summary>
public sealed record OutgoingEmailDto
{
    /// <summary>Gets the email id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the kind by name (<c>Code</c> or <c>General</c>): which providers send it.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the template key.</summary>
    public required string Template { get; init; }

    /// <summary>Gets the masked recipient, such as <c>m***@lube.it</c>.</summary>
    public required string Recipient { get; init; }

    /// <summary>Gets the status by name (<c>Queued</c>, <c>Sent</c>, <c>Failed</c> or <c>Skipped</c>).</summary>
    public required string Status { get; init; }

    /// <summary>Gets how many attempts were made.</summary>
    public required int Attempts { get; init; }

    /// <summary>Gets the provider that accepted it, or <see langword="null"/>.</summary>
    public string? Provider { get; init; }

    /// <summary>Gets the type of the last failure, or <see langword="null"/>.</summary>
    public string? LastError { get; init; }

    /// <summary>Gets when it was queued.</summary>
    public required DateTimeOffset QueuedAt { get; init; }

    /// <summary>Gets when the last attempt was made, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastAttemptAt { get; init; }

    /// <summary>Gets when a provider accepted it, or <see langword="null"/>.</summary>
    public DateTimeOffset? SentAt { get; init; }
}

/// <summary>Maps queued emails to DTOs at compile time (Mapperly); the recipient goes out masked.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target, EnumMappingStrategy = EnumMappingStrategy.ByName)]
public static partial class OutgoingEmailMapper
{
    /// <summary>Maps a queued email.</summary>
    /// <param name="email">The email.</param>
    /// <returns>The DTO, with the recipient masked.</returns>
    [MapProperty(nameof(OutgoingEmail.To), nameof(OutgoingEmailDto.Recipient), Use = nameof(MaskedRecipient))]
    public static partial OutgoingEmailDto ToDto(this OutgoingEmail email);

    /// <summary>Masks an address (<c>EmailAddress.ToString()</c> is masked).</summary>
    /// <param name="address">The address.</param>
    /// <returns>The masked address.</returns>
    private static string MaskedRecipient(Domain.Primitives.EmailAddress address) => address.ToString();
}

/// <summary>Queued emails, optionally of one status. The list orders itself by the cursor key.</summary>
public sealed class OutgoingEmailsSpec : Specification<OutgoingEmail>
{
    /// <summary>Filters by status when one is given.</summary>
    /// <param name="status">The status, or <see langword="null"/> for all.</param>
    public OutgoingEmailsSpec(EmailStatus? status)
    {
        if (status is { } value)
        {
            Query.Where(email => email.Status == value);
        }
    }
}

/// <summary>Reads the email queue, newest first (<c>GET /admin/emails</c>), for admins only.</summary>
/// <param name="Status">Only emails of this status, or <see langword="null"/> for all.</param>
/// <param name="Paging">The cursor page.</param>
public sealed record ListOutgoingEmailsQuery(EmailStatus? Status, CursorRequest Paging) : ICursorQuery<OutgoingEmailDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Handles <see cref="ListOutgoingEmailsQuery"/>.</summary>
/// <param name="reader">The paged reader.</param>
public sealed class ListOutgoingEmailsQueryHandler(IPagedReader reader) : IQueryHandler<ListOutgoingEmailsQuery, CursorResult<OutgoingEmailDto>>
{
    /// <summary>Reads one page of the queue, newest first.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page and the cursor for the next one.</returns>
    public Task<CursorResult<OutgoingEmailDto>> Handle(ListOutgoingEmailsQuery request, CancellationToken cancellationToken) =>
        reader.CursorPageAsync(
            new OutgoingEmailsSpec(request.Status),
            email => email.QueuedAt,
            CursorDirection.Descending,
            request.Paging,
            email => email.ToDto(),
            cancellationToken);
}
