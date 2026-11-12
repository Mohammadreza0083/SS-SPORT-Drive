import { describe, expect, it, vi } from "vitest";
import { forwardPages, forwardScoutReport, forwardStream } from "./media-server";

/**
 * The same-origin routes the browser asks for signed media (V-5b) and scout reports (V-8):
 * `GET /api/links/{code}/players/{id}/videos/{videoId}/stream`, `…/documents/{documentId}/pages` and
 * `…/scouts/{scoutId}`. Each forwards to the API with the session cookie as `X-Viewer-Session` and the viewer's
 * address in `X-Forwarded-For`, and answers JSON that is never cached. Reads only, so no CSRF check.
 */

const API = "http://api:8080";
const SESSION = "session-token-from-cookie";
const PLAYER = "0b6f2c1e-1111-4a4a-8888-000000000001";
const ITEM = "0b6f2c1e-2222-4a4a-8888-000000000002";

/**
 * A browser request with or without the session cookie.
 *
 * @param cookie - Whether to send the session cookie.
 * @returns The request.
 */
function browser(cookie = true): Request {
  const headers = new Headers({ "X-Forwarded-For": "203.0.113.9" });
  if (cookie) {
    headers.set("Cookie", `theme=dark; __Host-ssd_vs=${SESSION}`);
  }

  return new Request("https://drive.ss-sport.co/api/links/Ab3xYz/players/x", { headers });
}

/**
 * A fetch stub that answers once.
 *
 * @param status - The status.
 * @param body - The JSON body.
 * @returns The stub.
 */
function api(status: number, body: unknown = {}) {
  return vi.fn(async () => Response.json(body, { status }));
}

/** The three forwarders with the API path each one calls. */
const routes = [
  { name: "forwardStream", forward: forwardStream, path: `/links/Ab3xYz/players/${PLAYER}/videos/${ITEM}/stream`, body: { playlistUrl: "/hls/t/x/master.m3u8", expiresAt: "2026-11-12T10:30:00Z" } },
  { name: "forwardPages", forward: forwardPages, path: `/links/Ab3xYz/players/${PLAYER}/documents/${ITEM}/pages`, body: { pages: ["/pages/t/x/1.webp"], expiresAt: "2026-11-12T10:30:00Z" } },
  { name: "forwardScoutReport", forward: forwardScoutReport, path: `/links/Ab3xYz/players/${PLAYER}/scouts/${ITEM}`, body: { id: ITEM, title: "Match", match: {}, player: null, skills: [] } },
] as const;

describe.each(routes)("$name", ({ forward, path, body }) => {
  it("asks the API with the session and the viewer's address, and answers its JSON uncached", async () => {
    const fetchMock = api(200, body);

    const response = await forward(browser(), API, "Ab3xYz", PLAYER, ITEM, fetchMock);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual(body);
    expect(response.headers.get("Cache-Control")).toBe("no-store");
    expect(response.headers.get("Content-Type")).toContain("application/json");
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${API}${path}`);
    const headers = new Headers(init.headers);
    expect(headers.get("X-Viewer-Session")).toBe(SESSION);
    expect(headers.get("X-Forwarded-For")).toBe("203.0.113.9");
    expect(init.cache).toBe("no-store");
  });

  it("answers 401 without a session cookie and without asking the API", async () => {
    const fetchMock = api(200, body);

    const response = await forward(browser(false), API, "Ab3xYz", PLAYER, ITEM, fetchMock);

    expect(response.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["player", "not-a-guid", ITEM],
    ["item", PLAYER, "../../admin"],
  ])("refuses a %s id that is not a GUID with 404, without asking the API", async (_which, playerId, itemId) => {
    const fetchMock = api(200, body);

    const response = await forward(browser(), API, "Ab3xYz", playerId, itemId, fetchMock);

    expect(response.status).toBe(404);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([401, 404, 422, 503])("passes a %s through with no body from the API", async (status) => {
    const fetchMock = api(status, { code: "secret.detail", detail: "internal" });

    const response = await forward(browser(), API, "Ab3xYz", PLAYER, ITEM, fetchMock);

    expect(response.status).toBe(status);
    expect(await response.text()).toBe("");
  });

  it("answers 502 when the API cannot be reached", async () => {
    const fetchMock = vi.fn(async () => {
      throw new TypeError("fetch failed");
    });

    const response = await forward(browser(), API, "Ab3xYz", PLAYER, ITEM, fetchMock);

    expect(response.status).toBe(502);
  });

  it("encodes the link code into the API path", async () => {
    const fetchMock = api(200, body);

    await forward(browser(), API, "A b/c", PLAYER, ITEM, fetchMock);

    expect(String(fetchMock.mock.calls[0]?.[0])).toContain("/links/A%20b%2Fc/");
  });
});
