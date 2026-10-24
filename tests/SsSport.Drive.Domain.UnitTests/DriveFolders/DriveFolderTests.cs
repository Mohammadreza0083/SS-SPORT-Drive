using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.UnitTests.DriveFolders;

/// <summary>A linked Google Drive folder: one player can have several; access can be lost and regained.</summary>
public sealed class DriveFolderTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The owning player.</summary>
    private static readonly Guid _playerId = Guid.CreateVersion7(_now);

    /// <summary>Linking stores the Drive id, the name and the player, with access OK and no sync yet.</summary>
    [Fact]
    public void Linking_a_folder()
    {
        var folder = DriveFolder.Link(_playerId, " 1AbCdEf ", " SS-SPORT Ali Hoshmand／OH ", _now);

        folder.Id.Version.Should().Be(7);
        folder.PlayerId.Should().Be(_playerId);
        folder.DriveFolderId.Should().Be("1AbCdEf");
        folder.Name.Should().Be("SS-SPORT Ali Hoshmand／OH");
        folder.LinkedAt.Should().Be(_now);
        folder.LastSyncedAt.Should().BeNull();
        folder.Access.Should().Be(FolderAccess.Ok);
        folder.DomainEvents.Should().ContainSingle().Which.Should().Be(new DriveFolderLinked(folder.Id, _playerId));
    }

    /// <summary>The Drive id and the name are required.</summary>
    /// <param name="driveId">The Drive folder id.</param>
    /// <param name="name">The folder name.</param>
    [Theory]
    [InlineData("", "name")]
    [InlineData("id", " ")]
    public void Drive_id_and_name_are_required(string driveId, string name)
    {
        var link = () => DriveFolder.Link(_playerId, driveId, name, _now);

        link.Should().Throw<DomainException>().Which.Code.Should().Be("drive_folder.invalid");
    }

    /// <summary>Losing access raises FolderAccessLost once.</summary>
    [Fact]
    public void Losing_access_raises_an_event_once()
    {
        var folder = DriveFolder.Link(_playerId, "id", "name", _now);
        folder.ClearDomainEvents();

        folder.MarkAccessLost(_now.AddMinutes(10));
        folder.MarkAccessLost(_now.AddMinutes(20));

        folder.Access.Should().Be(FolderAccess.Lost);
        folder.DomainEvents.Should().ContainSingle().Which.Should().Be(new FolderAccessLost(folder.Id, _playerId));
    }

    /// <summary>A successful sync records the time and restores lost access.</summary>
    [Fact]
    public void A_successful_sync_restores_access()
    {
        var folder = DriveFolder.Link(_playerId, "id", "name", _now);
        folder.MarkAccessLost(_now.AddMinutes(10));
        folder.ClearDomainEvents();

        folder.MarkSynced(_now.AddMinutes(20));

        folder.Access.Should().Be(FolderAccess.Ok);
        folder.LastSyncedAt.Should().Be(_now.AddMinutes(20));
        folder.DomainEvents.Should().ContainSingle().Which.Should().Be(new FolderAccessRestored(folder.Id, _playerId));
    }

    /// <summary>A routine sync raises nothing.</summary>
    [Fact]
    public void A_routine_sync_raises_nothing()
    {
        var folder = DriveFolder.Link(_playerId, "id", "name", _now);
        folder.ClearDomainEvents();

        folder.MarkSynced(_now.AddMinutes(10));

        folder.DomainEvents.Should().BeEmpty();
    }

    /// <summary>A folder renamed in Drive keeps its id and takes the new name.</summary>
    [Fact]
    public void Rename_takes_the_new_name()
    {
        var folder = DriveFolder.Link(_playerId, "id", "old", _now);

        folder.Rename("SS-SPORT Ivan Valeev／MB");

        folder.Name.Should().Be("SS-SPORT Ivan Valeev／MB");
    }
}
