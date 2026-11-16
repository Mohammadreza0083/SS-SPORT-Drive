using System.ComponentModel;
using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SsSport.Drive.Api.MiniApp;
using SsSport.Drive.Api.Paging;
using SsSport.Drive.Api.Security;
using SsSport.Drive.Application.Archive;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Telegram.Notices;

namespace SsSport.Drive.Api.Admin;

/// <summary>
/// The console's archive migration (Epic 8): start a read-only scan of the Shared Drive and read its report (M-1), import
/// found folders as waiting archive players (M-2), and send the waiting archive players out and back as CSV (M-3).
/// </summary>
public static class ArchiveEndpoints
{
    /// <summary>The OpenAPI tag of the archive endpoints.</summary>
    public const string Tag = "Admin archive";

    /// <summary>How many rows the CSV export reads at a time.</summary>
    private const int CsvPageSize = PageRequest.MaxPageSize;

    /// <summary>A realistic scan.</summary>
    private const string ExampleScan = """
        { "id": "0199f6a5-3333-7b3d-9a52-3f1c8e7d2b10", "status": "Completed", "startedAt": "2026-11-16T09:00:00+00:00",
          "finishedAt": "2026-11-16T09:04:12+00:00", "failureReason": null, "folders": 41, "files": 214, "bytes": 107374182400,
          "inBackup": 196, "missingFromBackup": 18, "skipped": 7 }
        """;

    /// <summary>A realistic report row.</summary>
    private static readonly ArchiveFolderRowDto[] _exampleRows =
    [
        new()
        {
            Id = Guid.Parse("0199f6a5-4444-7b3d-9a52-3f1c8e7d2b10"),
            Path = "Men Volleyball/SS-SPORT Ivan Valeev／OH-OPP",
            FolderName = "SS-SPORT Ivan Valeev／OH-OPP",
            Category = "Men",
            SuggestedName = "Ivan Valeev",
            SuggestedPositions = ["OH", "OPP"],
            Videos = 8,
            Documents = 2,
            Scouts = 1,
            Files = 11,
            Bytes = 4_000_000_000,
            InBackup = 10,
            MissingFromBackup = 1,
            Skipped = 2,
            IsTruncated = false,
            State = "New",
            PlayerId = null,
        },
    ];

