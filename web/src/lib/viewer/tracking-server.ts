import { SESSION_COOKIE } from "./gate";
import { clientAddress, isSameOrigin, problem, readCookie } from "./gate-server";
import { GUID, apiHeaders, linkBase } from "./content-server";

/**
 * The server side of the same-origin routes the player reports watching through (backlog W-2): start, heartbeat and
 * player events under `/api/links/{code}/players/{id}/videos/{videoId}/watch`. They change state with the session
 * cookie, so each checks CSRF first (same origin and a JSON body), like the gate routes. The cookie goes to the API as
 * `X-Viewer-Session` and the viewer's address as `X-Forwarded-For`. Only the known fields of the body are passed on, and
 * an error comes back as a bare status, never the API's body.
 */

/** What a forwarder needs to know about the API call it makes. */
type ApiCall = {
  /** The API path after the link, starting with a slash. */
  path: string;
  /** The body to send, already reduced to the known fields. */
  body: unknown;
};

/**
 * True for a finite number.
 *
 * @param value - Any value.
 * @returns Whether it is a finite number.
 */
function isNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

/**
 * Forwards one tracking POST: CSRF, ids, session, body, then the API.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code.
 * @param ids - The ids from the URL; every one must be a GUID.
 * @param build - Builds the API call from the parsed body, or null when the body is not acceptable.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The API's answer, or a bare error status.
 */
async function forwardTracking(
  request: Request,
  apiBaseUrl: string,
  linkCode: string,
  ids: string[],
  build: (body: Record<string, unknown>) => ApiCall | null,
  fetchImpl: typeof fetch,
): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }
  if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) {
    return problem(415, "Send JSON.");
  }
  if (!ids.every((id) => GUID.test(id))) {
    return new Response(null, { status: 404 });
  }
  const session = readCookie(request.headers.get("Cookie"), SESSION_COOKIE);
  if (!session) {
    return new Response(null, { status: 401 });
  }

  let parsed: unknown;
  try {
    parsed = await request.json();
  } catch {
    return new Response(null, { status: 400 });
  }
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    return new Response(null, { status: 400 });
  }
  const call = build(parsed as Record<string, unknown>);
  if (!call) {
    return new Response(null, { status: 400 });
  }

  const headers = apiHeaders(session, clientAddress(request.headers));
  headers.set("Content-Type", "application/json");
  let response: Response;
  try {
    response = await fetchImpl(`${linkBase(apiBaseUrl, linkCode)}${call.path}`, {
      method: "POST",
      headers,
      body: JSON.stringify(call.body),
      cache: "no-store",
    });
  } catch {
    return new Response(null, { status: 502 });
  }

  if (!response.ok) {
    return new Response(null, { status: response.status });
  }
  const noStore = { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" };
  if (response.status === 204) {
    return new Response(null, { status: 204, headers: noStore });
  }
  return new Response(response.body, { status: response.status, headers: { ...noStore, "Content-Type": "application/json; charset=utf-8" } });
}

/**
 * Forwards `POST /api/links/{code}/players/{id}/videos/{videoId}/watch`: starts a watch (one play).
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param playerId - The player id from the URL.
 * @param videoId - The video id from the URL.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The watch id and heartbeat interval, or a bare error status.
 */
export function forwardWatchStart(request: Request, apiBaseUrl: string, linkCode: string, playerId: string, videoId: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  return forwardTracking(request, apiBaseUrl, linkCode, [playerId, videoId], () => ({ path: `/players/${playerId}/videos/${videoId}/watch`, body: {} }), fetchImpl);
}

/**
 * Forwards `POST …/watch/{watchId}/heartbeat`: the range played since the last heartbeat.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param playerId - The player id from the URL.
 * @param videoId - The video id from the URL.
 * @param watchId - The watch id from the URL.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns 204, or a bare error status.
 */
export function forwardHeartbeat(request: Request, apiBaseUrl: string, linkCode: string, playerId: string, videoId: string, watchId: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  return forwardTracking(
    request,
    apiBaseUrl,
    linkCode,
    [playerId, videoId, watchId],
    (body) => (isNumber(body.from) && isNumber(body.to) ? { path: `/watch/${watchId}/heartbeat`, body: { from: body.from, to: body.to } } : null),
    fetchImpl,
  );
}

/**
 * Forwards `POST …/watch/{watchId}/events`: a player event (`Play`, `Pause`, `Seek` or `Ended`).
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param playerId - The player id from the URL.
 * @param videoId - The video id from the URL.
 * @param watchId - The watch id from the URL.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns 204, or a bare error status.
 */
export function forwardPlaybackEvent(request: Request, apiBaseUrl: string, linkCode: string, playerId: string, videoId: string, watchId: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  return forwardTracking(
    request,
    apiBaseUrl,
    linkCode,
    [playerId, videoId, watchId],
    (body) => (typeof body.type === "string" && isNumber(body.position) ? { path: `/watch/${watchId}/events`, body: { type: body.type, position: body.position } } : null),
    fetchImpl,
  );
}
