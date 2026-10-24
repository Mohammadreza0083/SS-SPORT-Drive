using SsSport.Drive.Application.Assets;
using SsSport.Drive.Application.Audit;
using SsSport.Drive.Application.DriveFolders;
using SsSport.Drive.Application.Players;
using SsSport.Drive.Application.Users;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Audit;
using SsSport.Drive.Domain.DriveFolders;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Application.UnitTests.Mapping;

/// <summary>
/// The Mapperly mappers turn aggregates into response DTOs, flattening value objects and enums to plain values.
/// (A DTO member without a mapping is a build error, so there is no runtime "configuration is valid" check.)
/// </summary>
public sealed class MappingTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A complete, published player maps to its details DTO with flattened values.</summary>
    [Fact]
    public void Player_maps_to_details()
    {
        var player = Player.Create(PlayerCategory.Women, _now);
        var name = FullName.Create("Arash Tehrani");
        player.Rename(name, Slug.FromName(name));
        player.ChangeNationality(Nationality.Create("ir"));
        player.ChangePositions(PlayerPositions.Create([Position.OutsideHitter, Position.Opposite]));
        player.LinkVolleybox(VolleyboxProfile.Create("volleybox.net/arash-tehrani-p48213"));
        player.ChangePhoto(PlayerPhoto.Create("players/p1/photo.webp"));
        player.Publish(_now.AddHours(1));
        player.SetQrCode(StorageKey.Create("qr/p1.png"));

        var dto = player.ToDetailsDto();

        dto.Id.Should().Be(player.Id);
        dto.Name.Should().Be("Arash Tehrani");
        dto.Slug.Should().Be("arash-tehrani");
        dto.Category.Should().Be("Women");
        dto.NationalityCode.Should().Be("IR");
        dto.NationalityName.Should().Be("Iran");
        dto.Positions.Should().Equal("OH", "OPP");
        dto.VolleyboxId.Should().Be(48213);
        dto.VolleyboxUrl.Should().Be("https://volleybox.net/arash-tehrani-p48213");
        dto.PhotoKey.Should().Be("players/p1/photo.webp");
        dto.QrCodeKey.Should().Be("qr/p1.png");
        dto.Status.Should().Be("Published");
        dto.IsComplete.Should().BeTrue();
        dto.MissingDetails.Should().BeEmpty();
        dto.PublishedAt.Should().Be(_now.AddHours(1));
    }

    /// <summary>A waiting player maps with empty values and lists what is missing.</summary>
    [Fact]
    public void Waiting_player_maps_to_summary_and_details()
    {
        var player = Player.Create(PlayerCategory.Men, _now);

        var summary = player.ToSummaryDto();
        var details = player.ToDetailsDto();

        summary.Name.Should().BeNull();
        summary.NationalityCode.Should().BeNull();
        summary.Positions.Should().BeEmpty();
        summary.Status.Should().Be("Waiting");
        summary.IsComplete.Should().BeFalse();
        details.MissingDetails.Should().Equal("Name", "Nationality", "Positions", "Volleybox", "Photo");
        details.VolleyboxId.Should().BeNull();
    }

    /// <summary>An asset maps with its file facts, tag names and renditions.</summary>
    [Fact]
    public void Asset_maps_with_renditions()
    {
        var asset = Asset.Add(Guid.CreateVersion7(_now), null, AssetSource.Drive, "f1", AssetFile.Create("game.mp4", "9e107d9d372bb6826bd81d3542a419d6", 2048), "Final", AssetTags.FullMatch | AssetTags.Highlight, 4, _now);
        asset.StartCopying();
        asset.AttachOriginal(StorageKey.Create("originals/game.mp4"));
        asset.RecordVideoInfo(720, 3600);
        asset.MarkRenditionReady(RenditionQuality.P360, StorageKey.Create("hls/a/360/index.m3u8"));

        var dto = asset.ToDto();

        dto.Kind.Should().Be("Video");
        dto.Source.Should().Be("Drive");
        dto.OriginalName.Should().Be("game.mp4");
        dto.SizeBytes.Should().Be(2048);
        dto.Tags.Should().Equal("Highlight", "FullMatch");
        dto.State.Should().Be("Processing");
        dto.Visibility.Should().Be("Visible");
        dto.DurationSeconds.Should().Be(3600);
        dto.Renditions.Select(r => (r.Quality, r.Status, r.PlaylistKey)).Should().Equal(
            (360, "Ready", "hls/a/360/index.m3u8"),
            (720, "Pending", null));
    }

    /// <summary>An untagged asset maps to an empty tag list.</summary>
    [Fact]
    public void Untagged_asset_has_no_tags()
    {
        var asset = Asset.Add(Guid.CreateVersion7(_now), null, AssetSource.Archive, null, AssetFile.Create("r.pdf", "9e107d9d372bb6826bd81d3542a419d6", 10), "Report", AssetTags.Untagged, 0, _now);

        asset.ToDto().Tags.Should().BeEmpty();
    }

    /// <summary>A Drive folder maps with its access state.</summary>
    [Fact]
    public void Drive_folder_maps()
    {
        var folder = DriveFolder.Link(Guid.CreateVersion7(_now), "1AbC", "SS-SPORT Ali Hoshmand／OH", _now);
        folder.MarkAccessLost(_now.AddMinutes(10));

        var dto = folder.ToDto();

        dto.DriveFolderId.Should().Be("1AbC");
        dto.Access.Should().Be("Lost");
        dto.AccessLostAt.Should().Be(_now.AddMinutes(10));
    }

    /// <summary>A user maps with role, status and the full email (admin-only DTO).</summary>
    [Fact]
    public void User_maps()
    {
        var user = User.CreateFirstAdmin(42, "Owner", EmailAddress.Create("owner@ss-sport.co"), _now);

        var dto = user.ToDto();

        dto.TelegramId.Should().Be(42);
        dto.Email.Should().Be("owner@ss-sport.co");
        dto.Role.Should().Be("Admin");
        dto.Status.Should().Be("Active");
    }

    /// <summary>An audit entry maps as-is.</summary>
    [Fact]
    public void Audit_entry_maps()
    {
        var entry = AuditEntry.Record(null, "asset.hidden", "Asset", "a1", """{"reason":"removed"}""", _now);

        var dto = entry.ToDto();

        dto.ActorUserId.Should().BeNull();
        dto.Action.Should().Be("asset.hidden");
        dto.Data.Should().Be("""{"reason":"removed"}""");
    }
}
