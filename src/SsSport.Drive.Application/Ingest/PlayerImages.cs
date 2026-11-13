using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Storage;
using SsSport.Drive.Application.Abstractions.Web;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.Ingest;

/// <summary>Makes a published player's QR code (I-8): it opens the home search filled with the player's name.</summary>
/// <param name="PlayerId">The player.</param>
public sealed record RenderPlayerQrCodeCommand(Guid PlayerId) : ICommand;

/// <summary>Handles <see cref="RenderPlayerQrCodeCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="renderer">Draws the QR code.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="webLinks">The site's addresses.</param>
public sealed class RenderPlayerQrCodeCommandHandler(IRepository<Player, Guid> players, IQrCodeRenderer renderer, IObjectStorage storage, IWebLinks webLinks)
    : ICommandHandler<RenderPlayerQrCodeCommand>
{
    /// <summary>Draws and stores the QR code at <c>qr/{playerId}.png</c>; players on the waiting list get none yet.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the code is stored.</returns>
    public async Task Handle(RenderPlayerQrCodeCommand request, CancellationToken cancellationToken)
    {
        if (await players.GetByIdAsync(request.PlayerId, cancellationToken) is not { IsPublished: true, Name: { } name } player)
        {
            return;
        }

        var png = renderer.RenderPng(webLinks.PlayerSearch(name.Value));
        var key = $"qr/{player.Id}.png";
        using var content = new MemoryStream(png, writable: false);
        await storage.PutAsync(key, content, "image/png", cancellationToken);
        player.SetQrCode(StorageKey.Create(key));
    }
}

/// <summary>Removes the background of a player's photo (I-7) and, for a published player, queues a new card.</summary>
/// <param name="PlayerId">The player.</param>
public sealed record CutOutPlayerPhotoCommand(Guid PlayerId) : ICommand;

/// <summary>Handles <see cref="CutOutPlayerPhotoCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="remover">Removes the background.</param>
/// <param name="scheduler">Queues the card.</param>
public sealed class CutOutPlayerPhotoCommandHandler(IRepository<Player, Guid> players, IObjectStorage storage, IBackgroundRemover remover, IIngestScheduler scheduler)
    : ICommandHandler<CutOutPlayerPhotoCommand>
{
    /// <summary>The ending of a processed photo's key.</summary>
    private const string WebpEnding = ".webp";

    /// <summary>Cuts the photo out, from the uploaded original when there is one, and stores it next to the photo.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the cut-out is stored, or there was nothing to do.</returns>
    public async Task Handle(CutOutPlayerPhotoCommand request, CancellationToken cancellationToken)
    {
        if (await players.GetByIdAsync(request.PlayerId, cancellationToken) is not { Photo: { } photo } player)
        {
            return;
        }

        var stem = photo.StorageKey.EndsWith(WebpEnding, StringComparison.Ordinal) ? photo.StorageKey[..^WebpEnding.Length] : photo.StorageKey;
        var source = await storage.GetAsync($"{stem}-original", cancellationToken) ?? await storage.GetAsync(photo.StorageKey, cancellationToken);
        if (source is null)
        {
            return;
        }

        byte[]? cutout;
        await using (source.Content)
        {
            cutout = await remover.CutOutAsync(source.Content, cancellationToken);
        }

        if (cutout is null)
        {
            return;
        }

        var key = $"{stem}-cutout{WebpEnding}";
        using var content = new MemoryStream(cutout, writable: false);
        await storage.PutAsync(key, content, "image/webp", cancellationToken);
        player.SetPhotoCutout(StorageKey.Create(key));
        if (player.IsPublished)
        {
            await scheduler.EnqueueAsync(IngestStep.RenderCard, player.Id, cancellationToken);
        }
    }
}

/// <summary>Makes a published player's card image (I-5).</summary>
/// <param name="PlayerId">The player.</param>
public sealed record RenderPlayerCardCommand(Guid PlayerId) : ICommand;

/// <summary>Handles <see cref="RenderPlayerCardCommand"/>.</summary>
/// <param name="players">The players.</param>
/// <param name="storage">The media bucket.</param>
/// <param name="renderer">Draws the card.</param>
/// <param name="timeProvider">The clock; its milliseconds version the key.</param>
public sealed class RenderPlayerCardCommandHandler(IRepository<Player, Guid> players, IObjectStorage storage, IPlayerCardRenderer renderer, TimeProvider timeProvider)
    : ICommandHandler<RenderPlayerCardCommand>
{
    /// <summary>Draws the card from the cut-out (or the photo) and stores it under a new versioned key.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the card is stored; players on the waiting list get none yet.</returns>
    public async Task Handle(RenderPlayerCardCommand request, CancellationToken cancellationToken)
    {
        if (await players.GetByIdAsync(request.PlayerId, cancellationToken) is not { IsPublished: true, Name: { } name } player)
        {
            return;
        }

        var facts = new PlayerCardFacts(
            name.Value,
            player.Nationality?.CountryName ?? string.Empty,
            [.. player.Positions?.Items.Select(position => position.Code()) ?? []],
            player.Category.ToString(),
            await ReadPhotoAsync(player, cancellationToken));
        var key = $"players/{player.Id}/card-{timeProvider.GetUtcNow().ToUnixTimeMilliseconds()}.png";
        using var content = new MemoryStream(renderer.RenderPng(facts), writable: false);
        await storage.PutAsync(key, content, "image/png", cancellationToken);
        player.SetCard(StorageKey.Create(key));
    }

    /// <summary>Reads the cut-out when there is one, else the photo.</summary>
    /// <param name="player">The player.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The image bytes, or <see langword="null"/> when there is none in storage.</returns>
    private async Task<byte[]?> ReadPhotoAsync(Player player, CancellationToken cancellationToken)
    {
        foreach (var key in new[] { player.PhotoCutout?.Value, player.Photo?.StorageKey }.OfType<string>())
        {
            if (await storage.GetAsync(key, cancellationToken) is { } stored)
            {
                await using (stored.Content)
                {
                    using var copy = new MemoryStream();
                    await stored.Content.CopyToAsync(copy, cancellationToken);
                    return copy.ToArray();
                }
            }
        }

        return null;
    }
}
