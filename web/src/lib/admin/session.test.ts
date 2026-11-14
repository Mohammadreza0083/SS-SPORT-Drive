import { describe, expect, it, vi } from "vitest";
import { ADMIN_COOKIE, ADMIN_HEADER, adminSignOut, readAdmin, requestAdminLink, startAdminSession } from "./session";

/**
 * Admin sign-in on the web (W-1 admin half, owner 2026-11-14: phase 0 of the console). The API (A-1) mails a one-time
 * link to `/admin/sign-in?token=…`; that page only shows a "Continue" button, so a mail scanner that opens the link
 * cannot burn it. The button posts the token to `/api/admin/session`, which trades it at `POST /auth/admin/session`
 * and keeps the session token in the `HttpOnly` cookie `__Host-ssd_as`. The browser never sees the token again; server
 * code sends it to the API as `X-Admin-Session`. Every route is same-origin only (CSRF).
 */

const API = "http://api:8080";

const TOKEN = "admin-session-token";

/**
 * Builds a browser request to an admin route.
 *
 * @param path - The route path.
 * @param init - Headers and body; Origin defaults to the site.
 * @returns The request.
 */
function browserRequest(path: string, init: { origin?: string | null; cookie?: string; body?: string; contentType?: string | null; method?: string } = {}): Request {
  const headers = new Headers({ Host: "drive.ss-sport.co" });
  if (init.origin !== null) {
    headers.set("Origin", init.origin ?? "https://drive.ss-sport.co");
  }

  if (init.contentType !== null) {
    headers.set("Content-Type", init.contentType ?? "application/json");
  }

  if (init.cookie) {
    headers.set("Cookie", init.cookie);
  }

  return new Request(`http://web:3000${path}`, { method: init.method ?? "POST", headers, body: init.body });
}

/**
 * The init a stubbed fetch was called with.
 *
 * @param fetchMock - The stub.
 * @returns The first call's URL and init.
 */
function firstCall(fetchMock: ReturnType<typeof vi.fn>): { url: string; init: RequestInit } {
  const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
  return { url, init };
}

describe("constants", () => {
  it("keeps the session in a __Host- cookie and sends it as X-Admin-Session", () => {
    expect(ADMIN_COOKIE).toBe("__Host-ssd_as");
    expect(ADMIN_HEADER).toBe("X-Admin-Session");
  });
});

