import { formatDuration } from "@/components/viewer/player-page/format";
import { POSITION_FILTERS } from "@/components/viewer/public/search";
import type { HomeData, LockedData, PublicPlayer, PublicViewer } from "@/components/viewer/public/types";
import { clientAddress, isSameOrigin, problem, readCookie } from "./gate-server";

/**
 * The server side of the public pages (backlog P-8): the home page and `/players/{slug}` read the API's `/public/…`
 * endpoints from here, the same-origin `/api/public/…` routes forward the request-access flow, and
 * `/api/viewer/sign-out` ends a signed-in viewer's session through the API's `/auth/viewer/*` endpoints (P-5). The viewer's token lives only in the `HttpOnly` cookie
 * {@link ACCOUNT_COOKIE} and travels to the API as `X-Viewer-Account`.
 */

/** The cookie holding a signed-in viewer's session token (set by the sign-in of P-5). */
export const ACCOUNT_COOKIE = "__Host-ssd_acct";

/** How many players the home page shows per page (decisions §11, P-8 D2). */
export const HOME_PAGE_SIZE = 24;

/** The most videos or documents the locked page asks for (the largest page the API serves). */
const MEDIA_PAGE_SIZE = 100;

/** The header the API reads the signed-in viewer's token from. */
const ACCOUNT_HEADER = "X-Viewer-Account";

/** A slug as the API creates them; anything else cannot be a player and never reaches the API. */
const SLUG = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** The longest slug a player can have. */
const MAX_SLUG_LENGTH = 120;

/** The request-access routes the browser may forward. */
export type PublicAction = "access-code" | "access-requests";

/** A page of the API. */
type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number };

/** A card of `GET /public/players`. */
type ApiCard = {
  slug: string;
  name: string;
  position: { code: string; label: string };
  nationality: { code: string; name: string };
  videos: number;
  documents: number;
  hasPhoto: boolean;
};

/** The answer of `GET /public/players/{slug}`. */
type ApiPlayer = ApiCard & {
  firstName: string;
  volleyboxUrl: string;
  stats: { videos: number; minutes: number; documents: number };
};

/** What the home page reads from the address bar. */
export type HomeParams = { q?: string; position?: string; page?: string };

/** What the locked page needs, or why there is nothing to show. */
export type LockedPageResult = { kind: "locked"; data: LockedData } | { kind: "not-found" };

/** The API answered with a status the caller must handle. */
class ApiStatus extends Error {
  /**
   * Creates the error.
   *
   * @param status - The HTTP status.
   */
  constructor(readonly status: number) {
    super(`The API answered ${status}.`);
  }
}

/**
 * Joins the API's address and a path.
 *
 * @param apiBaseUrl - Where the API is.
 * @param path - The path, starting with a slash.
 * @returns The full address.
 */
export function apiUrl(apiBaseUrl: string, path: string): string {
  return `${apiBaseUrl.replace(/\/+$/, "")}${path}`;
}

/**
 * Builds the headers of a read: the viewer's address, so the API's per-address limits count each visitor.
 *
 * @param clientIp - The visitor's address, if known.
 * @returns The headers.
 */
function readHeaders(clientIp: string | null): Headers {
  const headers = new Headers();
  if (clientIp) {
    headers.set("X-Forwarded-For", clientIp);
  }

  return headers;
}

/**
 * Reads JSON from the API.
 *
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @param url - The address.
 * @param headers - The headers to send.
 * @returns The parsed body.
 * @throws {ApiStatus} When the API does not answer 2xx.
 */
async function getJson<T>(fetchImpl: typeof fetch, url: string, headers: Headers): Promise<T> {
  const response = await fetchImpl(url, { headers, cache: "no-store" });
  if (!response.ok) {
    throw new ApiStatus(response.status);
  }

  return (await response.json()) as T;
}

/**
 * Whether a slug could belong to a player.
 *
 * @param slug - The slug from the address.
 * @returns True for the shapes the API creates.
 */
function isSlug(slug: string): boolean {
  return slug.length <= MAX_SLUG_LENGTH && SLUG.test(slug);
}

/**
 * The same-origin address of a player's photo.
 *
 * @param slug - The player's slug.
 * @returns The path.
 */
function photoPath(slug: string): string {
  return `/api/public/players/${slug}/photo`;
}

/**
 * Turns an API card into what the home page shows.
 *
 * @param card - The card.
 * @returns The player.
 */
