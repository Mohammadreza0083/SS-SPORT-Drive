import type { PlayerPageData } from "@/components/viewer/player-page/types";
import { pageCopy } from "@/components/viewer/player-page/copy";
import type { RosterData } from "@/components/viewer/roster/types";
import { SESSION_COOKIE } from "./gate";
import { clientAddress, readCookie } from "./gate-server";
import { pagesPath, scoutPath, streamPath } from "./media-client";
import { watchPath } from "./tracking-client";
import type { PlayerScout } from "./scout";

export { clientAddress };

/**
 * The server side of what a signed-in viewer sees behind `/r/{code}` (backlog V-2b). The `/r/{code}` pages and the
 * photo route call the API's viewer content endpoints from here, with the session cookie's value in a header and the
 * viewer's own address in `X-Forwarded-For`, so the API's per-address limits count each viewer, not the web server.
 */

/** The header the API reads the session token from. */
const SESSION_HEADER = "X-Viewer-Session";

/** Roster cards per request: a link holds at most 20 players. */
const ROSTER_PAGE_SIZE = 20;

/** Videos and documents per request: the API's largest page (decisions §2b). */
const MEDIA_PAGE_SIZE = 100;

/** A player id as the API issues them. */
export const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** One page of a list, as the API answers it. */
type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number };

/** The link overview (`GET /links/{code}/content`). */
type LinkContent = {
  type: "Personal" | "Roster";
  playerCount: number;
  recipient: { name: string; organisation: string | null };
  note: string | null;
  viewerEmail: string;
};

/** A position with its label. */
type Position = { code: string; label: string };

/** A country. */
type Country = { code: string; name: string };

/** A roster card (`GET /links/{code}/players`). */
type LinkPlayerCard = { id: string; name: string; firstName: string; position: Position; nationality: Country; hasPhoto: boolean; videoCount: number };

/** A player page header (`GET /links/{code}/players/{id}`). */
type LinkPlayer = {
  id: string;
  name: string;
  firstName: string;
  position: Position;
  nationality: Country;
  volleyboxUrl: string;
  hasPhoto: boolean;
  stats: { videos: number; minutes: number; documents: number; matches: number };
};

/** A video row. */
type LinkVideo = { id: string; title: string; tag: string; durationSeconds: number };

/** A document row. */
type LinkDocument = { id: string; title: string; pageCount: number };

/** A scout file row. */
type LinkScout = { id: string; title: string };

/** What `/r/{code}` shows: the gate, one player's page, or the roster. */
export type ViewerPage = { kind: "gate" } | { kind: "player"; data: PlayerPageData } | { kind: "roster"; data: RosterData };

/** What `/r/{code}/p/{id}` shows: the gate, the player's page, or 404. */
export type PlayerPageResult = { kind: "gate" } | { kind: "player"; data: PlayerPageData } | { kind: "not-found" };

/** An API answer the page treats specially. */
class ApiStatus extends Error {
  /**
   * @param status - The HTTP status.
   */
  constructor(readonly status: number) {
    super(`The API answered ${status}.`);
  }
}

/**
 * The headers of a call to the API on the viewer's behalf.
 *
 * @param sessionToken - The session cookie's value.
 * @param clientIp - The viewer's address, if known.
 * @returns The headers.
 */
export function apiHeaders(sessionToken: string, clientIp: string | null): Headers {
  const headers = new Headers({ [SESSION_HEADER]: sessionToken });
  if (clientIp) {
    headers.set("X-Forwarded-For", clientIp);
  }

  return headers;
}

/**
 * Reads one viewer content endpoint.
 *
 * @param fetchImpl - The fetch to use.
 * @param url - The full URL.
 * @param headers - The headers.
 * @returns The parsed body.
 * @throws {ApiStatus} For any answer that is not 2xx.
 */
async function getJson<T>(fetchImpl: typeof fetch, url: string, headers: Headers): Promise<T> {
  const response = await fetchImpl(url, { headers, cache: "no-store" });
  if (!response.ok) {
    throw new ApiStatus(response.status);
  }

  return (await response.json()) as T;
}

/**
 * The base of a link's API routes.
 *
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code.
 * @returns The base URL, with no trailing slash.
 */
