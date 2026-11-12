using System.Text.Json.Nodes;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SsSport.Drive.Application.Links;

namespace SsSport.Drive.Api.Links;

/// <summary>
/// The email gate of <c>/r/{code}</c> (L-2), all public: ask for a 6-digit code, type it in for a trusted device and a
/// session, open again from a trusted device, and read who a session belongs to. The web app calls these on the
/// browser's behalf and passes the cookie values in headers; the API never reads cookies.
/// </summary>
public static class ViewerGateEndpoints
{
    /// <summary>The OpenAPI tag.</summary>
    public const string Tag = "Viewer gate";

    /// <summary>The header carrying the device token.</summary>
    public const string DeviceHeader = "X-Viewer-Device";

    /// <summary>The header carrying the session token.</summary>
    public const string SessionHeader = "X-Viewer-Session";

    /// <summary>The OpenAPI security scheme of the device token.</summary>
    public const string DeviceSchemeName = "ViewerDevice";

    /// <summary>The OpenAPI security scheme of the session token.</summary>
    public const string SessionSchemeName = "ViewerSession";

    /// <summary>A realistic answer after a code was asked for.</summary>
    private const string ExampleSent =
        """
        { "email": "m***@lube.it", "resendAfterSeconds": 60, "expiresInSeconds": 600 }
        """;

    /// <summary>A realistic answer after the right code.</summary>
    private const string ExampleAccess =
        """
        {
          "deviceToken": "pX2bq9ZcR1t0mKvL8wYhN3sD5fG7jA4eU6iO0rT2yQw",
          "deviceExpiresAt": "2027-02-04T09:00:00+00:00",
          "sessionToken": "Vb7nM2qX9kL4tR8wZ1cY5hJ3gF6dS0aP2eU4iO7rT9y",
          "sessionExpiresAt": "2026-11-07T09:00:00+00:00",
          "awaitingApproval": false
        }
        """;

    /// <summary>A realistic answer for a trusted device: no new device token.</summary>
    private const string ExampleOpened =
        """
        {
          "deviceToken": null,
          "deviceExpiresAt": null,
          "sessionToken": "Vb7nM2qX9kL4tR8wZ1cY5hJ3gF6dS0aP2eU4iO7rT9y",
          "sessionExpiresAt": "2026-11-07T09:00:00+00:00",
          "awaitingApproval": false
        }
        """;

    /// <summary>A realistic session.</summary>
    private const string ExampleSession =
        """
        { "email": "marco@lube.it", "isForward": false, "expiresAt": "2026-11-07T09:00:00+00:00" }
        """;

    /// <summary>Maps the gate endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapViewerGate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/links/{code}/email-code", RequestCodeAsync)
            .WithTags(Tag)
            .WithName("RequestEmailCode")
            .WithSummary("Email a 6-digit code to open a personal link")
            .WithDescription(
                "Public. The email must be one of the recipient's, or, when the link allows forwarding, any email (`AnyEmail`) or one on the " +
                "recipient's domain (`SameDomain`); anything else, and a forward an admin blocked, is 422 `share_link.email_not_allowed`. " +
                "A malformed email is 422 `email.invalid`. " +
                "The code is drawn and mailed in the background (Resend, then Brevo), valid 10 minutes and 5 tries; asking again replaces it. " +
                "A new code waits 60 seconds and an email gets at most 5 per link per hour: 429 `email_code.too_many` with `Retry-After`. " +
                "A link that does not open is the same 404 `share_link.unavailable` as `GET /links/{code}`.")
            .Accepts<EmailCodeFormDto>("application/json")
            .Produces<EmailCodeSentDto>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(Document("202", ExampleSent, """{ "email": "marco@lube.it" }"""));

        app.MapPost("/links/{code}/verify", VerifyAsync)
            .WithTags(Tag)
            .WithName("VerifyEmailCode")
            .WithSummary("Type the code: trust this device and start a session")
            .WithDescription(
                "Public. Checks the newest code mailed to the email for this link. The right code trusts the device for 90 days (the browser keeps " +
                $"the device token it sends in `{DeviceHeader}`, or gets a new one) and starts a one-day session. The web app puts both tokens into " +
                "`HttpOnly` cookies; they are stored only as keyed hashes. A wrong code is 422 `email_code.invalid` with `attemptsLeft`; the fifth wrong " +
                "code, and any code after it, is 422 `email_code.exhausted`; an old or replaced code is 422 `email_code.expired`; a used code is 422 " +
                "`email_code.used`. The code must be 6 digits (400). " +
                "Forwards (L-3): an email that is not the recipient's is recorded and every admin is alerted in the bot with Keep / Block. Until an " +
                "admin keeps it the answer is 200 with `awaitingApproval: true`, the device token and no session (`sessionToken` and " +
                "`sessionExpiresAt` are null): the gate shows \"waiting for approval\". A blocked forward is 422 `share_link.email_not_allowed`.")
            .Accepts<VerifyEmailCodeFormDto>("application/json")
            .Produces<ViewerAccessDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(Document("200", ExampleAccess, """{ "email": "marco@lube.it", "code": "042917" }""", DeviceSchemeName, optional: true));

