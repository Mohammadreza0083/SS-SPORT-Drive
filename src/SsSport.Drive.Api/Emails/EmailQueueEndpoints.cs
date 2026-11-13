using System.ComponentModel;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Domain.Emails;

namespace SsSport.Drive.Api.Emails;

/// <summary>The email queue for admins (L-4): every email, newest first, with its outcome and no full addresses.</summary>
public static class EmailQueueEndpoints
{
    /// <summary>The OpenAPI tag.</summary>
    public const string Tag = "Email queue";

    /// <summary>Two realistic rows of the queue.</summary>
    private static readonly OutgoingEmailDto[] _exampleRows =
    [
        new()
        {
            Id = Guid.Parse("019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c"),
            Kind = "Code",
            Template = "gate.code",
            Recipient = "m***@lube.it",
            Status = "Sent",
            Attempts = 1,
            Provider = "Resend",
            QueuedAt = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero),
            LastAttemptAt = new DateTimeOffset(2026, 11, 12, 9, 0, 1, TimeSpan.Zero),
            SentAt = new DateTimeOffset(2026, 11, 12, 9, 0, 1, TimeSpan.Zero),
        },
        new()
        {
            Id = Guid.Parse("019a3e1b-4a29-7e0f-93b2-7c8d9e0f1a2b"),
            Kind = "General",
            Template = "forward.kept",
            Recipient = "s***@other.com",
            Status = "Queued",
            Attempts = 1,
            LastError = "EmailTransportException",
            QueuedAt = new DateTimeOffset(2026, 11, 12, 8, 55, 0, TimeSpan.Zero),
            LastAttemptAt = new DateTimeOffset(2026, 11, 12, 8, 55, 2, TimeSpan.Zero),
        },
    ];

    /// <summary>Maps the email queue endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapEmailQueue(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/emails", ListAsync)
            .WithTags(Tag)
            .WithName("ListOutgoingEmails")
            .RequireAdmin()
            .WithSummary("List the email queue")
            .WithDescription(
                "Admins only. One cursor page of queued emails, newest first: the kind (`Code` goes Resend then Brevo, `General` goes through Google SMTP), " +
                "the template, the masked recipient, the status (`Queued`, `Sent`, `Failed`, `Skipped`), the attempts, the provider that accepted it and the " +
                "type of the last failure. Full addresses, codes and tokens are never shown. Filter with `status`. 400 for an unknown status or a bad page.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithCursorResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));
        return app;
    }

    /// <summary>Lists the queue.</summary>
    /// <param name="paging">The cursor page.</param>
    /// <param name="status">The status filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Results<Ok<CursorResult<OutgoingEmailDto>>, ValidationProblem>> ListAsync(
        [AsParameters] CursorParameters paging,
        [FromQuery(Name = "status"), Description("Queued, Sent, Failed or Skipped (any case); all when left out.")] string? status,
        ISender sender,
        CancellationToken cancellationToken)
    {
        EmailStatus? parsed = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (status.Any(char.IsDigit) || !Enum.TryParse<EmailStatus>(status, ignoreCase: true, out var value) || !Enum.IsDefined(value))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["status"] = ["Use Queued, Sent, Failed or Skipped."],
                });
            }

            parsed = value;
        }

        return TypedResults.Ok(await sender.Send(new ListOutgoingEmailsQuery(parsed, paging.ToRequest()), cancellationToken));
    }
}
