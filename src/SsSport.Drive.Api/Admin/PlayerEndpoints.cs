using System.ComponentModel;
using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Application.Admin;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Telegram.Notices;

namespace SsSport.Drive.Api.Admin;

/// <summary>
/// The console's players (A-3 list and CSV, A-4 player page, A-5 waiting list): list, read, edit details, upload a photo,
/// publish, and manage the files (order, hero, visibility, retry).
/// </summary>
public static class PlayerEndpoints
{
    /// <summary>The OpenAPI tag of the players endpoints.</summary>
    public const string Tag = "Admin players";

    /// <summary>The OpenAPI tag of the waiting list endpoints.</summary>
    public const string WaitingTag = "Admin waiting list";

    /// <summary>The CSV's header row.</summary>
    private const string CsvHeader = "Name,Positions,Nationality,Videos,Documents,Viewers,Last viewed,Analyst,Status,Volleybox";

    /// <summary>How many rows the CSV export reads at a time.</summary>
    private const int CsvPageSize = PageRequest.MaxPageSize;

    /// <summary>The image types a photo may have.</summary>
    private static readonly string[] _photoTypes = ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    /// <summary>Two realistic list rows.</summary>
    private static readonly AdminPlayerRowDto[] _exampleRows =
    [
        new()
        {
            Id = Guid.Parse("0199f6a2-4c1e-7b3d-9a52-3f1c8e7d2b10"),
            Name = "Arash Tehrani",
            Positions = ["OPP", "OH"],
            NationalityCode = "IR",
            NationalityName = "Iran",
            VideoCount = 6,
            DocumentCount = 2,
            Viewers = 5,
            LastViewedAt = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
            AnalystName = "Sara N.",
            Status = "Live",
            HasPhoto = true,
            VolleyboxId = 48213,
            CreatedAt = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>A realistic file row.</summary>
    private static readonly AdminPlayerFileDto[] _exampleFiles =
    [
        new()
        {
            Id = Guid.Parse("0199f6a3-1111-7b3d-9a52-3f1c8e7d2b10"),
            Kind = "Video",
            Title = "Highlights 2026",
            FileName = "Highlights_2026.mp4",
            Tags = ["Highlight"],
            SortOrder = 1,
            Visibility = "Visible",
            State = "Ready",
            Source = "Drive",
            DurationSeconds = 84,
            IsHero = true,
        },
    ];

    /// <summary>A realistic waiting row.</summary>
    private static readonly WaitingPlayerRowDto[] _exampleWaiting =
    [
        new()
        {
            Id = Guid.Parse("0199f6a4-2222-7b3d-9a52-3f1c8e7d2b10"),
            Name = "Ali Hoshmand",
            FolderName = "SS-SPORT Ali Hoshmand",
            VideoCount = 8,
            DocumentCount = 1,
            Missing = ["Positions", "Volleybox"],
            WaitingSince = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    /// <summary>Maps the players endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAdminPlayers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/players", ListAsync)
            .WithTags(Tag)
            .WithName("ListAdminPlayers")
            .RequireAdmin()
            .WithSummary("List players")
            .WithDescription(
                "Admins only. One page of every player, newest first: positions as codes, how many videos and documents, how many different emails watched, " +
                "when the player was last watched, the analyst and the console status (`Live`, `Processing`, `Attention` = a failed file or a lost Drive folder, " +
                "`Waiting`). Filter with `q` (part of a name, or a Volleybox id), `position` (S, OH, OPP, MB, L, DS) and `status` (live, processing, attention, " +
                "waiting). 400 for an unknown filter or a bad page.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/players.csv", ExportAsync)
            .WithTags(Tag)
            .WithName("ExportAdminPlayers")
            .RequireAdmin()
            .WithSummary("Export players as CSV")
            .WithDescription(
                "Admins only. The same filters as the list, as a CSV attachment (`players-yyyy-MM-dd.csv`, Tehran date) streamed page by page, never held whole. " +
                "A cell a spreadsheet would run as a formula starts with an apostrophe. Not cached. 400 for an unknown filter.")
            .Produces(StatusCodes.Status200OK, typeof(string), "text/csv")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/players/{id:guid}", GetAsync)
            .WithTags(Tag)
            .WithName("GetAdminPlayer")
            .RequireAdmin()
            .WithSummary("Read a player")
            .WithDescription(
                "Admins only. The player page's header: facts, what is missing, the hero, how many emails have access, the average completion and the Drive " +
                "folders (at most 20, newest first). 404 for an unknown player.")
            .Produces<AdminPlayerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPatch("/admin/players/{id:guid}", UpdateAsync)
            .WithTags(Tag)
            .WithName("UpdateAdminPlayer")
            .RequireAdmin()
            .WithSummary("Edit a player's details")
            .WithDescription(
                "Admins only; also after Publish. The same fields and rules as the Mini App's edit: only the fields that are sent change. Answers the player. " +
                "400 for unreadable category or positions; 422 `player.name_invalid_characters`, `player.name_too_long`, `player.nationality_invalid`, " +
                "`player.duplicate_position`, `player.volleybox_url_invalid`, `player.volleybox_not_a_player`, `player.volleybox_taken`; 404 for an unknown player.")
            .Accepts<UpdatePlayerDto>("application/json")
            .Produces<AdminPlayerDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null, """{ "positions": ["OH", "OPP"], "volleyboxUrl": "https://volleybox.net/ali-hoshmand-p60214" }"""));

        app.MapPut("/admin/players/{id:guid}/photo", UploadPhotoAsync)
            .WithTags(Tag)
            .WithName("UploadAdminPlayerPhoto")
            .RequireAdmin()
            .WithSummary("Upload a player's photo")
            .WithDescription(
                "Admins only. The same as the Mini App's upload: the photo itself is the request body with its image content type (JPEG, PNG, WebP or HEIC, " +
                "at least 800 px on the short side, metadata removed). 400 for an empty body, 413 `player.photo_too_large` over 15 MB, 422 " +
                "`player.photo_too_small` or `player.photo_unreadable`, 404 for an unknown player.")
            .Accepts<byte[]>(_photoTypes[0], _photoTypes[1..])
            .Produces<PlayerPhotoDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithMetadata(new RequestSizeLimitAttribute(UploadPlayerPhotoCommand.MaxBytes + (1024 * 1024)))
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPost("/admin/players/{id:guid}/publish", PublishAsync)
            .WithTags(Tag)
            .WithName("PublishAdminPlayer")
            .RequireAdmin()
            .WithSummary("Publish a player")
            .WithDescription(
                "Admins only. Publishes a complete player: ingest starts (copy, transcode, QR code, card) and the owning analyst is told in the bot. Audited as " +
                "`player.published`. Publishing a published player answers the player again and changes nothing. 422 `player.incomplete` while a detail is missing; " +
                "404 for an unknown player.")
            .Produces<AdminPlayerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/players/{id:guid}/files", ListFilesAsync)
            .WithTags(Tag)
            .WithName("ListAdminPlayerFiles")
            .RequireAdmin()
            .WithSummary("List a player's files")
            .WithDescription(
                "Admins only. One page of the player's files in page order, hidden ones included: kind, tags, state, why it failed, where it came from, whether it " +
                "left its Drive folder and whether it is the hero. 404 for an unknown player.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithPagedResponse(_exampleFiles)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPatch("/admin/players/{id:guid}/files/{fileId:guid}", EditFileAsync)
            .WithTags(Tag)
            .WithName("EditAdminPlayerFile")
            .RequireAdmin()
            .WithSummary("Edit a player's file")
            .WithDescription(
                "Admins only. The same as the Mini App's edit-video sheet: `title`, `tags`, `isHero` and `hidden` (audited as `asset.hidden`, `asset.shown`). " +
                "Fields left out stay. 400 for a bad title or tag; 404 for an unknown player or a file of another player; 409 `concurrency.conflict`; " +
                "422 `asset.hero_not_video`, `asset.hero_hidden`.")
            .Accepts<EditPlayerFileDto>("application/json")
            .Produces<PlayerFileDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null, """{ "isHero": true }"""));

        app.MapPost("/admin/players/{id:guid}/files/{fileId:guid}/move", MoveAsync)
            .WithTags(Tag)
            .WithName("MoveAdminPlayerFile")
            .RequireAdmin()
            .WithSummary("Move a file to a place on the page")
            .WithDescription(
                "Admins only. `position` is the new place from 1 to the number of files; the files between shift by one and every file is renumbered 1..n. " +
                "Audited as `asset.moved`. 400 for a place outside the range; 404 for an unknown player or a file of another player.")
            .Accepts<MoveFileDto>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("204", null, """{ "position": 1 }"""));

