using System.Globalization;
using Ardalis.Specification;
using FluentValidation;
using SsSport.Drive.Application.Abstractions.Media;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Application.Abstractions.Security;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Validation;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Links;

namespace SsSport.Drive.Application.Links;

/// <summary>
/// Where ingest (I-3, I-4) writes a video's HLS files and a document's page images in object storage, and so where the
/// edge tokens point (V-5b). The version is the asset's content version, so a replaced file never mixes with old tokens.
/// </summary>
public static class MediaFolders
{
    /// <summary>The master playlist's file name inside a video's HLS folder.</summary>
    public const string MasterPlaylist = "master.m3u8";

    /// <summary>The folder of a video's HLS renditions.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="videoId">The video.</param>
    /// <param name="version">The video's content version.</param>
    /// <returns>The folder, e.g. <c>players/{player}/videos/{video}/v1/hls</c> (no slash at either end).</returns>
    public static string VideoHls(Guid playerId, Guid videoId, int version) =>
        $"players/{playerId}/videos/{videoId}/v{version.ToString(CultureInfo.InvariantCulture)}/hls";

    /// <summary>The folder of a document's page images, named <c>1.webp</c>, <c>2.webp</c>, and so on.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="documentId">The document.</param>
    /// <param name="version">The document's content version.</param>
    /// <returns>The folder, e.g. <c>players/{player}/documents/{document}/v1/pages</c> (no slash at either end).</returns>
    public static string DocumentPages(Guid playerId, Guid documentId, int version) =>
        $"players/{playerId}/documents/{documentId}/v{version.ToString(CultureInfo.InvariantCulture)}/pages";
}

/// <summary>A video's signed stream.</summary>
public sealed record VideoStreamDto
{
    /// <summary>Gets the site-relative URL of the master playlist on the edge, e.g. <c>/hls/{token}/{folder}/master.m3u8</c>.</summary>
    public string PlaylistUrl { get; init; } = string.Empty;

    /// <summary>Gets when the token stops working.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>A document's signed page images.</summary>
public sealed record DocumentPagesDto
{
    /// <summary>Gets one site-relative edge URL per page, in order, e.g. <c>/pages/{token}/{folder}/1.webp</c>.</summary>
    public IReadOnlyList<string> Pages { get; init; } = [];

    /// <summary>Gets when the token stops working.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>One ready, visible video of a player, read untracked.</summary>
public sealed class ViewerVideoSpec : Specification<Asset>, ISingleResultSpecification<Asset>
{
    /// <summary>Initializes a new instance of the <see cref="ViewerVideoSpec"/> class.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="videoId">The video.</param>
    public ViewerVideoSpec(Guid playerId, Guid videoId) =>
        Query.Where(ViewerAssetConditions.Watchable(playerId, AssetKind.Video)).Where(asset => asset.Id == videoId).AsNoTracking();
}

/// <summary>One ready, visible document (PDF or image) of a player, read untracked.</summary>
public sealed class ViewerDocumentSpec : Specification<Asset>, ISingleResultSpecification<Asset>
{
    /// <summary>Initializes a new instance of the <see cref="ViewerDocumentSpec"/> class.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="documentId">The document.</param>
    public ViewerDocumentSpec(Guid playerId, Guid documentId) =>
        Query.Where(ViewerAssetConditions.Watchable(playerId, AssetKind.Document)).Where(asset => asset.Id == documentId).AsNoTracking();
}

/// <summary>Signs a video's stream for the viewer's session (<c>GET /links/{code}/players/{id}/videos/{videoId}/stream</c>).</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any; the token is bound to it.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="VideoId">The video.</param>
public sealed record GetVideoStreamQuery(string Code, string? SessionToken, Guid PlayerId, Guid VideoId) : IQuery<VideoStreamDto>;

/// <summary>Handles <see cref="GetVideoStreamQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="assets">The assets.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="issuer">Signs the edge token.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetVideoStreamQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<Asset, Guid> assets,
    IViewerSecrets secrets,
    IHlsTokenIssuer issuer,
    TimeProvider timeProvider) : IQueryHandler<GetVideoStreamQuery, VideoStreamDto>
{
    /// <inheritdoc />
    public async Task<VideoStreamDto> Handle(GetVideoStreamQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);
        var video = await assets.FirstOrDefaultAsync(new ViewerVideoSpec(request.PlayerId, request.VideoId), cancellationToken)
            ?? throw new NotFoundException("Video");
        issuer.EnsureEnabled();

        var access = issuer.Issue(MediaFolders.VideoHls(request.PlayerId, video.Id, video.Version), request.SessionToken!);
        return new VideoStreamDto { PlaylistUrl = access.PlaylistUrl, ExpiresAt = access.ExpiresAt };
    }
}