        app.MapPost("/links/{code}/session", OpenAsync)
            .WithTags(Tag)
            .WithName("OpenWithDevice")
            .WithSummary("Open a link again from a trusted device")
            .WithDescription(
                "Public. A device trusted for this link (within 90 days of its code) gets a new one-day session without a code; no new device token is " +
                "issued. A link with `verifyOnNewDevice` off opens for anyone, as the recipient's first email. Otherwise 401 " +
                "`viewer.verification_required`: show the email gate. A device whose email is a forward no admin has kept yet is 403 " +
                "`share_link.forward_pending` (show \"waiting for approval\"); once kept it opens, and once blocked it is 401.")
            .Produces<ViewerAccessDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(Document("200", ExampleOpened, null, DeviceSchemeName, optional: true));

        app.MapGet("/links/{code}/session", GetSessionAsync)
            .WithTags(Tag)
            .WithName("GetViewerSession")
            .WithSummary("Read who a viewer session belongs to")
            .WithDescription(
                "Public; the session token is the proof. Answers the viewer's email (laid over the videos), whether it is a forward, and when the " +
                "session ends. A missing, unknown or ended session, or one for another link, is 401 `viewer.verification_required`.")
            .Produces<ViewerSessionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(Document("200", ExampleSession, null, SessionSchemeName, optional: false));

        return app;
    }

    /// <summary>Asks for a code.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="form">The email form.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202 with what the gate shows next.</returns>
    private static async Task<Accepted<EmailCodeSentDto>> RequestCodeAsync(string code, EmailCodeFormDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Accepted((string?)null, await sender.Send(new RequestEmailCodeCommand(code, form.Email), cancellationToken));

    /// <summary>Checks a typed code.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="form">The code form.</param>
    /// <param name="deviceToken">The browser's device token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the tokens, or 422 with why not.</returns>
    private static async Task<Results<Ok<ViewerAccessDto>, ProblemHttpResult>> VerifyAsync(
        string code,
        VerifyEmailCodeFormDto form,
        [FromHeader(Name = DeviceHeader)] string? deviceToken,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new VerifyEmailCodeCommand(code, form.Email, form.Code, deviceToken), cancellationToken);
        if (result.Access is { } access)
        {
            return TypedResults.Ok(access);
        }

        var extensions = new Dictionary<string, object?> { ["code"] = result.FailureCode, ["attemptsLeft"] = result.AttemptsLeft };
        return TypedResults.Problem(title: result.FailureMessage, statusCode: StatusCodes.Status422UnprocessableEntity, extensions: extensions);
    }

    /// <summary>Opens a link from a trusted device.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="deviceToken">The browser's device token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the session token.</returns>
    private static async Task<Ok<ViewerAccessDto>> OpenAsync(string code, [FromHeader(Name = DeviceHeader)] string? deviceToken, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new OpenWithDeviceCommand(code, deviceToken), cancellationToken));

    /// <summary>Reads a session.</summary>
    /// <param name="code">The link code.</param>
    /// <param name="sessionToken">The session token, if any.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with who it belongs to.</returns>
    private static async Task<Ok<ViewerSessionDto>> GetSessionAsync(string code, [FromHeader(Name = SessionHeader)] string? sessionToken, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetViewerSessionQuery(code, sessionToken), cancellationToken));

    /// <summary>Adds the examples and the gate's token requirement to an operation.</summary>
    /// <param name="status">The success status.</param>
    /// <param name="responseExample">The success example.</param>
    /// <param name="requestExample">The request example, if any.</param>
    /// <param name="scheme">The token scheme, if any.</param>
    /// <param name="optional">Whether the operation also works without the token.</param>
    /// <returns>The transformer.</returns>
    private static Func<OpenApiOperation, OpenApiOperationTransformerContext, CancellationToken, Task> Document(
        string status, string responseExample, string? requestExample, string? scheme = null, bool optional = false) =>
        (operation, context, _) =>
        {
            var requirements = new List<OpenApiSecurityRequirement>();
            if (scheme is not null)
            {
                requirements.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [] });
            }

            if (optional || scheme is null)
            {
                requirements.Add(new OpenApiSecurityRequirement());
            }

            operation.Security = requirements;
            if (operation.Responses?.TryGetValue(status, out var success) == true && success.Content?.TryGetValue("application/json", out var media) == true)
            {
                media.Example = JsonNode.Parse(responseExample);
            }

            if (requestExample is not null && operation.RequestBody?.Content?.TryGetValue("application/json", out var body) == true)
            {
                body.Example = JsonNode.Parse(requestExample);
            }

            return Task.CompletedTask;
        };
}