        app.MapPost("/admin/files/{id:guid}/retry", RetryAsync)
            .WithTags(Tag)
            .WithName("RetryAdminFile")
            .RequireAdmin()
            .WithSummary("Retry a failed file")
            .WithDescription(
                "Admins only. Puts a failed file back in the queue and queues its copy again. Audited as `asset.retried`. 404 for an unknown file; 422 " +
                "`asset.invalid_state` for a file that did not fail.")
            .Accepts<object>("application/json")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("202", null));

        app.MapGet("/admin/waiting", WaitingAsync)
            .WithTags(WaitingTag)
            .WithName("ListWaitingPlayers")
            .RequireAdmin()
            .WithSummary("List the waiting players")
            .WithDescription(
                "Admins only. One page of the players that are not published, oldest first: the newest Drive folder, how many files, what is missing (`Name`, " +
                "`Nationality`, `Positions`, `Volleybox`, `Photo`) and since when they wait. Filter with `missing`. 400 for an unknown detail.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleWaiting)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapGet("/admin/waiting/summary", WaitingSummaryAsync)
            .WithTags(WaitingTag)
            .WithName("GetWaitingSummary")
            .RequireAdmin()
            .WithSummary("Count the waiting list's tiles")
            .WithDescription("Admins only. How many players wait, how many have no Volleybox link or no photo, and how many were published today (Tehran).")
            .Produces<WaitingSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", """{ "waiting": 5, "noVolleybox": 3, "noPhoto": 2, "publishedToday": 1 }"""));
        return app;
    }

    /// <summary>Lists players.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="q">The search.</param>
    /// <param name="position">The position code.</param>
    /// <param name="status">The console status.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<AdminPlayerRowDto>>> ListAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "q"), Description("Part of a name, or a Volleybox id.")] string? q,
        [FromQuery(Name = "position"), Description("S, OH, OPP, MB, L or DS.")] string? position,
        [FromQuery(Name = "status"), Description("live, processing, attention or waiting.")] string? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAdminPlayersQuery(paging.ToRequest(), q, position, status), cancellationToken));

    /// <summary>Streams the players as CSV, one page at a time.</summary>
    /// <param name="q">The search.</param>
    /// <param name="position">The position code.</param>
    /// <param name="status">The console status.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="settings">The notification rules, for the Tehran date.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private static async Task ExportAsync(
        [FromQuery(Name = "q"), Description("Part of a name, or a Volleybox id.")] string? q,
        [FromQuery(Name = "position"), Description("S, OH, OPP, MB, L or DS.")] string? position,
        [FromQuery(Name = "status"), Description("live, processing, attention or waiting.")] string? status,
        ISender sender,
        INotificationSettings settings,
        TimeProvider timeProvider,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var first = await sender.Send(new GetAdminPlayersQuery(new PageRequest(1, CsvPageSize), q, position, status), cancellationToken);
        var day = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), settings.Rules.TimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition = $"attachment; filename=\"players-{day}.csv\"";
        response.Headers.CacheControl = "no-store";
        await using var writer = new StreamWriter(response.Body, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync(CsvHeader);
        var page = first;
        for (var number = 1; ; number++)
        {
            foreach (var row in page.Items)
            {
                await writer.WriteLineAsync(CsvLine(row));
            }

            if (number >= page.TotalPages)
            {
                break;
            }

            page = await sender.Send(new GetAdminPlayersQuery(new PageRequest(number + 1, CsvPageSize), q, position, status), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>Writes one player as a CSV line.</summary>
    /// <param name="row">The player.</param>
    /// <returns>The line, without its line ending.</returns>
    private static string CsvLine(AdminPlayerRowDto row) => string.Join(
        ',',
        Cell(row.Name),
        Cell(string.Join('/', row.Positions)),
        Cell(row.NationalityName),
        Cell(row.VideoCount.ToString(CultureInfo.InvariantCulture)),
        Cell(row.DocumentCount.ToString(CultureInfo.InvariantCulture)),
        Cell(row.Viewers.ToString(CultureInfo.InvariantCulture)),
        Cell(row.LastViewedAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
        Cell(row.AnalystName),
        Cell(row.Status),
        Cell(row.VolleyboxId?.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Writes one CSV cell: a cell a spreadsheet would run as a formula (starting with <c>= + - @</c>, a tab or a return) gets an
    /// apostrophe first, and a cell with a comma, a quote or a line break is quoted.
    /// </summary>
    /// <param name="value">The text, or <see langword="null"/> for an empty cell.</param>
    /// <returns>The cell.</returns>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    /// <summary>Reads a player.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the player.</returns>
    private static async Task<Ok<AdminPlayerDto>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAdminPlayerQuery(id), cancellationToken));

    /// <summary>Edits a player's details.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="form">The fields to change.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the player.</returns>
    private static async Task<Ok<AdminPlayerDto>> UpdateAsync(Guid id, UpdatePlayerDto form, ISender sender, CancellationToken cancellationToken)
    {
        _ = await sender.Send(form.ToCommand(id), cancellationToken);
        return TypedResults.Ok(await sender.Send(new GetAdminPlayerQuery(id), cancellationToken));
    }

    /// <summary>Uploads a photo.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="request">The request, whose body is the photo.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the photo's facts, or 413 for a photo over the limit.</returns>
    private static async Task<Results<Ok<PlayerPhotoDto>, ProblemHttpResult>> UploadPhotoAsync(Guid id, HttpRequest request, ISender sender, CancellationToken cancellationToken)
    {
        var content = await MiniAppPhotoEndpoints.ReadCappedAsync(request.Body, UploadPlayerPhotoCommand.MaxBytes, cancellationToken);
        if (content is null)
        {
            return TypedResults.Problem(
                title: "The photo is larger than 15 MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                extensions: new Dictionary<string, object?> { ["code"] = "player.photo_too_large" });
        }

        return TypedResults.Ok(await sender.Send(new UploadPlayerPhotoCommand(id, content), cancellationToken));
    }

    /// <summary>Publishes a player.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the player.</returns>
    private static async Task<Ok<AdminPlayerDto>> PublishAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new PublishPlayerCommand(id), cancellationToken);
        return TypedResults.Ok(await sender.Send(new GetAdminPlayerQuery(id), cancellationToken));
    }

    /// <summary>Lists a player's files.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="paging">The page.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<AdminPlayerFileDto>>> ListFilesAsync(Guid id, [AsParameters] PageParameters paging, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetAdminPlayerFilesQuery(id, paging.ToRequest()), cancellationToken));

    /// <summary>Edits a file.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="fileId">The file id.</param>
    /// <param name="form">The fields to change.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the file.</returns>
    private static async Task<Ok<PlayerFileDto>> EditFileAsync(Guid id, Guid fileId, EditPlayerFileDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(form.ToCommand(id, fileId), cancellationToken));

    /// <summary>Moves a file.</summary>
    /// <param name="id">The player id.</param>
    /// <param name="fileId">The file id.</param>
    /// <param name="form">The new place.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204.</returns>
    private static async Task<NoContent> MoveAsync(Guid id, Guid fileId, MoveFileDto form, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new MoveFileCommand(id, fileId, form.Position), cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Retries a failed file.</summary>
    /// <param name="id">The file id.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202.</returns>
    private static async Task<Accepted> RetryAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new RetryFileCommand(id), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    /// <summary>Lists the waiting players.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="missing">The detail filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<WaitingPlayerRowDto>>> WaitingAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "missing"), Description("name, nationality, positions, volleybox or photo.")] string? missing,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetWaitingPlayersQuery(paging.ToRequest(), missing), cancellationToken));

    /// <summary>Counts the waiting list's tiles.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the tiles.</returns>
    private static async Task<Ok<WaitingSummaryDto>> WaitingSummaryAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetWaitingSummaryQuery(), cancellationToken));
}
