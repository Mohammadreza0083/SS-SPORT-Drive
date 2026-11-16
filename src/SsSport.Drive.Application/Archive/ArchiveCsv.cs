using System.Text;
using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Authorization;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.MiniApp;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Archive;

/// <summary>One waiting archive player as a CSV row (M-3).</summary>
public sealed record ArchiveCsvRowDto
{
    /// <summary>Gets the player id; the import finds the player by it.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Gets the newest linked Drive folder's name, for the team's eyes only (the import ignores it).</summary>
    public string? Folder { get; init; }

    /// <summary>Gets <c>Men</c> or <c>Women</c>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Gets the name, or <see langword="null"/>.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the country code, or <see langword="null"/>.</summary>
    public string? NationalityCode { get; init; }

    /// <summary>Gets the position codes, primary first.</summary>
    public IReadOnlyList<string> Positions { get; init; } = [];

    /// <summary>Gets the Volleybox page, or <see langword="null"/>.</summary>
    public string? VolleyboxUrl { get; init; }
}

/// <summary>One row the import refused.</summary>
public sealed record ArchiveCsvErrorDto
{
    /// <summary>Gets the row number in the file; the header is row 1.</summary>
    public int Row { get; init; }

    /// <summary>Gets the stable code, e.g. <c>player.nationality_invalid</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets what is wrong, in words.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>What a CSV import did.</summary>
public sealed record ArchiveCsvResultDto
{
    /// <summary>Gets how many players changed.</summary>
    public int Updated { get; init; }

    /// <summary>Gets how many rows matched their player already.</summary>
    public int Unchanged { get; init; }

    /// <summary>Gets the refused rows, in file order.</summary>
    public IReadOnlyList<ArchiveCsvErrorDto> Errors { get; init; } = [];
}

/// <summary>
/// The archive CSV (M-3): <c>Id, Folder, Category, Name, Nationality, Positions, Volleybox</c>, RFC 4180, UTF-8, CRLF. A cell a
/// spreadsheet would run as a formula is written with a leading apostrophe and read back without it.
/// </summary>
public static class ArchiveCsv
{
    /// <summary>The column names, in the order the export writes them.</summary>
    public static readonly IReadOnlyList<string> Columns = ["Id", "Folder", "Category", "Name", "Nationality", "Positions", "Volleybox"];

    /// <summary>Gets the header line.</summary>
    public static string HeaderLine { get; } = string.Join(',', Columns);

    /// <summary>Writes one player as a CSV line.</summary>
    /// <param name="row">The player.</param>
    /// <returns>The line, without its line ending.</returns>
    public static string Line(ArchiveCsvRowDto row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join(
            ',',
            Cell(row.PlayerId.ToString()),
            Cell(row.Folder),
            Cell(row.Category),
            Cell(row.Name),
            Cell(row.NationalityCode),
            Cell(string.Join('/', row.Positions)),
            Cell(row.VolleyboxUrl));
    }

    /// <summary>
    /// Writes one cell: a formula-looking cell (starting with <c>= + - @</c>, a tab or a return) gets an apostrophe first, and a
    /// cell with a comma, a quote or a line break is quoted.
    /// </summary>
    /// <param name="value">The text, or <see langword="null"/> for an empty cell.</param>
    /// <returns>The cell.</returns>
    public static string Cell(string? value)
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

    /// <summary>Removes the apostrophe the export put before a formula-looking cell; other apostrophes stay.</summary>
    /// <param name="cell">The cell as read.</param>
    /// <returns>The cell's value.</returns>
    public static string Unguard(string cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return cell.Length > 1 && cell[0] == '\'' && cell[1] is '=' or '+' or '-' or '@' or '\t' or '\r' ? cell[1..] : cell;
    }

    /// <summary>Reads RFC 4180 text into rows of cells: quoted cells may hold commas, doubled quotes and line breaks; blank lines are skipped.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when cell.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    row.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow(rows, row, cell);
                    row = [];
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }

