import { describe, expect, it, vi } from "vitest";
import { ACCOUNT_COOKIE } from "@/lib/viewer/public-server";
import { ADMIN_COOKIE, ADMIN_SESSION_SECONDS, VIEWER_SESSION_SECONDS, isAudience, readAdmin, requestSignInLink, signOutAdmin, startSession } from "./sign-in-server";

/**
 * The web half of magic-link sign-in (backlog W-1, the web part of A-1 and P-5; decisions §7a, §8a):
 *
 * - `POST /api/auth/{viewer|admin}/link` forwards the email to the API's `/auth/{audience}/link` (same-origin JSON only).
 * - The emailed link opens `/sign-in?token=…` or `/admin/sign-in?token=…`. That page never spends the one-time token
 *   by itself (mail scanners open links); its Continue button posts the token to `POST /api/auth/{audience}/session`,
 *   which turns it into a session at the API, stores the session token only in an `HttpOnly` `__Host-` cookie and
 *   redirects: viewers to the home page, admins to `/admin`.
 * - The viewer cookie is `__Host-ssd_acct` (7 days, `SameSite=Lax`), the admin cookie `__Host-ssd_as` (30 days,
 *   `SameSite=Strict`). The API never reads cookies: the web forwards them as `X-Viewer-Account` / `X-Admin-Session`.
 * - `POST /api/admin/sign-out` ends the admin session and clears the cookie.
 */

const API = "http://api:8080";

/**
 * Builds a browser request to one of the sign-in routes.
 *
 * @param path - The route path.
 * @param init - Headers and body; Origin and Host default to the site.
 * @returns The request.
 */
function browserRequest(
  path: string,
  init: { origin?: string | null; cookie?: string; body?: string; contentType?: string | null; forwardedFor?: string } = {},
): Request {
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

  if (init.forwardedFor) {
    headers.set("X-Forwarded-For", init.forwardedFor);
  }

  return new Request(`http://web:3000${path}`, { method: "POST", headers, body: init.body });
}

/**
 * A form post of the Continue button.
 *
 * @param path - The route path.
 * @param token - The one-time token, or nothing.
 * @param origin - The Origin header, or null for none.
 * @returns The request.
 */
function continueRequest(path: string, token: string | null, origin: string | null = "https://drive.ss-sport.co"): Request {
  const body = token === null ? "" : new URLSearchParams({ token }).toString();
  return browserRequest(path, { origin, body, contentType: "application/x-www-form-urlencoded" });
}

/**
 * The API's answer to a redeemed token.
 *
 * @param audience - Who signed in.
 * @returns The response.
 */
function sessionAnswer(audience: "Viewer" | "Admin"): Response {
  return Response.json({ sessionToken: "session-token-abc", expiresAt: "2026-11-21T09:00:00+00:00", email: "scout@club.com", audience });
}

describe("isAudience", () => {
  it("knows exactly the two audiences", () => {
    expect(isAudience("viewer")).toBe(true);
    expect(isAudience("admin")).toBe(true);
    expect(isAudience("Admin")).toBe(false);
    expect(isAudience("player")).toBe(false);
  });
});

