import { SESSION_COOKIE } from "./gate";
import { clientAddress, readCookie } from "./gate-server";
import { GUID, apiHeaders, linkBase } from "./content-server";

/**
 * The server side of the same-origin routes the browser asks for signed media (backlog V-5b) and scout reports (V-8):
 * `…/videos/{videoId}/stream`, `…/documents/{documentId}/pages` and `…/scouts/{scoutId}`. Each forwards to the API with
 * the session cookie as `X-Viewer-Session` and the viewer's address in `X-Forwarded-For`, and answers JSON that is never
 * cached. They only read, so no CSRF check is needed (the same as the photo route).
 */

/**
 * Forwards one read to the API and passes its JSON back.
 *
 * @param request - The browser's request, with the session cookie.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @param itemId - The video, document or scout file id.
 * @param suffix - The path after the player, with `{id}` standing for the item id.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The API's JSON, or a bare status (the API's body never leaks through an error).
 */
async function forwardJson(
  request: Request,
  apiBaseUrl: string,
  linkCode: string,
  playerId: string,
  itemId: string,
  suffix: string,
  fetchImpl: typeof fetch,
): Promise<Response> {
  if (!GUID.test(playerId) || !GUID.test(itemId)) {
    return new Response(null, { status: 404 });
  }

  const session = readCookie(request.headers.get("Cookie"), SESSION_COOKIE);
  if (!session) {
    return new Response(null, { status: 401 });
  }

  let response: Response;
  try {
    response = await fetchImpl(`${linkBase(apiBaseUrl, linkCode)}/players/${playerId}/${suffix.replace("{id}", itemId)}`, {
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
    headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" },
  });
}

/**
 * Forwards `GET /api/links/{code}/players/{id}/videos/{videoId}/stream`.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param playerId - The player id from the URL.
 * @param videoId - The video id from the URL.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The signed stream answer, or a bare error status.
 */
export function forwardStream(request: Request, apiBaseUrl: string, linkCode: string, playerId: string, videoId: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  return forwardJson(request, apiBaseUrl, linkCode, playerId, videoId, "videos/{id}/stream", fetchImpl);
}

/**
 * Forwards `GET /api/links/{code}/players/{id}/documents/{documentId}/pages`.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param playerId - The player id from the URL.
 * @param documentId - The document id from the URL.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The signed pages answer, or a bare error status.
 */
export function forwardPages(request: Request, apiBaseUrl: string, linkCode: string, playerId: string, documentId: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  return forwardJson(request, apiBaseUrl, linkCode, playerId, documentId, "documents/{id}/pages", fetchImpl);
}

/**
 * Forwards `GET /api/links/{code}/players/{id}/scouts/{scoutId}`.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param playerId - The player id from the URL.
 * @param scoutId - The scout file id from the URL.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The report, or a bare error status (422 when the file cannot be read).
 */
export function forwardScoutReport(request: Request, apiBaseUrl: string, linkCode: string, playerId: string, scoutId: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  return forwardJson(request, apiBaseUrl, linkCode, playerId, scoutId, "scouts/{id}", fetchImpl);
}
