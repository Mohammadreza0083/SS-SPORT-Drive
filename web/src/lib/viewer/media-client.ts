import type { ScoutReport } from "./scout";

/**
 * The browser side of signed media (backlog V-5b) and scout reports (V-8): the same-origin routes, and small fetch
 * helpers the player, the document viewer and the scout view call. They send the session cookie (same origin), never
 * cache, and throw on any answer that is not 2xx so the caller can retry or show its error.
 */

/** A failed request to one of the media routes. */
export class MediaRequestError extends Error {
  /**
   * @param status - The HTTP status the route answered.
   */
  constructor(readonly status: number) {
    super(`The media route answered ${status}.`);
    this.name = "MediaRequestError";
  }
}

/**
 * The base of a player's routes on this site.
 *
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @returns The path, with the code encoded.
 */
function playerBase(linkCode: string, playerId: string): string {
  return `/api/links/${encodeURIComponent(linkCode)}/players/${playerId}`;
}

/**
 * The route that signs a video's stream.
 *
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @param videoId - The video id.
 * @returns The same-origin path.
 */
export function streamPath(linkCode: string, playerId: string, videoId: string): string {
  return `${playerBase(linkCode, playerId)}/videos/${videoId}/stream`;
}

/**
 * The route that signs a document's page images.
 *
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @param documentId - The document id.
 * @returns The same-origin path.
 */
export function pagesPath(linkCode: string, playerId: string, documentId: string): string {
  return `${playerBase(linkCode, playerId)}/documents/${documentId}/pages`;
}

/**
 * The route that reads one scout file as a report.
 *
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @param scoutId - The scout file id.
 * @returns The same-origin path.
 */
export function scoutPath(linkCode: string, playerId: string, scoutId: string): string {
  return `${playerBase(linkCode, playerId)}/scouts/${scoutId}`;
}

/**
 * The route that lists a player's scout files.
 *
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @returns The same-origin path.
 */
export function scoutsPath(linkCode: string, playerId: string): string {
  return `${playerBase(linkCode, playerId)}/scouts`;
}

/**
 * Reads JSON from a same-origin media route.
 *
 * @param path - The route.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The parsed body.
 * @throws {MediaRequestError} For any answer that is not 2xx.
 */
async function getJson<T>(path: string, fetchImpl: typeof fetch): Promise<T> {
  const response = await fetchImpl(path, { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) {
    throw new MediaRequestError(response.status);
  }
  return (await response.json()) as T;
}

/**
 * Asks for a video's signed master playlist URL. It is the edge's `/hls/…` path and nothing else: a URL anywhere else
 * is refused, so a wrong answer can never point the player off the edge.
 *
 * @param path - The video's stream route.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The playlist URL, site-relative.
 * @throws {MediaRequestError} When the route refuses.
 * @throws {Error} When the URL is not an edge HLS URL.
 */
export async function fetchStreamUrl(path: string, fetchImpl: typeof fetch = fetch): Promise<string> {
  const { playlistUrl } = await getJson<{ playlistUrl?: string }>(path, fetchImpl);
  if (typeof playlistUrl !== "string" || !playlistUrl.startsWith("/hls/")) {
    throw new Error("The stream URL is not on the edge.");
  }
  return playlistUrl;
}

/**
 * Asks for a document's signed page image URLs, in order. Anything that is not the edge's `/pages/…` path is dropped.
 *
 * @param path - The document's pages route.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The page URLs, site-relative.
 * @throws {MediaRequestError} When the route refuses.
 */
export async function fetchPages(path: string, fetchImpl: typeof fetch = fetch): Promise<string[]> {
  const { pages } = await getJson<{ pages?: string[] }>(path, fetchImpl);
  return (pages ?? []).filter((page) => typeof page === "string" && page.startsWith("/pages/"));
}

/**
 * Reads a scout file as a report.
 *
 * @param path - The scout file's report route.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The report.
 * @throws {MediaRequestError} When the route refuses; 422 means the file is not a readable scout file.
 */
export function fetchScoutReport(path: string, fetchImpl: typeof fetch = fetch): Promise<ScoutReport> {
  return getJson<ScoutReport>(path, fetchImpl);
}