describe("requestSignInLink (POST /api/auth/{audience}/link)", () => {
  it("forwards the email as JSON to the API and passes the 202 back", async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 202 }));

    const response = await requestSignInLink(
      browserRequest("/api/auth/viewer/link", { body: JSON.stringify({ email: "scout@club.com" }), forwardedFor: "203.0.113.9" }),
      API,
      "viewer",
      fetchImpl,
    );

    expect(response.status).toBe(202);
    expect(fetchImpl).toHaveBeenCalledOnce();
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://api:8080/auth/viewer/link");
    expect(init.method).toBe("POST");
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(new Headers(init.headers).get("X-Forwarded-For")).toBe("203.0.113.9");
    expect(JSON.parse(String(init.body))).toEqual({ email: "scout@club.com" });
  });

  it("uses the admin route for the admin audience", async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 202 }));

    await requestSignInLink(browserRequest("/api/auth/admin/link", { body: JSON.stringify({ email: "owner@ss-sport.co" }) }), API, "admin", fetchImpl);

    expect((fetchImpl.mock.calls[0] as unknown[])[0]).toBe("http://api:8080/auth/admin/link");
  });

  it("passes a rate-limit answer through with its Retry-After", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ title: "Too many" }), { status: 429, headers: { "Retry-After": "60", "Content-Type": "application/problem+json" } }));

    const response = await requestSignInLink(browserRequest("/api/auth/viewer/link", { body: JSON.stringify({ email: "scout@club.com" }) }), API, "viewer", fetchImpl);

    expect(response.status).toBe(429);
    expect(response.headers.get("Retry-After")).toBe("60");
  });

  it("refuses cross-site and non-JSON requests without calling the API", async () => {
    const fetchImpl = vi.fn();

    const crossSite = await requestSignInLink(browserRequest("/api/auth/viewer/link", { origin: "https://evil.example", body: "{}" }), API, "viewer", fetchImpl);
    const form = await requestSignInLink(browserRequest("/api/auth/viewer/link", { contentType: "application/x-www-form-urlencoded", body: "email=a" }), API, "viewer", fetchImpl);

    expect(crossSite.status).toBe(403);
    expect(form.status).toBe(415);
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it("answers 502 when the API cannot be reached", async () => {
    const fetchImpl = vi.fn(async () => {
      throw new TypeError("fetch failed");
    });

    const response = await requestSignInLink(browserRequest("/api/auth/viewer/link", { body: JSON.stringify({ email: "scout@club.com" }) }), API, "viewer", fetchImpl);

    expect(response.status).toBe(502);
  });
});

describe("startSession (POST /api/auth/{audience}/session)", () => {
  it("turns a viewer's token into the account cookie and goes home", async () => {
    const fetchImpl = vi.fn(async () => sessionAnswer("Viewer"));

    const response = await startSession(continueRequest("/api/auth/viewer/session", "one-time-token"), API, "viewer", fetchImpl);

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://api:8080/auth/viewer/session");
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(JSON.parse(String(init.body))).toEqual({ token: "one-time-token" });

    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/");
    const cookie = response.headers.get("Set-Cookie") ?? "";
    expect(cookie).toContain(`${ACCOUNT_COOKIE}=session-token-abc`);
    expect(cookie).toContain("Path=/");
    expect(cookie).toContain(`Max-Age=${VIEWER_SESSION_SECONDS}`);
    expect(cookie).toContain("HttpOnly");
    expect(cookie).toContain("Secure");
    expect(cookie).toContain("SameSite=Lax");
    expect(cookie).not.toContain("Domain=");
  });

  it("turns an admin's token into the admin cookie and opens the console", async () => {
    const fetchImpl = vi.fn(async () => sessionAnswer("Admin"));

    const response = await startSession(continueRequest("/api/auth/admin/session", "one-time-token"), API, "admin", fetchImpl);

    expect((fetchImpl.mock.calls[0] as unknown[])[0]).toBe("http://api:8080/auth/admin/session");
    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/admin");
    const cookie = response.headers.get("Set-Cookie") ?? "";
    expect(cookie).toContain(`${ADMIN_COOKIE}=session-token-abc`);
    expect(cookie).toContain(`Max-Age=${ADMIN_SESSION_SECONDS}`);
    expect(cookie).toContain("HttpOnly");
    expect(cookie).toContain("Secure");
    expect(cookie).toContain("SameSite=Strict");
  });

  it("uses the agreed cookie names and lifetimes", () => {
    expect(ACCOUNT_COOKIE).toBe("__Host-ssd_acct");
    expect(ADMIN_COOKIE).toBe("__Host-ssd_as");
    expect(VIEWER_SESSION_SECONDS).toBe(7 * 24 * 60 * 60);
    expect(ADMIN_SESSION_SECONDS).toBe(30 * 24 * 60 * 60);
  });

  it("sends a used or expired token back to the sign-in page with no cookie", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ code: "sign_in.invalid" }), { status: 401 }));

    const viewer = await startSession(continueRequest("/api/auth/viewer/session", "spent"), API, "viewer", fetchImpl);
    const admin = await startSession(continueRequest("/api/auth/admin/session", "spent"), API, "admin", fetchImpl);

    expect(viewer.status).toBe(303);
    expect(viewer.headers.get("Location")).toBe("/sign-in?error=expired");
    expect(viewer.headers.get("Set-Cookie")).toBeNull();
    expect(admin.headers.get("Location")).toBe("/admin/sign-in?error=expired");
    expect(admin.headers.get("Set-Cookie")).toBeNull();
  });

  it("does not call the API without a token", async () => {
    const fetchImpl = vi.fn();

    const response = await startSession(continueRequest("/api/auth/viewer/session", null), API, "viewer", fetchImpl);

    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/sign-in?error=expired");
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it("says the service is unavailable when the API cannot be reached", async () => {
    const fetchImpl = vi.fn(async () => {
      throw new TypeError("fetch failed");
    });

    const response = await startSession(continueRequest("/api/auth/viewer/session", "one-time-token"), API, "viewer", fetchImpl);

    expect(response.headers.get("Location")).toBe("/sign-in?error=unavailable");
    expect(response.headers.get("Set-Cookie")).toBeNull();
  });

  it("refuses a cross-site post (login CSRF) without spending the token", async () => {
    const fetchImpl = vi.fn();

    const response = await startSession(continueRequest("/api/auth/viewer/session", "one-time-token", "https://evil.example"), API, "viewer", fetchImpl);

    expect(response.status).toBe(403);
    expect(response.headers.get("Set-Cookie")).toBeNull();
    expect(fetchImpl).not.toHaveBeenCalled();
  });
});

