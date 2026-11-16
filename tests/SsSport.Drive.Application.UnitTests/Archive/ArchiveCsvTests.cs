using SsSport.Drive.Application.Archive;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Archive;

/// <summary>
/// M-3: the waiting archive players go out as CSV (<c>Id, Folder, Category, Name, Nationality, Positions, Volleybox</c>), the
/// team fills in the blanks in a spreadsheet, and the file comes back. Each row is checked with the player rules on its own:
/// a bad row is reported with its row number and code and changes nothing, the good rows are saved. An empty cell leaves
/// the detail as it is. Only waiting archive players can change; nothing is published and photos never come by CSV.
/// </summary>
public sealed class ArchiveCsvTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The header the export writes.</summary>
    private const string Header = "Id,Folder,Category,Name,Nationality,Positions,Volleybox";

    /// <summary>The admin.</summary>
    private static readonly Guid _admin = Guid.CreateVersion7(_now);

    /// <summary>The players.</summary>
    private readonly InMemoryRepository<Player> _players = new();

    /// <summary>The audit log.</summary>
    private readonly InMemoryRepository<AuditEntry> _audit = new();

    /// <summary>The parser reads quoted cells with commas, doubled quotes and line breaks, and both line endings.</summary>
    [Fact]
    public void The_parser_reads_rfc_4180()
    {
        var rows = ArchiveCsv.Parse("a,\"b,1\",\"say \"\"hi\"\"\"\r\nx,\"two\nlines\",\n\nlast,,");

        rows.Should().HaveCount(3);
        rows[0].Should().Equal("a", "b,1", "say \"hi\"");
        rows[1].Should().Equal("x", "two\nlines", "");
        rows[2].Should().Equal("last", "", "");
    }

    /// <summary>A cell the export guarded against formulas (a leading apostrophe) reads back without the apostrophe.</summary>
    [Fact]
    public void A_formula_guard_is_removed_on_the_way_back()
    {
        ArchiveCsv.Unguard("'=1+1").Should().Be("=1+1");
        ArchiveCsv.Unguard("'-OH").Should().Be("-OH");
        ArchiveCsv.Unguard("O'Neil").Should().Be("O'Neil");
        ArchiveCsv.Unguard("'Neil").Should().Be("'Neil");
    }

    /// <summary>The filled-in details of a waiting archive player are saved; empty cells leave a detail as it is.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Filled_in_details_are_saved()
    {
        var player = Archive("Ivan Valeev");
        var csv = $"{Header}\r\n{player.Id},SS-SPORT Ivan Valeev,Women,,ru,OH/OPP,https://volleybox.net/ivan-valeev-p60214\r\n";

        var result = await Handler().Handle(new ImportArchiveCsvCommand(csv), TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(new ArchiveCsvResultDto { Updated = 1, Unchanged = 0, Errors = [] });
        player.Name!.Value.Should().Be("Ivan Valeev");
        player.Category.Should().Be(PlayerCategory.Women);
        player.Nationality!.Code.Should().Be("RU");
        player.Positions!.Items.Should().Equal(Position.OutsideHitter, Position.Opposite);
        player.Volleybox!.PlayerId.Should().Be(60214);
        player.Status.Should().Be(PlayerStatus.Waiting);
        _audit.Added.Should().ContainSingle(entry => entry.Action == "archive.csv_imported" && entry.ActorUserId == _admin);
    }

    /// <summary>A row that matches the player already counts as unchanged.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_row_that_changes_nothing_is_unchanged()
    {
        var player = Archive("Ivan Valeev");
        var csv = $"{Header}\n{player.Id},x,Men,Ivan Valeev,,,\n";

        var result = await Handler().Handle(new ImportArchiveCsvCommand(csv), TestContext.Current.CancellationToken);

        result.Updated.Should().Be(0);
        result.Unchanged.Should().Be(1);
    }

    /// <summary>A bad row is reported with its row number and code and changes nothing; the good rows still save.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_bad_row_is_reported_and_the_others_save()
    {
        var good = Archive("Ivan Valeev");
        var badNationality = Archive("Sara Ahmadi");
        var badPositions = Archive("Aref Moradi");
        var csv = string.Join(
            "\n",
            "Name,Id,Nationality,Positions,Volleybox,Category,Folder",
            $"Ivan Valeev,{good.Id},IR,,,,",
            $"Sara Renamed,{badNationality.Id},ZZ,MB,,,",
            $"Aref Moradi,{badPositions.Id},,OH/OPP/MB,,,",
            $"Nobody,{Guid.CreateVersion7(_now)},IR,,,,",
            "Nobody,not-an-id,IR,,,,",
            $"Ivan Again,{good.Id},IR,,,,");

        var result = await Handler().Handle(new ImportArchiveCsvCommand(csv), TestContext.Current.CancellationToken);

        result.Updated.Should().Be(1);
        result.Errors.Select(error => (error.Row, error.Code)).Should().Equal(
            (3, "player.nationality_invalid"),
            (4, "player.too_many_positions"),
            (5, "archive.csv_player_not_waiting"),
            (6, "archive.csv_bad_id"),
            (7, "archive.csv_duplicate_row"));
        good.Nationality!.Code.Should().Be("IR");
        badNationality.Name!.Value.Should().Be("Sara Ahmadi");
        badNationality.Positions.Should().BeNull();
    }

    /// <summary>Published players and players the team added are not changed by the archive CSV.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Only_waiting_archive_players_change()
    {
        var team = Player.Create(PlayerCategory.Men, _now);
        _players.Seed(team);
        var csv = $"{Header}\n{team.Id},x,Men,Team Player,,,";

        var result = await Handler().Handle(new ImportArchiveCsvCommand(csv), TestContext.Current.CancellationToken);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be("archive.csv_player_not_waiting");
        team.Name.Should().BeNull();
    }

    /// <summary>A Volleybox page that belongs to another player, or to another row of the file, is refused.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_volleybox_page_is_used_once()
    {
        var owner = Archive("Ivan Valeev");
        owner.LinkVolleybox(VolleyboxProfile.Create("https://volleybox.net/ivan-valeev-p1"));
        var first = Archive("Sara Ahmadi");
        var second = Archive("Aref Moradi");
        var third = Archive("Reza Karimi");
        var csv = string.Join(
            "\n",
            Header,
            $"{first.Id},x,,,,,volleybox.net/someone-p1",
            $"{second.Id},x,,,,,volleybox.net/aref-p2",
            $"{third.Id},x,,,,,volleybox.net/reza-p2");

        var result = await new ImportArchiveCsvCommandHandler(new UnsavedBlind<Player>(_players), _audit, new AdminUser(_admin), new FixedClock(_now))
            .Handle(new ImportArchiveCsvCommand(csv), TestContext.Current.CancellationToken);

        result.Errors.Select(error => (error.Row, error.Code)).Should().Equal((2, "player.volleybox_taken"), (4, "player.volleybox_taken"));
        second.Volleybox!.PlayerId.Should().Be(2);
        third.Volleybox.Should().BeNull();
    }

    /// <summary>Two rows renamed to the same name get different slugs.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Same_new_names_get_different_slugs()
    {
        var first = Archive("Ivan Valeev");
        var second = Archive("Sara Ahmadi");
        var csv = $"{Header}\n{first.Id},x,,Ali Karimi,,,\n{second.Id},x,,Ali Karimi,,,";

        await new ImportArchiveCsvCommandHandler(new UnsavedBlind<Player>(_players), _audit, new AdminUser(_admin), new FixedClock(_now))
            .Handle(new ImportArchiveCsvCommand(csv), TestContext.Current.CancellationToken);

        first.Slug!.Value.Should().Be("ali-karimi");
        second.Slug!.Value.Should().Be("ali-karimi-2");
    }

    /// <summary>A file without the Id column, or with more rows than allowed, is refused whole.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_file_without_ids_or_too_long_is_refused()
    {
        var noId = () => Handler().Handle(new ImportArchiveCsvCommand("Name,Nationality\nIvan,RU"), TestContext.Current.CancellationToken);
        (await noId.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("archive.csv_invalid_header");

        var rows = string.Join("\n", Enumerable.Repeat($"{Guid.NewGuid()},x,,,,,", ImportArchiveCsvCommand.MaxRows + 1));
        var tooLong = () => Handler().Handle(new ImportArchiveCsvCommand($"{Header}\n{rows}"), TestContext.Current.CancellationToken);
        (await tooLong.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("archive.csv_too_many_rows");
    }

    /// <summary>The file is sent as text, at most 1 MB.</summary>
    [Fact]
    public void The_file_is_at_most_a_megabyte()
    {
        var validator = new ImportArchiveCsvCommandValidator();

        validator.Validate(new ImportArchiveCsvCommand(Header)).IsValid.Should().BeTrue();
        validator.Validate(new ImportArchiveCsvCommand(" ")).IsValid.Should().BeFalse();
        validator.Validate(new ImportArchiveCsvCommand(new string('a', ImportArchiveCsvCommand.MaxCharacters + 1))).IsValid.Should().BeFalse();
    }

    /// <summary>The export writes one row per waiting archive player with the cells the import reads.</summary>
    [Fact]
    public void The_export_writes_the_columns_the_import_reads()
    {
        var row = new ArchiveCsvRowDto
        {
            PlayerId = Guid.Parse("0199f6a4-2222-7b3d-9a52-3f1c8e7d2b10"),
            Folder = "SS-SPORT Ivan Valeev／OH",
            Category = "Men",
            Name = "Ivan Valeev",
            NationalityCode = null,
            Positions = ["OH", "OPP"],
            VolleyboxUrl = null,
        };

        ArchiveCsv.HeaderLine.Should().Be(Header);
        ArchiveCsv.Line(row).Should().Be("0199f6a4-2222-7b3d-9a52-3f1c8e7d2b10,SS-SPORT Ivan Valeev／OH,Men,Ivan Valeev,,OH/OPP,");
        ArchiveCsv.Line(row with { Name = "=HYPERLINK(\"x\")" }).Should().Contain("\"'=HYPERLINK(\"\"x\"\")\"");
    }

    /// <summary>Builds the handler.</summary>
    /// <returns>The handler.</returns>
    private ImportArchiveCsvCommandHandler Handler() => new(_players, _audit, new AdminUser(_admin), new FixedClock(_now));

    /// <summary>Seeds a waiting archive player with a name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The player.</returns>
    private Player Archive(string name)
    {
        var player = Player.CreateFromArchive(PlayerCategory.Men, _now);
        var fullName = FullName.Create(name);
        player.Rename(fullName, Slug.FromName(fullName));
        _players.Seed(player);
        return player;
    }
}
