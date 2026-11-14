import type { WatchStarted, WatchTransport } from "./watch-tracker";

/**
 * The browser side of watch tracking (backlog W-2): the same-origin routes under a video's `…/watch` path, and the
 * transport the tracker sends through. Every call is a same-origin JSON POST, never cached; heartbeats and events use
 * `keepalive` so the last one still goes out when the viewer leaves the page.
 */

/** The heartbeat interval used when the answer has none that makes sense. */
const DEFAULT_HEARTBEAT_SECONDS = 10;

/** A watch id as the API makes it. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A failed request to one of the tracking routes. */
export class TrackingRequestError extends Error {
  /**
   * @param status - The HTTP status the route answered.
   */
  constructor(readonly status: number) {
    super(`The tracking route answered ${status}.`);
    this.name = "TrackingRequestError";
  }
}

/**
 * The route that starts a watch of a video; heartbeats and events go beneath it.
 *
 * @param linkCode - The link code.
 * @param playerId - The player id.
 * @param videoId - The video id.
 * @returns The same-origin path, with the code encoded.
 */
export function watchPath(linkCode: string, playerId: string, videoId: string): string {
  return `/api/links/${encodeURIComponent(linkCode)}/players/${playerId}/videos/${videoId}/watch`;
}

/**
 * Posts JSON to a tracking route.
 *
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @param url - The route.
 * @param body - The JSON body.
 * @param keepalive - Whether the request may outlive the page.
 * @returns The response.
 * @throws {TrackingRequestError} For any answer that is not 2xx.
 */
async function post(fetchImpl: typeof fetch, url: string, body: unknown, keepalive: boolean): Promise<Response> {
  const response = await fetchImpl(url, {
    method: "POST",
    credentials: "same-origin",
    cache: "no-store",
    keepalive,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new TrackingRequestError(response.status);
  }
  return response;
}

/**
 * The transport the tracker sends through for one video.
 *
 * @param path - The video's watch route.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The transport.
 */
export function createWatchTransport(path: string, fetchImpl: typeof fetch = fetch): WatchTransport {
  return {
    async start(): Promise<WatchStarted> {
      const answer = (await (await post(fetchImpl, path, {}, false)).json()) as { watchId?: unknown; heartbeatSeconds?: unknown };
      if (typeof answer.watchId !== "string" || !GUID.test(answer.watchId)) {
        throw new Error("The watch id is not valid.");
      }
      const seconds = typeof answer.heartbeatSeconds === "number" && Number.isFinite(answer.heartbeatSeconds) && answer.heartbeatSeconds > 0;
      return { watchId: answer.watchId, heartbeatSeconds: seconds ? (answer.heartbeatSeconds as number) : DEFAULT_HEARTBEAT_SECONDS };
    },
    async heartbeat(watchId: string, from: number, to: number): Promise<void> {
      await post(fetchImpl, `${path}/${watchId}/heartbeat`, { from, to }, true);
    },
    async event(watchId: string, type: string, position: number): Promise<void> {
      await post(fetchImpl, `${path}/${watchId}/events`, { type, position }, true);
    },
  };
}
