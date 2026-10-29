using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Application.MiniApp;

namespace SsSport.Drive.Api.MiniApp;

/// <summary>
/// The player photo step of the Mini App (T-4b): upload as the raw request body, read back as WebP. Only the analyst
/// who owns the player, or an admin, may do either.
/// </summary>
public static class MiniAppPhotoEndpoints
{
    /// <summary>The route of a player's photo.</summary>
    public const string Route = "/miniapp/players/{id:guid}/photo";

    /// <summary>The upload content types the reference lists (the real format is detected from the bytes).</summary>
    private static readonly string[] _uploadTypes = ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    /// <summary>A realistic upload result for the API reference.</summary>
    private const string ExamplePhoto =
        """
        {
          "photoKey": "players/0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e/photo-1761750000000.webp",
          "width": 1200,
          "height": 1600,
          "isComplete": true,
          "missingDetails": []
        }
        """;

    /// <summary>Maps <c>PUT</c> and <c>GET</c> on <see cref="Route"/>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPlayerPhoto(this IEndpointRouteBuilder app)
    {
        app.MapPut(Route, UploadAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("UploadMiniAppPlayerPhoto")
            .WithSummary("Upload a player's photo")
            .WithDescription(
                "Send the photo itself as the request body (no multipart), with its image content type. JPEG, PNG, WebP and HEIC are accepted; " +
                "the real format is detected from the bytes. The shorter side must be at least 800 px. " +
                "The photo is made upright, every bit of metadata (GPS included) is removed, the longer side is capped at 2000 px and it is stored as WebP, " +
                "next to the original, under new versioned keys. Only the analyst who owns the player, or an admin, may upload. " +
                "Errors: 400 for an empty body, 413 `player.photo_too_large` over 15 MB, 422 `player.photo_too_small` or `player.photo_unreadable`.")
            .Accepts<byte[]>(_uploadTypes[0], _uploadTypes[1..])
            .Produces<PlayerPhotoDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithMetadata(new RequestSizeLimitAttribute(UploadPlayerPhotoCommand.MaxBytes + (1024 * 1024)))
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", ExamplePhoto));

        app.MapGet(Route, ReadAsync)
            .WithTags(MiniAppEndpoints.Tag)
            .WithName("GetMiniAppPlayerPhoto")
            .WithSummary("Read a player's photo")
            .WithDescription(
                "Streams the player's current photo as WebP; storage itself is never exposed. " +
                "Only the analyst who owns the player, or an admin, may read it. 404 when the player has no photo yet.")
            .Produces(StatusCodes.Status200OK, typeof(byte[]), "image/webp")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        return app;
    }

    /// <summary>Reads the body (refusing more than 15 MB) and uploads it.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="request">The HTTP request.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the photo, or a 413 ProblemDetails; other errors come from the pipeline.</returns>
    private static async Task<Results<Ok<PlayerPhotoDto>, ProblemHttpResult>> UploadAsync(Guid id, HttpRequest request, ISender sender, CancellationToken cancellationToken)
    {
        var content = await ReadCappedAsync(request.Body, UploadPlayerPhotoCommand.MaxBytes, cancellationToken);
        if (content is null)
        {
            return TypedResults.Problem(
                title: "The photo is larger than 15 MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                extensions: new Dictionary<string, object?> { ["code"] = "player.photo_too_large" });
        }

        return TypedResults.Ok(await sender.Send(new UploadPlayerPhotoCommand(id, content), cancellationToken));
    }

    /// <summary>Streams the photo, cacheable by the viewer's own browser only.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="response">The HTTP response, for the cache header.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The image.</returns>
    private static async Task<FileStreamHttpResult> ReadAsync(Guid id, ISender sender, HttpResponse response, CancellationToken cancellationToken)
    {
        var photo = await sender.Send(new GetPlayerPhotoQuery(id), cancellationToken);
        response.Headers.CacheControl = "private, max-age=300";
        return TypedResults.Stream(photo.Content, photo.ContentType);
    }

    /// <summary>Reads a request body into memory, stopping as soon as it is larger than allowed.</summary>
    /// <param name="body">The body.</param>
    /// <param name="maxBytes">The most bytes allowed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes, or <see langword="null"/> when the body is larger than <paramref name="maxBytes"/>.</returns>
    private static async Task<byte[]?> ReadCappedAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