export function linkBase(apiBaseUrl: string, linkCode: string): string {
  return `${apiBaseUrl.replace(/\/+$/, "")}/links/${encodeURIComponent(linkCode)}`;
}

/**
 * The same-origin URL the browser loads a player's photo from.
 *
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @returns The path.
 */
function photoPath(linkCode: string, playerId: string): string {
  return `/api/links/${encodeURIComponent(linkCode)}/players/${playerId}/photo`;
}

/**
 * Turns "the API no longer accepts the session" into the gate; anything else unexpected is thrown so the page shows
 * its error rather than a misleading gate.
 *
 * @param error - What was thrown.
 * @returns The gate.
 * @throws The error, when it is not a 401.
 */
function gateOnUnauthorized(error: unknown): { kind: "gate" } {
  if (error instanceof ApiStatus && error.status === 401) {
    return { kind: "gate" };
  }

  throw error;
}

/**
 * Builds the player page data from the API's answers.
 *
 * @param linkCode - The link code.
 * @param content - The link overview.
 * @param player - The player header.
 * @param videos - The first page of videos.
 * @param documents - The first page of documents.
 * @param scouts - The first page of scout files.
 * @returns The page data.
 */
function toPlayerPage(linkCode: string, content: LinkContent, player: LinkPlayer, videos: Paged<LinkVideo>, documents: Paged<LinkDocument>, scouts: Paged<LinkScout>): PlayerPageData {
  const { organisation, name } = content.recipient;
  return {
    player: {
      name: player.name,
      firstName: player.firstName,
      position: player.position,
      nationality: player.nationality,
      volleyboxUrl: player.volleyboxUrl,
      photoUrl: player.hasPhoto ? photoPath(linkCode, player.id) : undefined,
    },
    viewerEmail: content.viewerEmail,
    recipient: organisation ? { organisation, name } : { name },
    note: content.note ? { text: content.note, author: pageCopy.noteAuthor } : undefined,
    stats: player.stats,
    // Streams and page images are signed per viewer when they are needed (V-5b): the page carries the route that signs
    // each one, never a stream URL or a page. The PDF itself never reaches the browser.
    videos: videos.items.map((video) => ({
      id: video.id,
      title: video.title,
      tag: video.tag,
      durationSeconds: video.durationSeconds,
      src: "",
      streamPath: streamPath(linkCode, player.id, video.id),
      watchPath: watchPath(linkCode, player.id, video.id),
    })),
    documents: documents.items.map((document) => ({
      id: document.id,
      title: document.title,
      pageCount: document.pageCount,
      pages: [],
      pagesPath: pagesPath(linkCode, player.id, document.id),
    })),
    // V-8: the player's DataVolley files; a report is read when one is opened.
    scouts: scouts.items.map((scout): PlayerScout => ({ id: scout.id, title: scout.title, reportPath: scoutPath(linkCode, player.id, scout.id) })),
  };
}

/**
 * Reads everything one player's page shows, given the link overview.
 *
 * @param fetchImpl - The fetch to use.
 * @param base - The link's API base.
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @param content - The link overview.
 * @param headers - The headers.
 * @returns The page data.
 */
async function readPlayer(fetchImpl: typeof fetch, base: string, linkCode: string, playerId: string, content: LinkContent, headers: Headers): Promise<PlayerPageData> {
  const player = `${base}/players/${playerId}`;
  const [header, videos, documents, scouts] = await Promise.all([
    getJson<LinkPlayer>(fetchImpl, player, headers),
    getJson<Paged<LinkVideo>>(fetchImpl, `${player}/videos?page=1&pageSize=${MEDIA_PAGE_SIZE}`, headers),
    getJson<Paged<LinkDocument>>(fetchImpl, `${player}/documents?page=1&pageSize=${MEDIA_PAGE_SIZE}`, headers),
    getJson<Paged<LinkScout>>(fetchImpl, `${player}/scouts?page=1&pageSize=${MEDIA_PAGE_SIZE}`, headers),
  ]);
  return toPlayerPage(linkCode, content, header, videos, documents, scouts);
}

