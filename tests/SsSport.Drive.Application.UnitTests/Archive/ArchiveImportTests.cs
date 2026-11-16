using SsSport.Drive.Application.Archive;
using SsSport.Drive.Domain.Archive;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Application.UnitTests.Archive;

/// <summary>
/// M-2: an admin imports found folders; each becomes an archive player on the waiting list with its category and the
/// name and positions its folder name suggests, the Drive folder linked to it (so the normal sync lists its files). No
/// player is ever published by an import. Folders already in the app are refused one by one, the rest go through.
/// </summary>
public sealed class ArchiveImportTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The admin.</summary>
    private static readonly Guid _admin = Guid.CreateVersion7(_now);

    /// <summary>The found folders.</summary>
    private readonly InMemoryRepository<ArchiveFolder> _found = new();

    /// <summary>The players.</summary>
    private readonly InMemoryRepository<Player> _players = new();

    /// <summary>The linked Drive folders.</summary>
    private readonly InMemoryRepository<DriveFolder> _linked = new();

    /// <summary>The audit log.</summary>
    private readonly InMemoryRepository<AuditEntry> _audit = new();

    /// <summary>An imported folder becomes a waiting archive player with the suggested details and its folder linked.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_folder_becomes_a_waiting_archive_player()
    {
        var folder = Found("p1", "SS-SPORT Ivan Valeev／OH-OPP", PlayerCategory.Men);

        var result = await Handler().Handle(new ImportArchiveFoldersCommand([folder.Id]), TestContext.Current.CancellationToken);

        var player = _players.Added.Should().ContainSingle().Subject;
        player.Origin.Should().Be(PlayerOrigin.Archive);
        player.Status.Should().Be(PlayerStatus.Waiting);
        player.AnalystId.Should().BeNull();
        player.Category.Should().Be(PlayerCategory.Men);
        player.Name!.Value.Should().Be("Ivan Valeev");
        player.Slug!.Value.Should().Be("ivan-valeev");
        player.Positions!.Items.Should().Equal(Position.OutsideHitter, Position.Opposite);

        var link = _linked.Added.Should().ContainSingle().Subject;
        link.PlayerId.Should().Be(player.Id);
        link.DriveFolderId.Should().Be("p1");
        link.Name.Should().Be("SS-SPORT Ivan Valeev／OH-OPP");
        link.DomainEvents.Should().ContainSingle(domainEvent => domainEvent is DriveFolderLinked);

        folder.State.Should().Be(ArchiveFolderState.Imported);
        folder.PlayerId.Should().Be(player.Id);
        result.Imported.Should().ContainSingle().Which.Should().Be(new ArchiveImportedDto(folder.Id, player.Id));
        result.Refused.Should().BeEmpty();
        _audit.Added.Should().ContainSingle(entry => entry.Action == "archive.imported" && entry.TargetId == player.Id.ToString() && entry.ActorUserId == _admin);
    }

    /// <summary>A folder whose name suggests no valid name or position still imports; the player waits for them.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_folder_without_suggestions_still_imports()
    {
        var folder = Found("p1", "SS-SPORT علی", PlayerCategory.Women);

        await Handler().Handle(new ImportArchiveFoldersCommand([folder.Id]), TestContext.Current.CancellationToken);

        var player = _players.Added.Should().ContainSingle().Subject;
        player.Name.Should().BeNull();
        player.Positions.Should().BeNull();
        player.MissingDetails().Should().Contain([PlayerDetail.Name, PlayerDetail.Positions]);
    }

    /// <summary>Folders already in the app are refused one by one, with a code; the others are imported.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Folders_already_in_the_app_are_refused_one_by_one()
    {
        var fresh = Found("p1", "SS-SPORT Ivan Valeev", PlayerCategory.Men);
        var imported = Found("p2", "SS-SPORT Sara Ahmadi", PlayerCategory.Women);
        imported.MarkImported(Guid.CreateVersion7(_now), _now);
        var linkedSince = Found("p3", "SS-SPORT Aref Moradi", PlayerCategory.Men);
        _linked.Seed(DriveFolder.Link(Guid.CreateVersion7(_now), "p3", "SS-SPORT Aref Moradi", _now));
        var unknown = Guid.CreateVersion7(_now);

        var result = await Handler().Handle(new ImportArchiveFoldersCommand([fresh.Id, imported.Id, linkedSince.Id, unknown]), TestContext.Current.CancellationToken);

        result.Imported.Should().ContainSingle().Which.FolderId.Should().Be(fresh.Id);
        result.Refused.Should().BeEquivalentTo(
        [
            new ArchiveRefusedDto(imported.Id, "archive.folder_not_new"),
            new ArchiveRefusedDto(linkedSince.Id, "drive_folder.taken"),
            new ArchiveRefusedDto(unknown, "archive.folder_not_found"),
        ]);
        _players.Added.Should().ContainSingle();
    }

    /// <summary>Two folders with the same name in one import get different slugs, though neither is saved yet.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Same_names_in_one_import_get_different_slugs()
    {
        var existing = Player.Create(PlayerCategory.Men, _now);
        existing.Rename(FullName.Create("Ali Karimi"), Slug.Create("ali-karimi"));
        _players.Seed(existing);
        var first = Found("p1", "SS-SPORT Ali Karimi", PlayerCategory.Men);
        var second = Found("p2", "SS-SPORT Ali  Karimi／OH", PlayerCategory.Women);

        await new ImportArchiveFoldersCommandHandler(_found, new UnsavedBlind<Player>(_players), _linked, _audit, new AdminUser(_admin), new FixedClock(_now))
            .Handle(new ImportArchiveFoldersCommand([first.Id, second.Id]), TestContext.Current.CancellationToken);

        _players.Added.Select(player => player.Slug!.Value).Should().Equal("ali-karimi-2", "ali-karimi-3");
    }

    /// <summary>The same Drive folder found by two scans is imported once, though the first link is not saved yet.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task One_drive_folder_is_imported_once_per_request()
    {
        var older = Found("p1", "SS-SPORT Ivan Valeev", PlayerCategory.Men);
        var newer = Found("p1", "SS-SPORT Ivan Valeev", PlayerCategory.Men);

        var result = await new ImportArchiveFoldersCommandHandler(_found, _players, new UnsavedBlind<DriveFolder>(_linked), _audit, new AdminUser(_admin), new FixedClock(_now))
            .Handle(new ImportArchiveFoldersCommand([older.Id, newer.Id]), TestContext.Current.CancellationToken);

        result.Imported.Should().ContainSingle();
        result.Refused.Should().ContainSingle().Which.Should().Be(new ArchiveRefusedDto(newer.Id, "drive_folder.taken"));
    }

    /// <summary>A request names 1 to 100 distinct folders.</summary>
    [Fact]
    public void The_request_names_one_to_a_hundred_distinct_folders()
    {
        var validator = new ImportArchiveFoldersCommandValidator();
        var id = Guid.CreateVersion7(_now);

        validator.Validate(new ImportArchiveFoldersCommand([id])).IsValid.Should().BeTrue();
        validator.Validate(new ImportArchiveFoldersCommand([])).IsValid.Should().BeFalse();
        validator.Validate(new ImportArchiveFoldersCommand([id, id])).IsValid.Should().BeFalse();
        validator.Validate(new ImportArchiveFoldersCommand([Guid.Empty])).IsValid.Should().BeFalse();
        validator.Validate(new ImportArchiveFoldersCommand(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList())).IsValid.Should().BeFalse();
    }

    /// <summary>Builds the handler.</summary>
    /// <returns>The handler.</returns>
    private ImportArchiveFoldersCommandHandler Handler() => new(_found, _players, _linked, _audit, new AdminUser(_admin), new FixedClock(_now));

    /// <summary>Seeds a found folder.</summary>
    /// <param name="driveId">The Drive folder id.</param>
    /// <param name="name">The folder name.</param>
    /// <param name="category">The category.</param>
    /// <returns>The folder.</returns>
    private ArchiveFolder Found(string driveId, string name, PlayerCategory category)
    {
        var folder = ArchiveFolder.Record(Guid.CreateVersion7(_now), driveId, $"{category} Volleyball/{name}", name, category, ArchiveFolderCounts.Empty, null, _now);
        _found.Seed(folder);
        return folder;
    }
}