function toPlayer(card: ApiCard): PublicPlayer {
  return {
    slug: card.slug,
    name: card.name,
    position: card.position.code,
    nationality: card.nationality.name,
    videos: card.videos,
    documents: card.documents,
    ...(card.hasPhoto ? { photoUrl: photoPath(card.slug) } : {}),
  };
}

/**
 * Reads who is signed in. A missing, ended or unreadable session is just a visitor: the nav is a nicety.
 *
 * @param apiBaseUrl - Where the API is.
 * @param accountToken - The account cookie's value, if any.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The viewer, or null.
 */
async function readViewer(apiBaseUrl: string, accountToken: string | undefined, fetchImpl: typeof fetch): Promise<PublicViewer> {
  if (!accountToken) {
    return null;
  }

  try {
    const headers = new Headers({ [ACCOUNT_HEADER]: accountToken });
    const viewer = await getJson<{ email: string }>(fetchImpl, apiUrl(apiBaseUrl, "/auth/viewer/me"), headers);
    return { email: viewer.email };
  } catch {
    return null;
  }
}

/**
 * What the home page shows: one page of the search, filtered in SQL by the API, and who is signed in.
 *
 * @param apiBaseUrl - Where the API is.
 * @param params - `?q=`, `?position=` and `?page=` from the address.
 * @param accountToken - The account cookie's value, if any.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @param clientIp - The visitor's address, passed on so the API's limits count each visitor.
 * @returns The home page data.
 * @throws When the API fails or cannot be reached, so the page shows its error.
 */
export async function loadHome(
  apiBaseUrl: string,
  params: HomeParams,
  accountToken: string | undefined,
  fetchImpl: typeof fetch = fetch,
  clientIp: string | null = null,
): Promise<HomeData> {
  const query = (params.q ?? "").trim();
  const position = POSITION_FILTERS.find((label) => label === (params.position ?? "").toUpperCase()) ?? "All";
  const wanted = Number.parseInt(params.page ?? "", 10);
  const page = Number.isFinite(wanted) && wanted >= 1 ? wanted : 1;

  const search = new URLSearchParams({ page: String(page), pageSize: String(HOME_PAGE_SIZE) });
  if (query) {
    search.set("search", query);
  }

  if (position !== "All") {
    search.set("position", position);
  }

  const [result, viewer] = await Promise.all([
    getJson<Paged<ApiCard>>(fetchImpl, apiUrl(apiBaseUrl, `/public/players?${search.toString()}`), readHeaders(clientIp)),
    readViewer(apiBaseUrl, accountToken, fetchImpl),
  ]);

  return {
    players: result.items.map(toPlayer),
    viewer,
    query,
    position,
    page: result.page,
    totalCount: result.totalCount,
    totalPages: result.totalPages,
  };
}

/**
 * What `/players/{slug}` shows: the locked page of a published player. Unknown, unpublished and malformed slugs are
 * not found.
 *
 * @param apiBaseUrl - Where the API is.
 * @param slug - The slug from the address.
 * @param accountToken - The account cookie's value, if any.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @param clientIp - The visitor's address, if known.
 * @returns What to render.
 * @throws When the API fails or cannot be reached, so the page shows its error.
 */
export async function loadLockedPage(
  apiBaseUrl: string,
  slug: string,
  accountToken: string | undefined,
  fetchImpl: typeof fetch = fetch,
  clientIp: string | null = null,
): Promise<LockedPageResult> {
  if (!isSlug(slug)) {
    return { kind: "not-found" };
  }

  const base = `/public/players/${slug}`;
  const headers = readHeaders(clientIp);
  let player: ApiPlayer;
  try {
    player = await getJson<ApiPlayer>(fetchImpl, apiUrl(apiBaseUrl, base), headers);
  } catch (error) {
    if (error instanceof ApiStatus && error.status === 404) {
      return { kind: "not-found" };
    }

    throw error;
  }

  const [videos, documents, viewer] = await Promise.all([
    getJson<Paged<{ title: string; tag: string; durationSeconds: number }>>(fetchImpl, apiUrl(apiBaseUrl, `${base}/videos?page=1&pageSize=${MEDIA_PAGE_SIZE}`), headers),
    getJson<Paged<{ title: string; pageCount: number }>>(fetchImpl, apiUrl(apiBaseUrl, `${base}/documents?page=1&pageSize=${MEDIA_PAGE_SIZE}`), headers),
    readViewer(apiBaseUrl, accountToken, fetchImpl),
  ]);

  return {
    kind: "locked",
    data: {
      player: {
        ...toPlayer({ ...player, videos: player.stats.videos, documents: player.stats.documents }),
        minutes: player.stats.minutes,
        volleyboxUrl: player.volleyboxUrl || undefined,
      },
      videos: videos.items.map((video) => ({ title: video.title, tag: video.tag, duration: formatDuration(video.durationSeconds) })),
      documents: documents.items.map((document) => ({ title: document.title, pages: document.pageCount })),
      viewer,
    },
  };
}