describe("signOutAdmin (POST /api/admin/sign-out)", () => {
  it("ends the session at the API, clears the cookie and goes to the admin sign-in page", async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 204 }));

    const response = await signOutAdmin(browserRequest("/api/admin/sign-out", { cookie: `${ADMIN_COOKIE}=admin-token`, contentType: null }), API, fetchImpl);

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://api:8080/auth/admin/sign-out");
    expect(new Headers(init.headers).get("X-Admin-Session")).toBe("admin-token");
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/admin/sign-in");
    const cookie = response.headers.get("Set-Cookie") ?? "";
    expect(cookie).toContain(`${ADMIN_COOKIE}=;`);
    expect(cookie).toContain("Max-Age=0");
  });

  it("refuses a cross-site sign-out", async () => {
    const fetchImpl = vi.fn();

    const response = await signOutAdmin(browserRequest("/api/admin/sign-out", { origin: "https://evil.example", cookie: `${ADMIN_COOKIE}=admin-token` }), API, fetchImpl);

    expect(response.status).toBe(403);
    expect(fetchImpl).not.toHaveBeenCalled();
  });
});

describe("readAdmin", () => {
  it("reads who is signed in from the API with the admin header", async () => {
    const fetchImpl = vi.fn(async () => Response.json({ email: "owner@ss-sport.co", displayName: "Owner", audience: "Admin", expiresAt: "2026-12-13T09:00:00+00:00" }));

    const admin = await readAdmin(API, "admin-token", fetchImpl);

    expect(admin).toEqual({ email: "owner@ss-sport.co", displayName: "Owner" });
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://api:8080/auth/admin/me");
    expect(new Headers(init.headers).get("X-Admin-Session")).toBe("admin-token");
  });

  it("is nobody without a cookie, or when the session ended or the API is down", async () => {
    const ended = vi.fn(async () => new Response(null, { status: 401 }));
    const down = vi.fn(async () => {
      throw new TypeError("fetch failed");
    });
    const unused = vi.fn();

    expect(await readAdmin(API, undefined, unused)).toBeNull();
    expect(await readAdmin(API, "admin-token", ended)).toBeNull();
    expect(await readAdmin(API, "admin-token", down)).toBeNull();
    expect(unused).not.toHaveBeenCalled();
  });
});