    /// <summary>Maps the archive endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAdminArchive(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/archive", SummaryAsync)
            .WithTags(Tag)
            .WithName("GetArchiveSummary")
            .RequireAdmin()
            .WithSummary("Read the archive page's header")
            .WithDescription(
                "Admins only. The newest scan whatever its status (`Running`, `Completed`, `Failed` with a reason), the newest completed scan (the report " +
                "the list shows) with its totals, and how many of its folders are `New`, `Linked` (already in the app before the scan) or `Imported`. " +
                "Both scans are null before the first one.")
            .Produces<ArchiveSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", $$"""{ "latestScan": {{ExampleScan}}, "report": {{ExampleScan}}, "new": 30, "linked": 9, "imported": 2 }"""));

        app.MapPost("/admin/archive/scans", StartAsync)
            .WithTags(Tag)
            .WithName("StartArchiveScan")
            .RequireAdmin()
            .WithSummary("Start a scan of the Shared Drive")
            .WithDescription(
                "Admins only. Queues a read-only scan: a background job walks `Men Volleyball` and `Women Volleyball`, lists every player folder and matches " +
                "each file's md5 against the R2 backup. Nothing is written to Drive or the backup. Audited as `archive.scan_started`. Answers 202 with " +
                "the running scan; read `GET /admin/archive` to follow it. 409 `archive.scan_running` while another scan runs (a scan stuck for over two " +
                "hours no longer counts).")
            .Accepts<object>("application/json")
            .Produces<ArchiveScanDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("202", ExampleScan.Replace("\"Completed\"", "\"Running\"", StringComparison.Ordinal), "{}"));

        app.MapGet("/admin/archive/folders", FoldersAsync)
            .WithTags(Tag)
            .WithName("ListArchiveFolders")
            .RequireAdmin()
            .WithSummary("List the report's player folders")
            .WithDescription(
                "Admins only. One page of the newest completed scan's player folders in path order: the category, the name and positions the folder name " +
                "suggests, the files by kind, how many the backup holds and how many it does not, the files left out, and the state (`New`, `Linked`, " +
                "`Imported`) with the player. Filter with `state` (new, linked, imported). Empty before the first completed scan. 400 for an unknown state.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithPagedResponse(_exampleRows)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPost("/admin/archive/import", ImportAsync)
            .WithTags(Tag)
            .WithName("ImportArchiveFolders")
            .RequireAdmin()
            .WithSummary("Import folders as waiting players")
            .WithDescription(
                "Admins only. Each named report folder becomes an archive player on the waiting list, without an analyst, with its category and the name " +
                "and positions its folder name suggests; its Drive folder is linked, so the normal sync lists the files. Nothing is published or " +
                "transcoded until an admin publishes. Audited as `archive.imported` per player. 1 to 100 distinct ids. Folders that cannot be imported are " +
                "refused one by one: `archive.folder_not_found`, `archive.folder_not_new` (imported or linked already), `drive_folder.taken` (linked since the " +
                "scan). 400 for an empty, repeated or oversized list.")
            .Accepts<ImportArchiveFoldersDto>("application/json")
            .Produces<ArchiveImportResultDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document(
                "200",
                """{ "imported": [ { "folderId": "0199f6a5-4444-7b3d-9a52-3f1c8e7d2b10", "playerId": "0199f6a6-5555-7b3d-9a52-3f1c8e7d2b10" } ], "refused": [] }""",
                """{ "folderIds": ["0199f6a5-4444-7b3d-9a52-3f1c8e7d2b10"] }"""));

        app.MapGet("/admin/archive/waiting.csv", ExportAsync)
            .WithTags(Tag)
            .WithName("ExportArchiveWaiting")
            .RequireAdmin()
            .WithSummary("Export the waiting archive players as CSV")
            .WithDescription(
                "Admins only. Every archive player still on the waiting list, oldest first, as a CSV attachment (`archive-waiting-yyyy-MM-dd.csv`, Tehran " +
                "date) streamed page by page: `Id, Folder, Category, Name, Nationality, Positions, Volleybox`. Positions are codes joined by `/`. A cell a " +
                "spreadsheet would run as a formula starts with an apostrophe (the import removes it). Not cached.")
            .Produces(StatusCodes.Status200OK, typeof(string), "text/csv")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document("200", null));

        app.MapPost("/admin/archive/waiting.csv", ImportCsvAsync)
            .WithTags(Tag)
            .WithName("ImportArchiveWaiting")
            .RequireAdmin()
            .WithSummary("Import the filled-in CSV")
            .WithDescription(
                "Admins only. The edited export comes back as JSON text (`csv`, at most 1 MB and 2000 rows; columns are found by their header names, the " +
                "`Id` column is required). Each row is checked with the player rules on its own: a bad row changes nothing and is reported with its row " +
                "number (the header is row 1) and code, e.g. `player.nationality_invalid`, `player.volleybox_taken`, `archive.csv_bad_id`, " +
                "`archive.csv_duplicate_row`, `archive.csv_player_not_waiting`. Good rows are saved; an empty cell leaves a detail as it is. Only waiting " +
                "archive players change, nothing is published, and photos never come by CSV. Audited as `archive.csv_imported`. 400 for an empty or " +
                "oversized file; 422 `archive.csv_invalid_header` or `archive.csv_too_many_rows` for the whole file.")
            .Accepts<ImportArchiveCsvDto>("application/json")
            .Produces<ArchiveCsvResultDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithMetadata(new RequestSizeLimitAttribute(4 * 1024 * 1024))
            .AddOpenApiOperationTransformer(MiniAppEndpoints.Document(
                "200",
                """{ "updated": 12, "unchanged": 3, "errors": [ { "row": 7, "code": "player.nationality_invalid", "message": "The nationality must be a two-letter country code from the country list." } ] }""",
                """{ "csv": "Id,Folder,Category,Name,Nationality,Positions,Volleybox\r\n0199f6a6-5555-7b3d-9a52-3f1c8e7d2b10,SS-SPORT Ivan Valeev／OH-OPP,Men,Ivan Valeev,RU,OH/OPP,https://volleybox.net/ivan-valeev-p70001\r\n" }"""));
        return app;
    }

    /// <summary>Reads the header.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the header.</returns>
    private static async Task<Ok<ArchiveSummaryDto>> SummaryAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetArchiveSummaryQuery(), cancellationToken));

    /// <summary>Starts a scan.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202 with the running scan.</returns>
    private static async Task<Accepted<ArchiveScanDto>> StartAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Accepted("/admin/archive", await sender.Send(new StartArchiveScanCommand(), cancellationToken));

    /// <summary>Lists the report's folders.</summary>
    /// <param name="paging">The page.</param>
    /// <param name="state">The state filter.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page.</returns>
    private static async Task<Ok<PagedResult<ArchiveFolderRowDto>>> FoldersAsync(
        [AsParameters] PageParameters paging,
        [FromQuery(Name = "state"), Description("new, linked or imported.")] string? state,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetArchiveFoldersQuery(paging.ToRequest(), state), cancellationToken));

    /// <summary>Imports folders.</summary>
    /// <param name="form">The folder ids.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with what was imported and refused.</returns>
    private static async Task<Ok<ArchiveImportResultDto>> ImportAsync(ImportArchiveFoldersDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ImportArchiveFoldersCommand(form.FolderIds ?? []), cancellationToken));

    /// <summary>Streams the waiting archive players as CSV, one page at a time.</summary>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="settings">The notification rules, for the Tehran date.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private static async Task ExportAsync(ISender sender, INotificationSettings settings, TimeProvider timeProvider, HttpResponse response, CancellationToken cancellationToken)
    {
        var page = await sender.Send(new GetArchiveCsvPageQuery(new PageRequest(1, CsvPageSize)), cancellationToken);
        var day = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), settings.Rules.TimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.ContentDisposition = $"attachment; filename=\"archive-waiting-{day}.csv\"";
        response.Headers.CacheControl = "no-store";
        await using var writer = new StreamWriter(response.Body, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync(ArchiveCsv.HeaderLine);
        for (var number = 1; ; number++)
        {
            foreach (var row in page.Items)
            {
                await writer.WriteLineAsync(ArchiveCsv.Line(row));
            }

            if (number >= page.TotalPages)
            {
                break;
            }

            page = await sender.Send(new GetArchiveCsvPageQuery(new PageRequest(number + 1, CsvPageSize)), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>Imports the filled-in CSV.</summary>
    /// <param name="form">The file's text.</param>
    /// <param name="sender">The MediatR sender.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with what changed and the refused rows.</returns>
    private static async Task<Ok<ArchiveCsvResultDto>> ImportCsvAsync(ImportArchiveCsvDto form, ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ImportArchiveCsvCommand(form.Csv ?? string.Empty), cancellationToken));
}

/// <summary>The body of <c>POST /admin/archive/import</c>.</summary>
public sealed record ImportArchiveFoldersDto
{
    /// <summary>Gets the report rows to import, 1 to 100.</summary>
    public IReadOnlyList<Guid>? FolderIds { get; init; }
}

/// <summary>The body of <c>POST /admin/archive/waiting.csv</c>.</summary>
public sealed record ImportArchiveCsvDto
{
    /// <summary>Gets the CSV file's text, at most 1 MB.</summary>
    public string? Csv { get; init; }
}
