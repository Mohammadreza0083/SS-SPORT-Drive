using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using SsSport.Drive.Api.Authentication;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Application.Accounts;
using SsSport.Drive.Domain.Accounts;

namespace SsSport.Drive.Api.Accounts;

/// <summary>
/// Email magic-link sign-in (A-1 admin console, P-5 viewers; decisions §7a). Asking for a link always answers 202, so
/// nobody can probe who is an admin. The link works once, for 15 minutes, and becomes a session token that the web app keeps
/// in a <c>__Host-</c> cookie and sends in a header (the API never reads cookies). Every POST must be JSON, which is the
/// CSRF check: a cross-site form cannot send it.
/// </summary>
public static class SignInEndpoints
{
    /// <summary>The OpenAPI tag.</summary>
    public const string Tag = "Sign-in";

    /// <summary>The header that carries a viewer account's session token.</summary>
    public const string ViewerHeaderName = "X-Viewer-Account";

    /// <summary>An example session answer.</summary>
    private const string ExampleSession =
        """
        {
          "sessionToken": "k3Yb0w1sJ2m9QeZxv7oXq5T8nC4hA6uRdL0pFiGyE1s",
          "expiresAt": "2026-11-19T09:00:00+00:00",
          "email": "owner@ss-sport.co",
          "audience": "Admin"
        }
        """;

    /// <summary>An example of who is signed in.</summary>
    private const string ExampleMe =
        """
        {
          "email": "owner@ss-sport.co",
          "displayName": "Owner",
          "audience": "Admin",
          "expiresAt": "2026-11-19T09:00:00+00:00"
        }
        """;

    /// <summary>Maps the sign-in endpoints for admins (<c>/auth/admin/*</c>) and viewers (<c>/auth/viewer/*</c>).</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSignIn(this IEndpointRouteBuilder app)
    {
        MapAudience(app, SignInAudience.Admin, "admin", AdminSessionAuthenticationHandler.HeaderName, "console");
        MapAudience(app, SignInAudience.Viewer, "viewer", ViewerHeaderName, "public site");
        return app;
    }

    /// <summary>Maps the four endpoints of one audience.</summary>
    /// <param name="app">The route builder.</param>
    /// <param name="audience">Who signs in.</param>
    /// <param name="segment">The route segment.</param>
    /// <param name="header">The session header.</param>
    /// <param name="place">Where the person signs in, for the docs.</param>
    private static void MapAudience(IEndpointRouteBuilder app, SignInAudience audience, string segment, string header, string place)
    {
        var name = audience.ToString();
        var group = app.MapGroup($"/auth/{segment}").WithTags(Tag).AddEndpointFilter(RequireJsonAsync);

        group.MapPost("/link", (SignInLinkFormDto form, ISender sender, CancellationToken cancellationToken) =>
                RequestLinkAsync(audience, form, sender, cancellationToken))
            .WithName($"Request{name}SignInLink")
            .WithSummary($"Ask for a {segment} sign-in link")
            .WithDescription(
                $"Public. Emails a one-time sign-in link for the {place}. It always answers 202 with no body, whether or not the address may sign in, so nobody " +
                "can probe for accounts; only an active admin's address gets an admin link. The link works once, for 15 minutes, and at most 5 links per " +
                "address per hour are sent. 400 for an address that is not an email; 415 for a body that is not JSON; 429 over the rate limit.")
            .Accepts<SignInLinkFormDto>("application/json")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("202", null, """{ "email": "owner@ss-sport.co" }""", signedIn: false));

        group.MapPost("/session", (SignInSessionFormDto form, ISender sender, CancellationToken cancellationToken) =>
                RedeemAsync(audience, form, sender, cancellationToken))
            .WithName($"Start{name}Session")
            .WithSummary($"Use a {segment} sign-in link")
            .WithDescription(
                $"Public. Turns the token of an emailed link into a session ({(audience == SignInAudience.Admin ? "30 days" : "7 days")}). The web app keeps the " +
                $"`sessionToken` in a `__Host-` cookie and sends it in the `{header}` header. 401 `sign_in.invalid` for a token that is unknown, used, " +
                "expired or for the other audience. 400 for a missing token; 415 for a body that is not JSON.")
            .Accepts<SignInSessionFormDto>("application/json")
            .Produces<SignInSessionDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleSession, """{ "token": "q8Wc2n0dJ5xTm1RzK7aVhE3yB6uPfLsG9oXiA4tNeUw" }""", signedIn: false));

        group.MapGet("/me", (HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
                MeAsync(audience, request.Headers[header], sender, cancellationToken))
            .WithName($"Get{name}SignedIn")
            .WithSummary($"Read the signed-in {segment}")
            .WithDescription($"Reads who the `{header}` header is signed in as. 401 `sign_in.required` without a live session of this audience.")
            .Produces<SignedInDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExampleMe, signedIn: false));

        group.MapPost("/sign-out", (HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
                SignOutAsync(audience, request.Headers[header], sender, cancellationToken))
            .WithName($"Sign{name}Out")
            .WithSummary($"Sign the {segment} out")
            .WithDescription($"Ends the session in the `{header}` header. Always 204, also without a session.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("204", null, signedIn: false));
    }

    /// <summary>Refuses a POST that is not JSON (415): a cross-site form cannot send JSON, so this is the CSRF check.</summary>
    /// <param name="context">The invocation.</param>
    /// <param name="next">The next filter.</param>
    /// <returns>The answer.</returns>
    private static async ValueTask<object?> RequireJsonAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsPost(request.Method) && !request.HasJsonContentType())
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Send the body as application/json.");
        }

        return await next(context);
    }

    /// <summary>Asks for a link.</summary>
    /// <param name="audience">Who signs in.</param>
    /// <param name="form">The email.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202.</returns>
    private static async Task<Accepted> RequestLinkAsync(SignInAudience audience, SignInLinkFormDto form, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new RequestSignInLinkCommand(audience, form.Email), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    /// <summary>Uses a link.</summary>
    /// <param name="audience">Who signs in.</param>
    /// <param name="form">The token.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the session.</returns>
    private static async Task<Ok<SignInSessionDto>> RedeemAsync(SignInAudience audience, SignInSessionFormDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new RedeemSignInLinkCommand(audience, form.Token), cancellationToken));

    /// <summary>Reads who is signed in.</summary>
    /// <param name="audience">Who must be signed in.</param>
    /// <param name="session">The session header.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with who is signed in.</returns>
    private static async Task<Ok<SignedInDto>> MeAsync(SignInAudience audience, StringValues session, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetSignedInQuery(audience, session.ToString()), cancellationToken));

    /// <summary>Signs out.</summary>
    /// <param name="audience">Who signs out.</param>
    /// <param name="session">The session header.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204.</returns>
    private static async Task<NoContent> SignOutAsync(SignInAudience audience, StringValues session, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new SignOutCommand(audience, session.ToString()), cancellationToken);
        return TypedResults.NoContent();
    }
}