describe("requestAdminLink (POST /api/admin/sign-in-link)", () => {
  it("forwards the email as JSON and always answers 202, whatever the API says", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));

    const response = await requestAdminLink(browserRequest("/api/admin/sign-in-link", { body: JSON.stringify({ email: "mr@ss-sport.co" }) }), API, fetchMock);

    expect(response.status).toBe(202);
    const { url, init } = firstCall(fetchMock);
    expect(url).toBe("http://api:8080/auth/admin/link");
    expect(init.method).toBe("POST");
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(JSON.parse(String(init.body))).toEqual({ email: "mr@ss-sport.co" });
  });

  it("passes a rate limit through as 429 so the page can say wait", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 429, headers: { "Retry-After": "60" } }));

    const response = await requestAdminLink(browserRequest("/api/admin/sign-in-link", { body: JSON.stringify({ email: "mr@ss-sport.co" }) }), API, fetchMock);

    expect(response.status).toBe(429);
    expect(response.headers.get("Retry-After")).toBe("60");
  });

  it("refuses a cross-site request (403) and a non-JSON body (415) without calling the API", async () => {
    const fetchMock = vi.fn();

    const crossSite = await requestAdminLink(browserRequest("/api/admin/sign-in-link", { origin: "https://evil.example", body: "{}" }), API, fetchMock);
    const form = await requestAdminLink(browserRequest("/api/admin/sign-in-link", { contentType: "application/x-www-form-urlencoded", body: "email=a" }), API, fetchMock);

    expect(crossSite.status).toBe(403);
    expect(form.status).toBe(415);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("startAdminSession (POST /api/admin/session)", () => {
  it("trades the token, sets the __Host- cookie until the session ends and goes to the console", async () => {
    const expiresAt = new Date(Date.now() + 30 * 24 * 3600 * 1000).toISOString();
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ sessionToken: TOKEN, expiresAt, email: "mr@ss-sport.co", audience: "Admin" }),
    );

    const response = await startAdminSession(
      browserRequest("/api/admin/session", { contentType: "application/x-www-form-urlencoded", body: "token=one-time-token" }),
      API,
      fetchMock,
    );

    const { url, init } = firstCall(fetchMock);
    expect(url).toBe("http://api:8080/auth/admin/session");
    expect(JSON.parse(String(init.body))).toEqual({ token: "one-time-token" });
    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/admin");
    const cookie = response.headers.get("Set-Cookie") ?? "";
    expect(cookie).toContain(`${ADMIN_COOKIE}=${TOKEN}`);
    expect(cookie).toContain("Path=/");
    expect(cookie).toContain("HttpOnly");
    expect(cookie).toContain("Secure");
    expect(cookie).toContain("SameSite=Lax");
    const maxAge = Number(/Max-Age=(\d+)/.exec(cookie)?.[1]);
    expect(maxAge).toBeGreaterThan(30 * 24 * 3600 - 120);
    expect(maxAge).toBeLessThanOrEqual(30 * 24 * 3600);
    expect(cookie).not.toContain("Domain=");
  });

  it("sends a used or expired link back to the sign-in page with an error and no cookie", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ code: "sign_in.invalid" }, { status: 401 }));

    const response = await startAdminSession(
      browserRequest("/api/admin/session", { contentType: "application/x-www-form-urlencoded", body: "token=used" }),
      API,
      fetchMock,
    );

    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/admin/sign-in?error=link");
    expect(response.headers.get("Set-Cookie")).toBeNull();
  });

  it("refuses a cross-site post and a missing token without calling the API", async () => {
    const fetchMock = vi.fn();

    const crossSite = await startAdminSession(
      browserRequest("/api/admin/session", { origin: "https://evil.example", contentType: "application/x-www-form-urlencoded", body: "token=x" }),
      API,
      fetchMock,
    );
    const empty = await startAdminSession(browserRequest("/api/admin/session", { contentType: "application/x-www-form-urlencoded", body: "" }), API, fetchMock);

    expect(crossSite.status).toBe(403);
    expect(empty.status).toBe(303);
    expect(empty.headers.get("Location")).toBe("/admin/sign-in?error=link");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("adminSignOut (POST /api/admin/sign-out)", () => {
  it("ends the session at the API, clears the cookie and goes to the sign-in page", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));

    const response = await adminSignOut(
      browserRequest("/api/admin/sign-out", { contentType: "application/x-www-form-urlencoded", body: "", cookie: `${ADMIN_COOKIE}=${TOKEN}` }),
      API,
      fetchMock,
    );

    const { url, init } = firstCall(fetchMock);
    expect(url).toBe("http://api:8080/auth/admin/sign-out");
    expect(new Headers(init.headers).get(ADMIN_HEADER)).toBe(TOKEN);
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/admin/sign-in");
    expect(response.headers.get("Set-Cookie")).toContain("Max-Age=0");
  });

  it("refuses a cross-site sign-out and keeps the cookie", async () => {
    const fetchMock = vi.fn();

    const response = await adminSignOut(browserRequest("/api/admin/sign-out", { origin: "https://evil.example", cookie: `${ADMIN_COOKIE}=${TOKEN}` }), API, fetchMock);

    expect(response.status).toBe(403);
    expect(response.headers.get("Set-Cookie")).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("readAdmin", () => {
  it("reads who is signed in with the session header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ email: "mr@ss-sport.co", displayName: "Mohammadreza", audience: "Admin", expiresAt: "2026-12-14T09:00:00Z" }),
    );

    const admin = await readAdmin(API, TOKEN, fetchMock);

    expect(admin).toEqual({ email: "mr@ss-sport.co", displayName: "Mohammadreza", expiresAt: "2026-12-14T09:00:00Z" });
    const { url, init } = firstCall(fetchMock);
    expect(url).toBe("http://api:8080/auth/admin/me");
    expect(new Headers(init.headers).get(ADMIN_HEADER)).toBe(TOKEN);
    expect(init.cache).toBe("no-store");
  });

  it("is null without a cookie, for an ended session and when the API is down", async () => {
    const ended = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    const down = vi.fn().mockRejectedValue(new Error("down"));
    const never = vi.fn();

    expect(await readAdmin(API, undefined, never)).toBeNull();
    expect(await readAdmin(API, TOKEN, ended)).toBeNull();
    expect(await readAdmin(API, TOKEN, down)).toBeNull();
    expect(never).not.toHaveBeenCalled();
  });
});