        EndRow(rows, row, cell);
        return rows;
    }

    /// <summary>Closes a row; a row with one empty cell is a blank line and is dropped.</summary>
    /// <param name="rows">The rows so far.</param>
    /// <param name="row">The row.</param>
    /// <param name="cell">The last cell.</param>
    private static void EndRow(List<IReadOnlyList<string>> rows, List<string> row, StringBuilder cell)
    {
        row.Add(cell.ToString());
        cell.Clear();
        if (row.Count > 1 || row[0].Length > 0)
        {
            rows.Add(row);
        }
    }
}

/// <summary>The waiting archive players, oldest first (paged by <see cref="IPagedReader"/>, which adds the id tie-breaker).</summary>
public sealed class WaitingArchivePlayersSpec : Specification<Player>
{
    /// <summary>Initializes the specification.</summary>
    public WaitingArchivePlayersSpec() =>
        Query.Where(player => player.Origin == PlayerOrigin.Archive && player.Status == PlayerStatus.Waiting)
            .OrderBy(player => player.CreatedAt);
}

/// <summary>The linked folders of some players, newest first, read untracked.</summary>
public sealed class FoldersOfPlayersSpec : Specification<DriveFolder>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="playerIds">The players (one page, at most 100).</param>
    public FoldersOfPlayersSpec(IReadOnlyCollection<Guid> playerIds) =>
        Query.Where(folder => playerIds.Contains(folder.PlayerId)).OrderByDescending(folder => folder.LinkedAt).AsNoTracking();
}

/// <summary>Reads one page of the archive CSV (<c>GET /admin/archive/waiting.csv</c> streams every page), for admins only.</summary>
/// <param name="Paging">The page.</param>
public sealed record GetArchiveCsvPageQuery(PageRequest Paging) : IPagedQuery<ArchiveCsvRowDto>, IAuthorizedRequest
{
    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="GetArchiveCsvPageQuery"/>.</summary>
public sealed class GetArchiveCsvPageQueryValidator : AbstractValidator<GetArchiveCsvPageQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetArchiveCsvPageQueryValidator() => RuleFor(query => query.Paging).ValidPaging();
}

/// <summary>Handles <see cref="GetArchiveCsvPageQuery"/>.</summary>
/// <param name="pages">The paged reader.</param>
/// <param name="folders">The linked folders.</param>
public sealed class GetArchiveCsvPageQueryHandler(IPagedReader pages, IRepository<DriveFolder, Guid> folders)
    : IQueryHandler<GetArchiveCsvPageQuery, PagedResult<ArchiveCsvRowDto>>
{
    /// <summary>Reads one page and each player's newest folder name.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<PagedResult<ArchiveCsvRowDto>> Handle(GetArchiveCsvPageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await pages.PageAsync(
            new WaitingArchivePlayersSpec(),
            request.Paging,
            player => new ArchiveCsvRowDto
            {
                PlayerId = player.Id,
                Category = player.Category.ToString(),
                Name = player.Name?.Value,
                NationalityCode = player.Nationality?.Code,
                Positions = player.Positions?.Items.Select(position => position.Code()).ToList() ?? [],
                VolleyboxUrl = player.Volleybox?.Url,
            },
            cancellationToken);
        if (page.Items.Count == 0)
        {
            return page;
        }

        var names = (await folders.ListAsync(new FoldersOfPlayersSpec(page.Items.Select(row => row.PlayerId).ToList()), cancellationToken))
            .GroupBy(folder => folder.PlayerId)
            .ToDictionary(group => group.Key, group => group.First().Name);
        return page with { Items = page.Items.Select(row => row with { Folder = names.GetValueOrDefault(row.PlayerId) }).ToList() };
    }
}

/// <summary>
/// Reads the filled-in archive CSV back (<c>POST /admin/archive/waiting.csv</c>, M-3), for admins only. Every row is checked
/// on its own with the player rules; bad rows are reported and change nothing, good rows are saved. Empty cells leave a
/// detail as it is. Only waiting archive players change; nothing is published.
/// </summary>
/// <param name="Csv">The file's text.</param>
public sealed record ImportArchiveCsvCommand(string Csv) : ICommand<ArchiveCsvResultDto>, IAuthorizedRequest
{
    /// <summary>The longest file accepted, in characters (about 1 MB).</summary>
    public const int MaxCharacters = 1_000_000;

    /// <summary>The most data rows a file may have.</summary>
    public const int MaxRows = 2000;

