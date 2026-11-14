import { describe, expect, it, vi } from "vitest";
import { forwardHeartbeat, forwardPlaybackEvent, forwardWatchStart } from "./tracking-server";

/**
 * The same-origin routes the player reports watching through (backlog W-2):
 *
 * - `POST /api/links/{code}/players/{id}/videos/{videoId}/watch` → API `POST /links/{code}/players/{id}/videos/{videoId}/watch`
 * - `POST …/watch/{watchId}/heartbeat` → API `POST /links/{code}/watch/{watchId}/heartbeat`
 * - `POST …/watch/{watchId}/events` → API `POST /links/{code}/watch/{watchId}/events`
 *
 * They change state with the session cookie, so each one checks CSRF first (same origin and a JSON body), exactly like
 * the gate routes. The cookie goes to the API as `X-Viewer-Session` and the viewer's address as `X-Forwarded-For`. Only
 * the known fields of the body are passed on. Errors come back as a bare status, never the API's body.
 */

const API = "http://api:8080";
const SESSION = "session-token-from-cookie";
const PLAYER = "0b6f2c1e-1111-4a4a-8888-000000000001";
const VIDEO = "0b6f2c1e-2222-4a4a-8888-000000000002";
const WATCH = "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c";

/** Options of a browser request in these tests. */
type BrowserOptions = { cookie?: boolean; origin?: string | null; contentType?: string | null; body?: string };

/**
 * A browser request to one of the routes.
 *
 * @param options - What to vary; the defaults are a good same-origin JSON request with the cookie.
 * @returns The request.
 */
function browser({ cookie = true, origin = "https://drive.ss-sport.co", contentType = "application/json", body = "{}" }: BrowserOptions = {}): Request {
  const headers = new Headers({ Host: "drive.ss-sport.co", "X-Forwarded-For": "203.0.113.9" });
  if (origin) {
    headers.set("Origin", origin);
  }
  if (contentType) {
    headers.set("Content-Type", contentType);
  }
  if (cookie) {
    headers.set("Cookie", `theme=dark; __Host-ssd_vs=${SESSION}`);
  }

  return new Request("https://drive.ss-sport.co/api/links/Ab3xYz/players/x/videos/y/watch", { method: "POST", headers, body });
}

/**
 * A fetch stub that answers once.
 *
 * @param status - The status.
 * @param body - The JSON body, or null for none.
 * @returns The stub.
 */
function api(status: number, body: unknown = null) {
  return vi.fn<typeof fetch>(async () => (body === null ? new Response(null, { status }) : Response.json(body, { status })));
}

/**
 * The URL and options of the first recorded fetch call.
 *
 * @param fetchMock - The stub.
 * @returns The URL and the options.
 */
function call(fetchMock: ReturnType<typeof api>): [string, RequestInit] {
  return fetchMock.mock.calls[0] as unknown as [string, RequestInit];
}

/** The three forwarders, each called the way its route calls it. */
const routes = [
  {
    name: "forwardWatchStart",
    forward: (request: Request, fetchMock: typeof fetch) => forwardWatchStart(request, API, "Ab3xYz", PLAYER, VIDEO, fetchMock),
    apiPath: `/links/Ab3xYz/players/${PLAYER}/videos/${VIDEO}/watch`,
    body: "{}",
    sent: {},
    ok: { status: 201, body: { watchId: WATCH, heartbeatSeconds: 10 } },
  },
  {
    name: "forwardHeartbeat",
    forward: (request: Request, fetchMock: typeof fetch) => forwardHeartbeat(request, API, "Ab3xYz", PLAYER, VIDEO, WATCH, fetchMock),
    apiPath: `/links/Ab3xYz/watch/${WATCH}/heartbeat`,
    body: JSON.stringify({ from: 20, to: 30, extra: "dropped" }),
    sent: { from: 20, to: 30 },
    ok: { status: 204, body: null },
  },
  {
    name: "forwardPlaybackEvent",
    forward: (request: Request, fetchMock: typeof fetch) => forwardPlaybackEvent(request, API, "Ab3xYz", PLAYER, VIDEO, WATCH, fetchMock),
    apiPath: `/links/Ab3xYz/watch/${WATCH}/events`,
    body: JSON.stringify({ type: "Pause", position: 42.5, extra: "dropped" }),
    sent: { type: "Pause", position: 42.5 },
    ok: { status: 204, body: null },
  },
] as const;

