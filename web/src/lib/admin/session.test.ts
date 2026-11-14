import { describe, expect, it, vi } from "vitest";
import { ADMIN_COOKIE, ADMIN_HEADER, readAdmin } from "./session";

/**
 * The console's view of the admin session. Signing in and out is W-1 (`lib/auth/sign-in-server`); the console only reads
 * who is signed in and when the session ends, with the `__Host-ssd_as` cookie's token sent as `X-Admin-Session`.
 */

const API = "http://api:8080";

const TOKEN = "admin-session-token";

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