/// <summary>Signs a document's page images for the viewer's session (<c>GET /links/{code}/players/{id}/documents/{documentId}/pages</c>).</summary>
/// <param name="Code">The link code.</param>
/// <param name="SessionToken">The session token, if any; the token is bound to it.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="DocumentId">The document.</param>
public sealed record GetDocumentPagesQuery(string Code, string? SessionToken, Guid PlayerId, Guid DocumentId) : IQuery<DocumentPagesDto>;

/// <summary>Handles <see cref="GetDocumentPagesQuery"/>.</summary>
/// <param name="links">The links.</param>
/// <param name="sessions">The viewer sessions.</param>
/// <param name="assets">The assets.</param>
/// <param name="secrets">Hashes tokens.</param>
/// <param name="issuer">Signs the edge token.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetDocumentPagesQueryHandler(
    IRepository<ShareLink, Guid> links,
    IRepository<ViewerSession, Guid> sessions,
    IRepository<Asset, Guid> assets,
    IViewerSecrets secrets,
    IHlsTokenIssuer issuer,
    TimeProvider timeProvider) : IQueryHandler<GetDocumentPagesQuery, DocumentPagesDto>
{
    /// <inheritdoc />
    public async Task<DocumentPagesDto> Handle(GetDocumentPagesQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await links.OpenLinkAsync(request.Code, now, cancellationToken);
        await sessions.RequireSessionAsync(secrets, link, request.SessionToken, now, cancellationToken);
        link.EnsureShows(request.PlayerId);
        var document = await assets.FirstOrDefaultAsync(new ViewerDocumentSpec(request.PlayerId, request.DocumentId), cancellationToken)
            ?? throw new NotFoundException("Document");
        issuer.EnsureEnabled();

        var folder = MediaFolders.DocumentPages(request.PlayerId, document.Id, document.Version);
        var access = issuer.Issue(folder, request.SessionToken!);
        var pages = Enumerable.Range(1, document.PageCount ?? 0)
            .Select(number => $"/pages/{access.Token}/{folder}/{number.ToString(CultureInfo.InvariantCulture)}.webp")
            .ToList();
        return new DocumentPagesDto { Pages = pages, ExpiresAt = access.ExpiresAt };
    }
}

/// <summary>The check the signing handlers share.</summary>
public static class HlsTokenIssuerExtensions
{
    /// <summary>The code clients see when no edge signing key is configured.</summary>
    public const string StreamingOffCode = "media.streaming_off";

    /// <summary>Makes sure tokens can be signed.</summary>
    /// <param name="issuer">The issuer.</param>
    /// <exception cref="ServiceUnavailableException"><c>media.streaming_off</c>: <c>Edge__HlsSigningKey</c> is empty.</exception>
    public static void EnsureEnabled(this IHlsTokenIssuer issuer)
    {
        if (!issuer.IsEnabled)
        {
            throw new ServiceUnavailableException(StreamingOffCode, "Video streaming is not set up.");
        }
    }
}

/// <summary>Validates <see cref="GetDocumentPagesQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetDocumentPagesQueryValidator : AbstractValidator<GetDocumentPagesQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetDocumentPagesQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
        RuleFor(r => r.PlayerId).ValidId().OverridePropertyName("playerId");
        RuleFor(r => r.DocumentId).ValidId().OverridePropertyName("documentId");
    }
}

/// <summary>Validates <see cref="GetVideoStreamQuery"/> on the server (decisions §1b rule 3); malformed input is a 400.</summary>
public sealed class GetVideoStreamQueryValidator : AbstractValidator<GetVideoStreamQuery>
{
    /// <summary>Builds the rules.</summary>
    public GetVideoStreamQueryValidator()
    {
        RuleFor(r => r.Code).BoundedCode().OverridePropertyName("code");
        RuleFor(r => r.SessionToken).BoundedToken().OverridePropertyName("sessionToken");
        RuleFor(r => r.PlayerId).ValidId().OverridePropertyName("playerId");
        RuleFor(r => r.VideoId).ValidId().OverridePropertyName("videoId");
    }
}
