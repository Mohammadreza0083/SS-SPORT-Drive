using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.Links;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Public;

namespace SsSport.Drive.Api.Public;

/// <summary>
/// Asking for access to a locked player (P-3, P-7, P-8): a code for the visitor's email, the request itself, and the
/// signed-in viewer's reads and sign-out. Limits: 60 s and 5 an hour for codes per email; 5 requests a day per email and
/// 20 an hour per player; a small per-address budget on the two writes.
/// </summary>
public static class PublicAccessEndpoints
{
    /// <summary>The Scalar tag of these endpoints.</summary>
    public const string Tag = "Access requests";

    /// <summary>The header a signed-in viewer's session token travels in.</summary>
    public const string AccountHeader = "X-Viewer-Account";

    /// <summary>The OpenAPI name of the signed-in viewer scheme.</summary>
    public const string AccountSchemeName = "ViewerAccount";

    private const string ExampleSent =
        """
        { "email": "j***@aurora-volley.example", "resendAfterSeconds": 60, "expiresInSeconds": 600 }
        """;

    private const string ExampleRequestSent =
        """
        { "status": "pending" }
        """;

    private const string ExampleViewer =
        """
        { "email": "scout@aurora-volley.example", "expiresAt": "2026-11-13T09:00:00+00:00" }
        """;

    /// <summary>Maps the access request endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPublicAccess(this IEndpointRouteBuilder app)
    {
        app.MapPost($"{PublicPlayerEndpoints.RoutePrefix}/access-code", RequestCodeAsync)
            .WithTags(Tag)
            .WithName("RequestAccessCode")
            .WithSummary("Email a 6-digit code before asking for access")
            .WithDescription(
                "Public. Mails a 6-digit code to any ordinary email (no company domain needed), valid 10 minutes and 5 tries; asking again replaces it. " +
                "A new code waits 60 seconds and an email gets at most 5 an hour: 429 `email_code.too_many` with `Retry-After`. A missing email is 400; " +
                "a malformed one is 422 `email.invalid`. The code is drawn and mailed in the background and is never in the answer.")
            .Accepts<AccessCodeFormDto>("application/json")
            .Produces<EmailCodeSentDto>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("202", ExampleSent, """{ "email": "j.demir@aurora-volley.example" }"""));

        app.MapPost($"{PublicPlayerEndpoints.RoutePrefix}/access-requests", SubmitAsync)
            .WithTags(Tag)
            .WithName("SubmitAccessRequest")
            .WithSummary("Ask for access to a locked player")
            .WithDescription(
                "Public. A **visitor** sends `playerSlug`, `about` (1 to 1000 characters), the `email`, the 6-digit `code` mailed to it and the Cloudflare " +
                "Turnstile `turnstileToken`. A **signed-in viewer** (`X-Viewer-Account`) sends only `playerSlug` and `about`: the session's email is used " +
                "and any email or code in the body is ignored. Checks run in this order: input (400); player published (404); a signed-in session that is not " +
                "live (401 `viewer.sign_in_required`); Turnstile (422 `turnstile.failed`, before the code so bots cannot burn a visitor's tries); the code " +
                "(422 `email_code.invalid` with `attemptsLeft`, `email_code.exhausted`, `email_code.expired` or `email_code.used`); a repeat while one is " +
                "pending (201, nothing new stored); the limits, 5 requests per email in 24 hours and 20 per player in an hour (429 `access_request.too_many` " +
                "with `Retry-After`). A code is used once. The request goes to the admins' inbox (P-4).")
            .Accepts<AccessRequestFormDto>("application/json")
            .Produces<AccessRequestSentDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document(
                "201",
                ExampleRequestSent,
                """{ "playerSlug": "arash-tehrani", "about": "Aurora Volley, head scout. Looking for an opposite for 2027.", "email": "j.demir@aurora-volley.example", "code": "042917", "turnstileToken": "0.abc..." }""",
                AccountSchemeName,
                optional: true));

        app.MapGet($"{PublicPlayerEndpoints.RoutePrefix}/viewer", GetViewerAsync)
            .WithTags(Tag)
            .WithName("GetPublicViewer")
            .WithSummary("Read who is signed in")
            .WithDescription(
                "Public; the session token is the proof. Answers the signed-in viewer's email and when the session ends (one day). A missing, unknown, " +
                "ended or signed-out session is 401 `viewer.sign_in_required`.")
            .Produces<PublicViewerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("200", ExampleViewer, null, AccountSchemeName, optional: false));

        app.MapPost($"{PublicPlayerEndpoints.RoutePrefix}/viewer/sign-out", SignOutAsync)
            .WithTags(Tag)
            .WithName("SignOutPublicViewer")
            .WithSummary("Sign the viewer out")
            .WithDescription("Public. Ends the signed-in session at once. Always 204, also without a token or with an unknown one, so signing out can be repeated.")
            .Produces(StatusCodes.Status204NoContent)
            .AddOpenApiOperationTransformer(ViewerGateEndpoints.Document("204", null, null, AccountSchemeName, optional: true));

        return app;
    }

    /// <summary>Asks for a code.</summary>
    /// <param name="form">The email.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202 with what the dialog shows next.</returns>
    private static async Task<Accepted<EmailCodeSentDto>> RequestCodeAsync(AccessCodeFormDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Accepted((string?)null, await sender.Send(new RequestAccessCodeCommand(form.Email), cancellationToken));

    /// <summary>Asks for access.</summary>
    /// <param name="form">The request.</param>
    /// <param name="accountToken">The signed-in viewer's token, if any.</param>
    /// <param name="context">The request, for the visitor's address.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201, or 422 when the code did not verify.</returns>
    private static async Task<Results<Created<AccessRequestSentDto>, ProblemHttpResult>> SubmitAsync(
        AccessRequestFormDto form,
        [FromHeader(Name = AccountHeader)] string? accountToken,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new SubmitAccessRequestCommand(
            form.PlayerSlug,
            form.About,
            form.Email,
            form.Code,
            form.TurnstileToken,
            accountToken,
            context.Connection.RemoteIpAddress?.ToString());
        var outcome = await sender.Send(command, cancellationToken);
        if (outcome.Sent)
        {
            return TypedResults.Created((string?)null, new AccessRequestSentDto());
        }

        var extensions = new Dictionary<string, object?> { ["code"] = outcome.FailureCode, ["attemptsLeft"] = outcome.AttemptsLeft };
        return TypedResults.Problem(title: outcome.FailureMessage, statusCode: StatusCodes.Status422UnprocessableEntity, extensions: extensions);
    }

    /// <summary>Reads who is signed in.</summary>
    /// <param name="accountToken">The signed-in viewer's token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the viewer.</returns>
    private static async Task<Ok<PublicViewerDto>> GetViewerAsync([FromHeader(Name = AccountHeader)] string? accountToken, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetPublicViewerQuery(accountToken), cancellationToken));

    /// <summary>Signs the viewer out.</summary>
    /// <param name="accountToken">The signed-in viewer's token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204.</returns>
    private static async Task<NoContent> SignOutAsync([FromHeader(Name = AccountHeader)] string? accountToken, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new SignOutViewerCommand(accountToken), cancellationToken);
        return TypedResults.NoContent();
    }
}