/**
 * Forwards a request-access route to the API (`POST /api/public/access-code|access-requests`). Cross-origin and
 * non-JSON requests are refused before anything is sent. The account cookie goes to the API as a header, never in the
 * body; the API's answer, its `Retry-After` and its ProblemDetails pass through unchanged.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param action - Which route.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The answer for the browser.
 */
export async function forwardPublic(request: Request, apiBaseUrl: string, action: PublicAction, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) {
    return problem(415, "Send JSON.");
  }

  const headers = new Headers({ "Content-Type": "application/json" });
  const clientIp = clientAddress(request.headers);
  if (clientIp) {
    headers.set("X-Forwarded-For", clientIp);
  }

  const account = readCookie(request.headers.get("Cookie"), ACCOUNT_COOKIE);
  if (account && action === "access-requests") {
    headers.set(ACCOUNT_HEADER, account);
  }

  let response: Response;
  try {
    response = await fetchImpl(apiUrl(apiBaseUrl, `/public/${action}`), { method: "POST", headers, body: await request.text(), cache: "no-store" });
  } catch {
    return problem(502, "The API is not reachable.");
  }

  const passed = new Headers({ "Content-Type": response.headers.get("Content-Type") ?? "application/json" });
  const retryAfter = response.headers.get("Retry-After");
  if (retryAfter) {
    passed.set("Retry-After", retryAfter);
  }

  return new Response(response.body, { status: response.status, headers: passed });
}

/**
 * Handles `POST /api/viewer/sign-out`: ends the session at the API (best effort), clears the account cookie and goes
 * home. The same-origin check keeps another site from signing a viewer out.
 *
 * @param request - The browser's request (a plain form post).
 * @param apiBaseUrl - Where the API is.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns A redirect to the home page that clears the cookie.
 */
export async function signOut(request: Request, apiBaseUrl: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  const account = readCookie(request.headers.get("Cookie"), ACCOUNT_COOKIE);
  if (account) {
    // The API's sign-in endpoints take JSON only (that is their CSRF check), so send an empty JSON body.
    const headers = new Headers({ [ACCOUNT_HEADER]: account, "Content-Type": "application/json" });
    const clientIp = clientAddress(request.headers);
    if (clientIp) {
      headers.set("X-Forwarded-For", clientIp);
    }

    try {
      await fetchImpl(apiUrl(apiBaseUrl, "/auth/viewer/sign-out"), { method: "POST", headers, body: "{}", cache: "no-store" });
    } catch {
      // The cookie is cleared below either way; the session also ends by itself within a day.
    }
  }

  return new Response(null, {
    status: 303,
    headers: { Location: "/", "Set-Cookie": `${ACCOUNT_COOKIE}=; Path=/; Max-Age=0; HttpOnly; Secure; SameSite=Lax` },
  });
}

/**
 * Forwards `GET /api/public/players/{slug}/photo` to the API and streams the image back with the API's cache header.
 * Errors pass through as a bare status.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param slug - The slug from the address.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The image, or an empty error answer.
 */
export async function forwardPublicPhoto(request: Request, apiBaseUrl: string, slug: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSlug(slug)) {
    return new Response(null, { status: 404 });
  }

  let response: Response;
  try {
    response = await fetchImpl(apiUrl(apiBaseUrl, `/public/players/${slug}/photo`), { headers: readHeaders(clientAddress(request.headers)), cache: "no-store" });
  } catch {
    return new Response(null, { status: 502 });
  }

  if (!response.ok) {
    return new Response(null, { status: response.status });
  }

  return new Response(response.body, {
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "image/webp",
      "Cache-Control": response.headers.get("Cache-Control") ?? "public, max-age=3600",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