    /// <summary>Gets the roles allowed: admins.</summary>
    public IReadOnlyCollection<string> RequiredRoles => ShareLinkRules.AdminRoles;
}

/// <summary>Validates <see cref="ImportArchiveCsvCommand"/> on the server: some text, at most 1 MB.</summary>
public sealed class ImportArchiveCsvCommandValidator : AbstractValidator<ImportArchiveCsvCommand>
{
    /// <summary>Builds the rules.</summary>
    public ImportArchiveCsvCommandValidator() =>
        RuleFor(command => command.Csv)
            .NotEmpty().Must(csv => !string.IsNullOrWhiteSpace(csv)).WithMessage("The file is empty.")
            .MaximumLength(ImportArchiveCsvCommand.MaxCharacters).WithMessage("The file is larger than 1 MB.")
            .OverridePropertyName("csv");
}

/// <summary>Handles <see cref="ImportArchiveCsvCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="audit">The audit log.</param>
/// <param name="currentUser">The admin.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class ImportArchiveCsvCommandHandler(
    IRepository<Player, Guid> players,
    IRepository<AuditEntry, Guid> audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ImportArchiveCsvCommand, ArchiveCsvResultDto>
{
    /// <summary>Applies the file.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What changed and which rows were refused.</returns>
    /// <exception cref="DomainException"><c>archive.csv_invalid_header</c> without an <c>Id</c> column; <c>archive.csv_too_many_rows</c> over the limit.</exception>
    public async Task<ArchiveCsvResultDto> Handle(ImportArchiveCsvCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rows = ArchiveCsv.Parse(request.Csv);
        var columns = rows.Count == 0 ? new Dictionary<string, int>() : Columns(rows[0]);
        if (!columns.ContainsKey("Id"))
        {
            throw new DomainException("archive.csv_invalid_header", $"The first line must name the columns, with an Id column: {ArchiveCsv.HeaderLine}.");
        }

        if (rows.Count - 1 > ImportArchiveCsvCommand.MaxRows)
        {
            throw new DomainException("archive.csv_too_many_rows", $"A file can have at most {ImportArchiveCsvCommand.MaxRows} rows.");
        }

        var batch = new CsvBatch(players);
        var errors = new List<ArchiveCsvErrorDto>();
        int updated = 0, unchanged = 0;
        for (var index = 1; index < rows.Count; index++)
        {
            var rowNumber = index + 1;
            try
            {
                if (await batch.ApplyAsync(new CsvRow(rows[index], columns), cancellationToken))
                {
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }
            catch (DomainException exception)
            {
                errors.Add(new ArchiveCsvErrorDto { Row = rowNumber, Code = exception.Code, Message = exception.Message });
            }
        }

        audit.Add(AuditEntry.Record(
            ShareLinkRules.ActorId(currentUser),
            ArchiveRules.CsvImportedAction,
            "ArchiveCsv",
            "waiting",
            $$"""{"updated":{{updated}},"unchanged":{{unchanged}},"errors":{{errors.Count}}}""",
            timeProvider.GetUtcNow()));
        return new ArchiveCsvResultDto { Updated = updated, Unchanged = unchanged, Errors = errors };
    }

    /// <summary>Maps the header's column names to their places, ignoring case and spaces.</summary>
    /// <param name="header">The header row.</param>
    /// <returns>The places by canonical column name.</returns>
    private static Dictionary<string, int> Columns(IReadOnlyList<string> header)
    {
        var places = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            var name = ArchiveCsv.Columns.FirstOrDefault(column => string.Equals(column, header[i].Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is not null)
            {
                places.TryAdd(name, i);
            }
        }

        return places;
    }

    /// <summary>One data row, read by column name.</summary>
    /// <param name="cells">The cells.</param>
    /// <param name="columns">The column places.</param>
    private readonly struct CsvRow(IReadOnlyList<string> cells, Dictionary<string, int> columns)
    {
        /// <summary>Reads a cell: trimmed, without the export's formula guard, or <see langword="null"/> when empty or absent.</summary>
        /// <param name="column">The column name.</param>
        /// <returns>The value.</returns>
        public string? this[string column] =>
            columns.TryGetValue(column, out var place) && place < cells.Count && ArchiveCsv.Unguard(cells[place].Trim()) is { Length: > 0 } value ? value : null;
    }

    /// <summary>Applies rows one at a time and remembers what the earlier rows of the file took.</summary>
    /// <param name="players">The players.</param>
    private sealed class CsvBatch(IRepository<Player, Guid> players)
    {
        /// <summary>The players already changed by this file.</summary>
        private readonly HashSet<Guid> _seen = [];

        /// <summary>The Volleybox ids given out by this file.</summary>
        private readonly HashSet<int> _volleybox = [];

        /// <summary>The slugs given out by this file.</summary>
        private readonly UnsavedSlugs _slugs = new(players);

        /// <summary>Checks a row in full, then applies it.</summary>
        /// <param name="row">The row.</param>
        /// <param name="cancellationToken">Cancels the queries.</param>
        /// <returns><see langword="true"/> when the player changed.</returns>
        /// <exception cref="DomainException">The row is refused; the player is unchanged.</exception>
        public async Task<bool> ApplyAsync(CsvRow row, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(row["Id"], out var id))
            {
                throw new DomainException("archive.csv_bad_id", "The Id cell is not a player id from the export.");
            }

            if (!_seen.Add(id))
            {
                throw new DomainException("archive.csv_duplicate_row", "This player is already in an earlier row.");
            }

            if (await players.GetByIdAsync(id, cancellationToken) is not { Origin: PlayerOrigin.Archive, Status: PlayerStatus.Waiting } player)
            {
                throw new DomainException("archive.csv_player_not_waiting", "No waiting archive player has this id.");
            }

            // Every value is built and checked before anything changes, so a refused row leaves the player as it was.
            PlayerCategory? category = row["Category"] is { } categoryText
                ? Enum.TryParse<PlayerCategory>(categoryText, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : throw new DomainException("archive.csv_bad_category", "Category must be Men or Women.")
                : null;
            var name = row["Name"] is { } nameText ? FullName.Create(nameText) : null;
            var nationality = row["Nationality"] is { } code ? Nationality.Create(code) : null;
            var positions = row["Positions"] is { } positionText ? ParsePositions(positionText) : null;
            var volleybox = row["Volleybox"] is { } url ? VolleyboxProfile.Create(url) : null;
            if (volleybox is not null && volleybox.PlayerId != player.Volleybox?.PlayerId)
            {
                if (_volleybox.Contains(volleybox.PlayerId))
                {
                    throw new DomainException("player.volleybox_taken", "A player with this Volleybox page already exists.");
                }

                await PlayerUniqueness.EnsureVolleyboxFreeAsync(players, volleybox, player.Id, cancellationToken);
            }

            var changed = false;
            if (category is { } newCategory && newCategory != player.Category)
            {
                player.ChangeCategory(newCategory);
                changed = true;
            }

            if (name is not null && name != player.Name)
            {
                player.Rename(name, await _slugs.TakeAsync(name, player.Id, cancellationToken));
                changed = true;
            }

            if (nationality is not null && nationality != player.Nationality)
            {
                player.ChangeNationality(nationality);
                changed = true;
            }

            if (positions is not null && positions != player.Positions)
            {
                player.ChangePositions(positions);
                changed = true;
            }

            if (volleybox is not null && volleybox.PlayerId != player.Volleybox?.PlayerId)
            {
                player.LinkVolleybox(volleybox);
                _volleybox.Add(volleybox.PlayerId);
                changed = true;
            }

            return changed;
        }

        /// <summary>Reads positions written as codes or names joined by <c>/</c>, <c>,</c> or <c>-</c>.</summary>
        /// <param name="text">The cell.</param>
        /// <returns>The positions.</returns>
        /// <exception cref="DomainException">An unknown position, a repeated one, or more than two.</exception>
        private static PlayerPositions ParsePositions(string text)
        {
            var parsed = new List<Position>();
            foreach (var part in text.Split(['/', ',', '-', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                parsed.Add(PositionParser.TryParse(part, out var position)
                    ? position
                    : throw new DomainException("archive.csv_bad_position", $"Unknown position \"{part}\". Use S, OH, OPP, MB, L or DS."));
            }

            return PlayerPositions.Create(parsed);
        }
    }
}