/**
 * What `/r/{code}` shows to someone with a session cookie: a Personal link opens its player's page, a Roster link its
 * roster. Without a cookie, or when the API no longer accepts the session, the gate.
 *
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code.
 * @param sessionToken - The session cookie's value, if any.
 * @param clientIp - The viewer's address, if known.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns What to render.
 * @throws When the API fails or cannot be reached.
 */
export async function loadViewerPage(
  apiBaseUrl: string,
  linkCode: string,
  sessionToken: string | undefined,
  clientIp: string | null,
  fetchImpl: typeof fetch = fetch,
): Promise<ViewerPage> {
  if (!sessionToken) {
    return { kind: "gate" };
  }

  const base = linkBase(apiBaseUrl, linkCode);
  const headers = apiHeaders(sessionToken, clientIp);
  try {
    const content = await getJson<LinkContent>(fetchImpl, `${base}/content`, headers);
    const players = await getJson<Paged<LinkPlayerCard>>(fetchImpl, `${base}/players?page=1&pageSize=${ROSTER_PAGE_SIZE}`, headers);
    if (content.type === "Personal" && players.items.length > 0) {
      return { kind: "player", data: await readPlayer(fetchImpl, base, linkCode, players.items[0].id, content, headers) };
    }

    return {
      kind: "roster",
      data: {
        sharedWith: content.recipient.organisation ?? content.recipient.name,
        playerCount: content.playerCount,
        players: players.items.map((card) => ({
          id: card.id,
          href: `/r/${encodeURIComponent(linkCode)}/p/${card.id}`,
          name: card.name,
          position: card.position.code,
          nationality: card.nationality.name,
          videos: card.videoCount,
          photoUrl: card.hasPhoto ? photoPath(linkCode, card.id) : undefined,
        })),
      },
    };
  } catch (error) {
    return gateOnUnauthorized(error);
  }
}

/**
 * What `/r/{code}/p/{id}` shows: one player of the link. A player who is not on the link (or an id that is not one)
 * is not found; no session, or one the API no longer accepts, is the gate.
 *
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code.
 * @param playerId - The player id from the URL.
 * @param sessionToken - The session cookie's value, if any.
 * @param clientIp - The viewer's address, if known.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns What to render.
 * @throws When the API fails or cannot be reached.
 */
export async function loadPlayerPage(
  apiBaseUrl: string,
  linkCode: string,
  playerId: string,
  sessionToken: string | undefined,
  clientIp: string | null,
  fetchImpl: typeof fetch = fetch,
): Promise<PlayerPageResult> {
  if (!GUID.test(playerId)) {
    return { kind: "not-found" };
  }

  if (!sessionToken) {
    return { kind: "gate" };
  }

  const base = linkBase(apiBaseUrl, linkCode);
  const headers = apiHeaders(sessionToken, clientIp);
  try {
    const content = await getJson<LinkContent>(fetchImpl, `${base}/content`, headers);
    return { kind: "player", data: await readPlayer(fetchImpl, base, linkCode, playerId, content, headers) };
  } catch (error) {
    if (error instanceof ApiStatus && error.status === 404) {
      return { kind: "not-found" };
    }

    return gateOnUnauthorized(error);
  }
}

/**
 * Forwards `GET /api/links/{code}/players/{id}/photo` to the API with the session cookie as a header, and streams the
 * image back with a private cache. Errors pass through as a bare status.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param playerId - The player id from the URL.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The image, or an empty error answer.
 */
export async function forwardPhoto(request: Request, apiBaseUrl: string, linkCode: string, playerId: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!GUID.test(playerId)) {
    return new Response(null, { status: 404 });
  }

  const session = readCookie(request.headers.get("Cookie"), SESSION_COOKIE);
  if (!session) {
    return new Response(null, { status: 401 });
  }

  let response: Response;
  try {
    response = await fetchImpl(`${linkBase(apiBaseUrl, linkCode)}/players/${playerId}/photo`, {
      headers: apiHeaders(session, clientAddress(request.headers)),
      cache: "no-store",
    });
  } catch {
    return new Response(null, { status: 502 });
  }

  if (!response.ok) {
    return new Response(null, { status: response.status });
  }

  return new Response(response.body, {
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "image/webp",
      "Cache-Control": "private, max-age=300",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
