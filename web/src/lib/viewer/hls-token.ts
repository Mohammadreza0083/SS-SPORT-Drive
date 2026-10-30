/**
 * Edge HLS tokens on the player side (backlog V-5a). Tokens sit in the URL path, `/hls/{token}/{video folder}/…`,
 * and live 30 minutes; the player renews them in the background while the viewer watches.
 */

/** How often the player renews its token while the viewer watches: well inside the 30-minute lifetime. */
export const REFRESH_EVERY_MS = 20 * 60 * 1000;

/** How often the player checks whether a renewal is due. */
export const REFRESH_CHECK_MS = 60 * 1000;

/** Matches the token segment of an edge HLS URL. */
const TOKEN_SEGMENT = /\/hls\/([^/]+)\//;

/**
 * Reads the token from an edge HLS URL.
 *
 * @param url - An absolute or site-relative URL.
 * @returns The token, or null when the URL is not an edge HLS URL.
 */
export function tokenFromUrl(url: string): string | null {
  return TOKEN_SEGMENT.exec(url)?.[1] ?? null;
}

/**
 * Puts a newer token into an edge HLS URL, so playlists and segments fetched after a renewal carry it.
 *
 * @param url - The URL hls.js is about to request.
 * @param token - The latest token, or null when there is none.
 * @returns The URL with the latest token, or unchanged.
 */
export function swapToken(url: string, token: string | null): string {
  return token ? url.replace(TOKEN_SEGMENT, `/hls/${token}/`) : url;
}
