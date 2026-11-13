using System.Text;
using NSubstitute;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Web;
using SsSport.Drive.Application.Ingest;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Ingest;

/// <summary>
/// The images made from a player:
/// <list type="bullet">
/// <item>I-8: the QR code, a PNG at <c>qr/{playerId}.png</c> that opens the home search prefilled with the name
/// (decisions §4a), made for published players only;</item>
/// <item>I-7: the cut-out photo, made from the uploaded original next to the photo (<c>…-cutout.webp</c>); when no
/// background-removal model is configured nothing is made and the viewer keeps its fallback;</item>
/// <item>I-5: the player card, a PNG under a new versioned key, made for published players from the cut-out when there
/// is one.</item>
/// </list>
/// </summary>
public sealed class PlayerImagesTests
{
    /// <summary>The bytes the stand-in renderers answer.</summary>
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>The players.</summary>
    private readonly IRepository<Player, Guid> _players = Substitute.For<IRepository<Player, Guid>>();

    /// <summary>The media bucket.</summary>
    private readonly FakeObjectStorage _storage = new();

    /// <summary>The QR renderer.</summary>
    private readonly IQrCodeRenderer _qr = Substitute.For<IQrCodeRenderer>();

    /// <summary>The site's links.</summary>
    private readonly IWebLinks _links = Substitute.For<IWebLinks>();

    /// <summary>The background remover.</summary>
    private readonly IBackgroundRemover _remover = Substitute.For<IBackgroundRemover>();

    /// <summary>The card renderer.</summary>
    private readonly IPlayerCardRenderer _cards = Substitute.For<IPlayerCardRenderer>();

    /// <summary>The scheduler.</summary>
    private readonly IIngestScheduler _scheduler = Substitute.For<IIngestScheduler>();

    /// <summary>Wires the stand-ins.</summary>
    public PlayerImagesTests()
    {
        _qr.RenderPng(Arg.Any<Uri>()).Returns(_png);
        _cards.RenderPng(Arg.Any<PlayerCardFacts>()).Returns(_png);
        _links.PlayerSearch(Arg.Any<string>()).Returns(call => new Uri($"https://drive.ss-sport.co/?q={Uri.EscapeDataString(call.Arg<string>())}"));
    }

    /// <summary>A published player's QR code opens the home search with its name and is stored under its fixed key.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_published_player_gets_a_qr_code()
    {
        var player = Stored(IngestData.PublishedPlayer("Ali Hoshmand"));

        await QrHandler().Handle(new RenderPlayerQrCodeCommand(player.Id), TestContext.Current.CancellationToken);

        _qr.Received(1).RenderPng(new Uri("https://drive.ss-sport.co/?q=Ali%20Hoshmand"));
        var key = $"qr/{player.Id}.png";
        _storage.Bytes(key).Should().Equal(_png);
        _storage.ContentType(key).Should().Be("image/png");
        player.QrCode!.Value.Should().Be(key);
    }

    /// <summary>A player on the waiting list gets no QR code yet.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_waiting_player_gets_no_qr_code()
    {
        var player = Stored(IngestData.WaitingPlayer());

        await QrHandler().Handle(new RenderPlayerQrCodeCommand(player.Id), TestContext.Current.CancellationToken);

        _storage.Keys.Should().BeEmpty();
        player.QrCode.Should().BeNull();
    }

