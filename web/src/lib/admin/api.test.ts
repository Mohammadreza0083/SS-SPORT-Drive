import { describe, expect, it, vi } from "vitest";
import { ADMIN_COOKIE } from "./session";
import { AdminApiError, adminFetch, forwardAdmin } from "./api";

/**
 * How the console talks to the API. Server components read with {@link adminFetch} (the session from the cookie goes
 * as `X-Admin-Session`, never cached). Browser actions (move a file, publish, retry, save settings, create a link) go
 * through the same-origin route `/api/admin/[...path]`, which {@link forwardAdmin} guards: same origin only, JSON bodies
 * only on writes (CSRF), a signed-in cookie, and only paths under the API's `/admin/`.
 */

const API = "http://api:8080";

const TOKEN = "admin-session-token";

/**
 * Builds a browser request to the forwarding route.
 *
 * @param path - The path after `/api/admin/`, with its query.
 * @param init - Method, headers and body.
 * @returns The request.
 */
function browserRequest(
  path: string,
  init: { method?: string; origin?: string | null; cookie?: string | null; body?: string; contentType?: string | null } = {},
): Request {
  const headers = new Headers({ Host: "drive.ss-sport.co" });
  if (init.origin !== null) {
    headers.set("Origin", init.origin ?? "https://drive.ss-sport.co");
  }

  if (init.cookie !== null) {
    headers.set("Cookie", init.cookie ?? `${ADMIN_COOKIE}=${TOKEN}`);
  }

  if (init.contentType !== null && init.body !== undefined) {
    headers.set("Content-Type", init.contentType ?? "application/json");
  }

  return new Request(`http://web:3000/api/admin/${path}`, { method: init.method ?? "GET", headers, body: init.body });
}

describe("adminFetch", () => {
  it("sends the session header, never caches and parses JSON", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ items: [], totalCount: 0 }));

    const body = await adminFetch<{ totalCount: number }>(API, TOKEN, "/admin/players?page=1", undefined, fetchMock);

    expect(body.totalCount).toBe(0);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://api:8080/admin/players?page=1");
    expect(new Headers(init.headers).get("X-Admin-Session")).toBe(TOKEN);
    expect(init.cache).toBe("no-store");
  });

  it("throws AdminApiError with the status and problem code for an error answer", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ code: "player.incomplete" }, { status: 422 }));

    const error = await adminFetch(API, TOKEN, "/admin/players/1/publish", { method: "POST", body: "{}" }, fetchMock).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(AdminApiError);
    expect((error as AdminApiError).status).toBe(422);
    expect((error as AdminApiError).code).toBe("player.incomplete");
  });

  it("answers null for 204", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));

    expect(await adminFetch(API, TOKEN, "/admin/players/1/files/2/move", { method: "POST", body: "{}" }, fetchMock)).toBeNull();
  });
});

describe("forwardAdmin (/api/admin/[...path])", () => {
  it("forwards a JSON write with the session header and passes the answer through", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ status: "Published" }, { status: 200 }));

    const response = await forwardAdmin(browserRequest("players/abc/publish", { method: "POST", body: "{}" }), API, ["players", "abc", "publish"], fetchMock);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ status: "Published" });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://api:8080/admin/players/abc/publish");
    expect(init.method).toBe("POST");
    expect(new Headers(init.headers).get("X-Admin-Session")).toBe(TOKEN);
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(response.headers.get("Cache-Control")).toBe("no-store");
  });

  it("keeps the query string on reads (the CSV export)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("Name\r\n", { headers: { "Content-Type": "text/csv", "Content-Disposition": 'attachment; filename="players-2026-11-14.csv"' } }));

    const response = await forwardAdmin(browserRequest("players.csv?status=live&position=MB"), API, ["players.csv"], fetchMock);

    expect(fetchMock.mock.calls[0][0]).toBe("http://api:8080/admin/players.csv?status=live&position=MB");
    expect(response.headers.get("Content-Type")).toBe("text/csv");
    expect(response.headers.get("Content-Disposition")).toContain("players-2026-11-14.csv");
  });

  it("refuses cross-site requests (403), non-JSON writes (415) and callers without a session (401)", async () => {
    const fetchMock = vi.fn();

    const crossSite = await forwardAdmin(browserRequest("settings", { method: "PUT", origin: "https://evil.example", body: "{}" }), API, ["settings"], fetchMock);
    const form = await forwardAdmin(browserRequest("settings", { method: "PUT", body: "a=1", contentType: "application/x-www-form-urlencoded" }), API, ["settings"], fetchMock);
    const anonymous = await forwardAdmin(browserRequest("settings", { method: "PUT", body: "{}", cookie: null }), API, ["settings"], fetchMock);

    expect(crossSite.status).toBe(403);
    expect(form.status).toBe(415);
    expect(anonymous.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("never forwards a path that leaves /admin/", async () => {
    const fetchMock = vi.fn();

    for (const segments of [["..", "auth", "admin", "session"], ["%2e%2e", "links"], [""], ["players", "..", "..", "links"]]) {
      const response = await forwardAdmin(browserRequest(segments.join("/")), API, segments, fetchMock);
      expect(response.status).toBe(404);
    }

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers 502 when the API cannot be reached", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error("down"));

    const response = await forwardAdmin(browserRequest("dashboard"), API, ["dashboard"], fetchMock);

    expect(response.status).toBe(502);
  });
});
