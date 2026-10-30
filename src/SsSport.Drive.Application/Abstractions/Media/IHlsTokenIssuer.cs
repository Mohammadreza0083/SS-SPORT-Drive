namespace SsSport.Drive.Application.Abstractions.Media;

/// <summary>
/// Issues short-lived, signed tokens that let one viewer stream one video's HLS files through the Cloudflare edge
/// (backlog V-5a). The edge Worker checks the signature, the expiry, the video folder and the viewer's session cookie,
/// so a copied playlist URL does not play anywhere else.
/// </summary>
public interface IHlsTokenIssuer
{
    /// <summary>
    /// Signs a token for the HLS folder of one video, bound to the viewer's session.
    /// </summary>
    /// <param name="pathPrefix">The video's HLS folder in object storage, e.g. <c>players/{id}/videos/{id}/hls</c> (no leading or trailing slash).</param>
    /// <param name="sessionCookie">The value of the viewer's session cookie; only a hash of it goes into the token.</param>
    /// <returns>The token, the master playlist URL that carries it, and when it expires.</returns>
    /// <exception cref="ArgumentException">The path prefix is not a plain object key, or the session is empty.</exception>
    HlsAccess Issue(string pathPrefix, string sessionCookie);
}

/// <summary>Access to one video's HLS stream through the edge.</summary>
/// <param name="Token">The signed token.</param>
/// <param name="PlaylistUrl">The site-relative URL of the master playlist, e.g. <c>/hls/{token}/{prefix}/master.m3u8</c>.</param>
/// <param name="ExpiresAt">When the token stops working (30 minutes after issue).</param>
public sealed record HlsAccess(string Token, string PlaylistUrl, DateTimeOffset ExpiresAt);