describe.each(routes)("$name", ({ forward, apiPath, body, sent, ok }) => {
  it("posts the known fields to the API with the session and the viewer's address", async () => {
    const fetchMock = api(ok.status, ok.body);

    const response = await forward(browser({ body }), fetchMock);

    expect(response.status).toBe(ok.status);
    expect(response.headers.get("Cache-Control")).toBe("no-store");
    if (ok.body !== null) {
      expect(await response.json()).toEqual(ok.body);
    }
    const [url, init] = call(fetchMock);
    expect(url).toBe(`${API}${apiPath}`);
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    const headers = new Headers(init.headers);
    expect(headers.get("X-Viewer-Session")).toBe(SESSION);
    expect(headers.get("X-Forwarded-For")).toBe("203.0.113.9");
    expect(headers.get("Content-Type")).toBe("application/json");
    expect(headers.get("Cookie")).toBeNull();
    expect(JSON.parse(String(init.body))).toEqual(sent);
  });

  it("refuses a cross-site request before calling the API", async () => {
    const fetchMock = api(ok.status, ok.body);

    const response = await forward(browser({ body, origin: "https://evil.example" }), fetchMock);

    expect(response.status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a request with no Origin", async () => {
    const fetchMock = api(ok.status, ok.body);

    const response = await forward(browser({ body, origin: null }), fetchMock);

    expect(response.status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a body that is not JSON", async () => {
    const fetchMock = api(ok.status, ok.body);

    const response = await forward(browser({ body, contentType: "text/plain" }), fetchMock);

    expect(response.status).toBe(415);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers 401 without the session cookie, without calling the API", async () => {
    const fetchMock = api(ok.status, ok.body);

    const response = await forward(browser({ body, cookie: false }), fetchMock);

    expect(response.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([401, 404, 422, 429])("passes a %i back as a bare status", async (status) => {
    const fetchMock = api(status, { type: "problem", detail: "internal detail" });

    const response = await forward(browser({ body }), fetchMock);

    expect(response.status).toBe(status);
    expect(await response.text()).toBe("");
  });

  it("answers 502 when the API is not reachable", async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => {
      throw new TypeError("fetch failed");
    });

    const response = await forward(browser({ body }), fetchMock);

    expect(response.status).toBe(502);
  });
});

describe("ids in the path", () => {
  it.each([
    ["a player", () => forwardWatchStart(browser(), API, "Ab3xYz", "not-a-guid", VIDEO, api(201))],
    ["a video", () => forwardWatchStart(browser(), API, "Ab3xYz", PLAYER, "../admin", api(201))],
    ["a watch", () => forwardHeartbeat(browser({ body: '{"from":0,"to":10}' }), API, "Ab3xYz", PLAYER, VIDEO, "../../admin", api(204))],
  ])("answers 404 for %s id that is not a GUID", async (_, forward) => {
    const response = await forward();

    expect(response.status).toBe(404);
  });
});

describe("bodies", () => {
  it.each([
    ["not JSON", "{from:"],
    ["a missing field", JSON.stringify({ from: 1 })],
    ["a field of the wrong type", JSON.stringify({ from: "1", to: 10 })],
  ])("answers 400 for a heartbeat with %s, without calling the API", async (_, body) => {
    const fetchMock = api(204);

    const response = await forwardHeartbeat(browser({ body }), API, "Ab3xYz", PLAYER, VIDEO, WATCH, fetchMock);

    expect(response.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers 400 for an event with no type, without calling the API", async () => {
    const fetchMock = api(204);

    const response = await forwardPlaybackEvent(browser({ body: JSON.stringify({ position: 3 }) }), API, "Ab3xYz", PLAYER, VIDEO, WATCH, fetchMock);

    expect(response.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