    /// <summary>The cut-out is made from the uploaded original and stored next to the photo; a published player's card is then remade.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_cutout_is_made_from_the_original()
    {
        var player = Stored(IngestData.PublishedPlayer());
        var photo = player.Photo!.StorageKey;
        var original = photo.Replace(".webp", "-original", StringComparison.Ordinal);
        _storage.Seed(original, Encoding.UTF8.GetBytes("original"));
        _storage.Seed(photo, Encoding.UTF8.GetBytes("webp"));
        byte[]? given = null;
        _remover.CutOutAsync(Arg.Do<Stream>(stream => given = Read(stream)), Arg.Any<CancellationToken>()).Returns(Encoding.UTF8.GetBytes("cutout"));

        await CutOutHandler().Handle(new CutOutPlayerPhotoCommand(player.Id), TestContext.Current.CancellationToken);

        var cutout = photo.Replace(".webp", "-cutout.webp", StringComparison.Ordinal);
        given.Should().Equal(Encoding.UTF8.GetBytes("original"));
        _storage.Bytes(cutout).Should().Equal(Encoding.UTF8.GetBytes("cutout"));
        _storage.ContentType(cutout).Should().Be("image/webp");
        player.PhotoCutout!.Value.Should().Be(cutout);
        await _scheduler.Received(1).EnqueueAsync(IngestStep.RenderCard, player.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>A photo without its original (e.g. imported) is cut out from the WebP itself.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Without_an_original_the_webp_is_cut_out()
    {
        var player = Stored(IngestData.WaitingPlayer());
        _storage.Seed(player.Photo!.StorageKey, Encoding.UTF8.GetBytes("webp"));
        byte[]? given = null;
        _remover.CutOutAsync(Arg.Do<Stream>(stream => given = Read(stream)), Arg.Any<CancellationToken>()).Returns(Encoding.UTF8.GetBytes("cutout"));

        await CutOutHandler().Handle(new CutOutPlayerPhotoCommand(player.Id), TestContext.Current.CancellationToken);

        given.Should().Equal(Encoding.UTF8.GetBytes("webp"));
        player.PhotoCutout.Should().NotBeNull();
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Without a configured model the remover answers nothing, and nothing changes.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Without_a_model_nothing_is_cut_out()
    {
        var player = Stored(IngestData.PublishedPlayer());
        _storage.Seed(player.Photo!.StorageKey, Encoding.UTF8.GetBytes("webp"));
        _remover.CutOutAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        await CutOutHandler().Handle(new CutOutPlayerPhotoCommand(player.Id), TestContext.Current.CancellationToken);

        _storage.Writes.Should().BeEmpty();
        player.PhotoCutout.Should().BeNull();
        await _scheduler.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The card shows the player's details and the cut-out, and is stored under a new versioned key.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_published_player_gets_a_card_from_the_cutout()
    {
        var player = Stored(IngestData.PublishedPlayer("Ali Hoshmand"));
        var cutout = player.Photo!.StorageKey.Replace(".webp", "-cutout.webp", StringComparison.Ordinal);
        player.SetPhotoCutout(StorageKey.Create(cutout));
        _storage.Seed(cutout, Encoding.UTF8.GetBytes("cutout"));
        _storage.Seed(player.Photo.StorageKey, Encoding.UTF8.GetBytes("webp"));

        await CardHandler().Handle(new RenderPlayerCardCommand(player.Id), TestContext.Current.CancellationToken);

        _cards.Received(1).RenderPng(Arg.Is<PlayerCardFacts>(facts =>
            facts.Name == "Ali Hoshmand"
            && facts.Nationality == "Iran"
            && facts.Positions.SequenceEqual(new[] { "OH" })
            && facts.Category == "Men"
            && facts.Photo != null && facts.Photo.SequenceEqual(Encoding.UTF8.GetBytes("cutout"))));
        var key = $"players/{player.Id}/card-{IngestData.Now.ToUnixTimeMilliseconds()}.png";
        _storage.ContentType(key).Should().Be("image/png");
        player.Card!.Value.Should().Be(key);
    }

    /// <summary>Without a cut-out the card uses the photo.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Without_a_cutout_the_card_uses_the_photo()
    {
        var player = Stored(IngestData.PublishedPlayer());
        _storage.Seed(player.Photo!.StorageKey, Encoding.UTF8.GetBytes("webp"));

        await CardHandler().Handle(new RenderPlayerCardCommand(player.Id), TestContext.Current.CancellationToken);

        _cards.Received(1).RenderPng(Arg.Is<PlayerCardFacts>(facts => facts.Photo != null && facts.Photo.SequenceEqual(Encoding.UTF8.GetBytes("webp"))));
    }

    /// <summary>A player on the waiting list gets no card yet.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_waiting_player_gets_no_card()
    {
        var player = Stored(IngestData.WaitingPlayer());

        await CardHandler().Handle(new RenderPlayerCardCommand(player.Id), TestContext.Current.CancellationToken);

        _cards.DidNotReceiveWithAnyArgs().RenderPng(default!);
        player.Card.Should().BeNull();
    }

    /// <summary>Makes the players repository answer a player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The player.</returns>
    private Player Stored(Player player)
    {
        _players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        return player;
    }

    /// <summary>Builds the QR handler.</summary>
    /// <returns>The handler.</returns>
    private RenderPlayerQrCodeCommandHandler QrHandler() => new(_players, _qr, _storage, _links);

    /// <summary>Builds the cut-out handler.</summary>
    /// <returns>The handler.</returns>
    private CutOutPlayerPhotoCommandHandler CutOutHandler() => new(_players, _storage, _remover, _scheduler);

    /// <summary>Builds the card handler.</summary>
    /// <returns>The handler.</returns>
    private RenderPlayerCardCommandHandler CardHandler() => new(_players, _storage, _cards, new StuckClock(IngestData.Now));

    /// <summary>Reads a stream to the end.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
