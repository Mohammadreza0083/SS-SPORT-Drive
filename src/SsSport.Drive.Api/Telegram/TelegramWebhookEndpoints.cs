using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using SsSport.Drive.Application.Telegram;
using SsSport.Drive.Infrastructure.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace SsSport.Drive.Api.Telegram;

/// <summary>
/// <c>POST /telegram/webhook</c>: where Telegram delivers the bot's updates. Only calls with the configured secret
/// token are accepted. Authenticated calls are always answered 200 (processing errors are logged), so Telegram never
/// re-delivers in a loop; re-deliveries are recognised by update id.
/// </summary>
public static partial class TelegramWebhookEndpoints
{
    /// <summary>The route Telegram posts to.</summary>
    public const string Route = "/telegram/webhook";

    /// <summary>The header Telegram sends the secret token in.</summary>
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>The OpenAPI security scheme describing the secret header.</summary>
    public const string SecuritySchemeName = "TelegramWebhookSecret";

    /// <summary>The log category.</summary>
    private const string LogCategory = "SsSport.Drive.Api.Telegram.Webhook";

    /// <summary>A realistic update for the API reference.</summary>
    private const string ExampleUpdate =
        """
        {
          "update_id": 734522101,
          "message": {
            "message_id": 17,
            "date": 1761379200,
            "chat": { "id": 123456789, "type": "private" },
            "from": { "id": 123456789, "is_bot": false, "first_name": "Reza", "username": "reza_analyst" },
            "text": "/start"
          }
        }
        """;

    /// <summary>Maps the webhook under the "Telegram" tag, documented with its secret-header security scheme.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapTelegramWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, ReceiveAsync)
            .WithTags("Telegram")
            .WithName("ReceiveTelegramUpdate")
            .WithSummary("Receive a Telegram update")
            .WithDescription(
                "Called by Telegram for every update of the bot (messages, button presses, the bot being blocked). " +
                $"The `{SecretHeader}` header must carry the secret registered with `setWebhook`; otherwise the call is rejected with 401 " +
                "(also when no secret is configured). Authenticated updates are always answered 200, even if processing fails, so Telegram " +
                "does not retry; an update delivered twice is processed once. A body that is not a Telegram update is answered 400.")
            .Accepts<JsonObject>("application/json")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SecuritySchemeName, context.Document)] = [] }];
                if (operation.RequestBody?.Content is { } content && content.TryGetValue("application/json", out var media))
                {
                    media.Example = JsonNode.Parse(ExampleUpdate);
                }

                return Task.CompletedTask;
            });

        return app;
    }

    /// <summary>Checks the secret, reads the update and hands it to the application.</summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="options">The Telegram settings.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="loggerFactory">Creates the webhook logger.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200, or a 400/401 ProblemDetails.</returns>
    private static async Task<Results<Ok, ProblemHttpResult>> ReceiveAsync(
        HttpRequest request,
        IOptions<TelegramOptions> options,
        ISender sender,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        if (!SecretMatches(options.Value.WebhookSecret, request.Headers[SecretHeader]))
        {
            LogRejected(logger);
            return TypedResults.Problem(title: "Invalid webhook secret.", statusCode: StatusCodes.Status401Unauthorized);
        }

        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(request.Body, JsonBotAPI.Options, cancellationToken);
        }
        catch (JsonException)
        {
            update = null;
        }

        if (update is null)
        {
            return TypedResults.Problem(title: "The body is not a Telegram update.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await sender.Send(new HandleTelegramUpdateCommand(update.Id, update.Type.ToString(), SenderId(update)), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Answer 200 anyway: a non-2xx makes Telegram re-deliver the same update again and again.
            LogFailed(logger, exception, update.Id);
        }

        return TypedResults.Ok();
    }

    /// <summary>
    /// Compares the received secret with the configured one in constant time (hashing first, so the length is not
    /// leaked either). An empty configured secret rejects everything.
    /// </summary>
    /// <param name="configured">The configured secret.</param>
    /// <param name="received">The header value.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    private static bool SecretMatches(string configured, string? received) =>
        configured.Length > 0
        && received is { Length: > 0 }
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(received)));

    /// <summary>Finds the Telegram user who caused the update, if any.</summary>
    /// <param name="update">The update.</param>
    /// <returns>The user id, or <see langword="null"/>.</returns>
    private static long? SenderId(Update update) =>
        update.Message?.From?.Id ?? update.CallbackQuery?.From.Id ?? update.MyChatMember?.From.Id;

    /// <summary>Logs a call without the right secret.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a Telegram webhook call with a missing or wrong secret token")]
    private static partial void LogRejected(ILogger logger);

    /// <summary>Logs an update whose processing failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="UpdateId">The update id.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Processing Telegram update {UpdateId} failed; answered 200 so Telegram does not retry")]
    private static partial void LogFailed(ILogger logger, Exception exception, long UpdateId);
}
