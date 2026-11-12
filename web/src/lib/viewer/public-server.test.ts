import { describe, expect, it, vi } from "vitest";
import { ACCOUNT_COOKIE, HOME_PAGE_SIZE, forwardPublic, forwardPublicPhoto, loadHome, loadLockedPage, signOut } from "./public-server";

/**
 * The server side of the public pages (backlog P-8): the home page and `/players/{slug}` read the API's `/public/…`
 * endpoints; the same-origin `/api/public/…` routes forward the request-access flow; `/api/viewer/sign-out` ends a
 * signed-in viewer's session. The signed-in viewer's token lives only in the `HttpOnly` cookie `__Host-ssd_acct` and
 * travels to the API as `X-Viewer-Account`.
 */

const API = "http://api:8080";

const TOKEN = "acct-token-from-cookie";

/**
 * Builds a browser request to one of the public routes.
 *
 * @param path - The route path.
 * @param init - Headers and body; Origin and Host default to the site.
 * @returns The request.
 */
function browserRequest(
  path: string,
  init: { origin?: string | null; cookie?: string; body?: unknown; contentType?: string | null; forwardedFor?: string } = {},
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

  return new Request(`http://web:3000${path}`, {
    method: "POST",
    headers,
    body: init.body === undefined ? undefined : typeof init.body === "string" ? init.body : JSON.stringify(init.body),
  });
}

/** One card as the API sends it. */
const apiCard = {
  slug: "arash-tehrani",
  name: "Arash Tehrani",
  position: { code: "OPP", label: "Opposite" },
  nationality: { code: "IR", name: "Iran" },
  videos: 12,
  documents: 3,
  hasPhoto: true,
};

/**
 * Builds a fetch stub that answers by path.
 *
 * @param routes - Path (without the API base, with the query) → response factory.
 * @returns The stub.
 */
function routed(routes: Record<string, () => Response>) {
  return vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input));
    const key = `${url.pathname}${url.search}`;
    const handler = routes[key] ?? routes[url.pathname];
    if (!handler) {
      throw new Error(`Unexpected call ${key}`);
    }

    return handler();
  });
}

/**
 * Finds the headers a stubbed fetch was called with for a path.
 *
 * @param fetchMock - The stub.
 * @param pathname - The API path.
 * @returns The headers.
 */
function headersFor(fetchMock: ReturnType<typeof vi.fn>, pathname: string): Headers {
  const call = fetchMock.mock.calls.find(([input]) => new URL(String(input)).pathname === pathname);
  if (!call) {
    throw new Error(`No call to ${pathname}`);
  }

  return new Headers((call[1] as RequestInit | undefined)?.headers);
}

describe("loadHome (P-1 on real data)", () => {
  it("asks the API for one page, filtered in SQL, and maps the cards", async () => {
    const fetchMock = routed({
      "/public/players": () => Response.json({ items: [apiCard], page: 2, pageSize: HOME_PAGE_SIZE, totalCount: 25, totalPages: 2 }),
    });

    const data = await loadHome(API, { q: " tehr ", position: "OPP", page: "2" }, undefined, fetchMock, "203.0.113.9");

    const url = new URL(String(fetchMock.mock.calls[0][0]));
    expect(url.origin + url.pathname).toBe("http://api:8080/public/players");
    expect(url.searchParams.get("search")).toBe("tehr");
    expect(url.searchParams.get("position")).toBe("OPP");
    expect(url.searchParams.get("page")).toBe("2");
    expect(url.searchParams.get("pageSize")).toBe(String(HOME_PAGE_SIZE));
    expect(headersFor(fetchMock, "/public/players").get("X-Forwarded-For")).toBe("203.0.113.9");
    expect(data).toMatchObject({ query: "tehr", position: "OPP", page: 2, totalCount: 25, totalPages: 2, viewer: null });
    expect(data.players).toEqual([
      {
        slug: "arash-tehrani",
        name: "Arash Tehrani",
        position: "OPP",
        nationality: "Iran",
        videos: 12,
        documents: 3,
        photoUrl: "/api/public/players/arash-tehrani/photo",
      },
    ]);
  });

  it("sends no filter for All, an empty search or a bad page, and leaves photoUrl out when there is no photo", async () => {
    const fetchMock = routed({
      "/public/players": () => Response.json({ items: [{ ...apiCard, hasPhoto: false }], page: 1, pageSize: HOME_PAGE_SIZE, totalCount: 1, totalPages: 1 }),
    });

    const data = await loadHome(API, { q: "  ", position: "All", page: "-3" }, undefined, fetchMock);

    const url = new URL(String(fetchMock.mock.calls[0][0]));
    expect(url.searchParams.has("search")).toBe(false);
    expect(url.searchParams.has("position")).toBe(false);
    expect(url.searchParams.get("page")).toBe("1");
    expect(data.position).toBe("All");
    expect(data.players[0].photoUrl).toBeUndefined();
  });

  it("reads the signed-in viewer with the account cookie's token", async () => {
    const fetchMock = routed({
      "/public/players": () => Response.json({ items: [], page: 1, pageSize: HOME_PAGE_SIZE, totalCount: 0, totalPages: 0 }),
      "/public/viewer": () => Response.json({ email: "scout@aurora-volley.example", expiresAt: "2026-11-13T09:00:00+00:00" }),
    });

    const data = await loadHome(API, {}, TOKEN, fetchMock);

    expect(data.viewer).toEqual({ email: "scout@aurora-volley.example" });
    expect(headersFor(fetchMock, "/public/viewer").get("X-Viewer-Account")).toBe(TOKEN);
    expect(headersFor(fetchMock, "/public/players").get("X-Viewer-Account")).toBeNull();
  });

  it("treats an ended session as signed out", async () => {
    const fetchMock = routed({
      "/public/players": () => Response.json({ items: [], page: 1, pageSize: HOME_PAGE_SIZE, totalCount: 0, totalPages: 0 }),
      "/public/viewer": () => Response.json({ status: 401, code: "viewer.sign_in_required" }, { status: 401 }),
    });

    expect((await loadHome(API, {}, TOKEN, fetchMock)).viewer).toBeNull();
  });

  it("throws when the API fails, so the page shows its error", async () => {
    const fetchMock = routed({ "/public/players": () => new Response("boom", { status: 500 }) });

    await expect(loadHome(API, {}, undefined, fetchMock)).rejects.toThrow();
  });
});

describe("loadLockedPage (P-2 on real data)", () => {
  const apiPlayer = {
    slug: "arash-tehrani",
    name: "Arash Tehrani",
    firstName: "Arash",
    position: { code: "OPP", label: "Opposite" },
    nationality: { code: "IR", name: "Iran" },
    volleyboxUrl: "https://volleybox.net/arash-tehrani-p81001",
    hasPhoto: true,
    stats: { videos: 2, minutes: 64, documents: 1 },
  };

  it("builds the locked page from the player, the videos and the documents", async () => {
    const fetchMock = routed({
      "/public/players/arash-tehrani": () => Response.json(apiPlayer),
      "/public/players/arash-tehrani/videos": () =>
        Response.json({ items: [{ title: "Highlights 2025", tag: "Highlight", durationSeconds: 84 }, { title: "Full match", tag: "Full match", durationSeconds: 3730 }], page: 1, pageSize: 100, totalCount: 2, totalPages: 1 }),
      "/public/players/arash-tehrani/documents": () => Response.json({ items: [{ title: "Season stats", pageCount: 4 }], page: 1, pageSize: 100, totalCount: 1, totalPages: 1 }),
    });

    const result = await loadLockedPage(API, "arash-tehrani", undefined, fetchMock);

    expect(result.kind).toBe("locked");
    if (result.kind !== "locked") {
      return;
    }

    expect(result.data.player).toMatchObject({
      slug: "arash-tehrani",
      name: "Arash Tehrani",
      position: "OPP",
      nationality: "Iran",
      videos: 2,
      documents: 1,
      minutes: 64,
      volleyboxUrl: "https://volleybox.net/arash-tehrani-p81001",
      photoUrl: "/api/public/players/arash-tehrani/photo",
    });
    expect(result.data.videos).toEqual([
      { title: "Highlights 2025", tag: "Highlight", duration: "1:24" },
      { title: "Full match", tag: "Full match", duration: "1:02:10" },
    ]);
    expect(result.data.documents).toEqual([{ title: "Season stats", pages: 4 }]);
    expect(result.data.viewer).toBeNull();
    for (const path of ["/public/players/arash-tehrani/videos", "/public/players/arash-tehrani/documents"]) {
      const call = fetchMock.mock.calls.find(([input]) => new URL(String(input)).pathname === path)!;
      expect(new URL(String(call[0])).searchParams.get("pageSize")).toBe("100");
    }
  });

  it("is not found for an unknown or unpublished slug", async () => {
    const fetchMock = routed({ "/public/players/nobody": () => Response.json({ status: 404 }, { status: 404 }) });

    expect(await loadLockedPage(API, "nobody", undefined, fetchMock)).toEqual({ kind: "not-found" });
  });

  it("does not call the API for a slug that cannot exist", async () => {
    const fetchMock = vi.fn();

    expect(await loadLockedPage(API, "../admin", undefined, fetchMock)).toEqual({ kind: "not-found" });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("forwardPublic (request-access routes)", () => {
  it("refuses a cross-site request without calling the API", async () => {
    const fetchMock = vi.fn();

    const response = await forwardPublic(browserRequest("/api/public/access-code", { origin: "https://evil.example", body: { email: "a@b.co" } }), API, "access-code", fetchMock);

    expect(response.status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a body that is not JSON", async () => {
    const fetchMock = vi.fn();

    const response = await forwardPublic(browserRequest("/api/public/access-requests", { contentType: "text/plain", body: "x" }), API, "access-requests", fetchMock);

    expect(response.status).toBe(415);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("forwards the code request with the viewer's address and passes the answer through", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ email: "j***@gmail.com", resendAfterSeconds: 60, expiresInSeconds: 600 }, { status: 202 }));

    const response = await forwardPublic(browserRequest("/api/public/access-code", { body: { email: "j.demir@gmail.com" }, forwardedFor: "203.0.113.9, 10.0.0.2" }), API, "access-code", fetchMock);

    expect(response.status).toBe(202);
    expect(await response.json()).toEqual({ email: "j***@gmail.com", resendAfterSeconds: 60, expiresInSeconds: 600 });
    expect(fetchMock.mock.calls[0][0]).toBe("http://api:8080/public/access-code");
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe("POST");
    expect(init.body).toBe(JSON.stringify({ email: "j.demir@gmail.com" }));
    const headers = new Headers(init.headers);
    expect(headers.get("X-Forwarded-For")).toBe("203.0.113.9");
    expect(headers.get("X-Viewer-Account")).toBeNull();
  });

  it("sends the signed-in viewer's token with an access request, never in the body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ status: "pending" }, { status: 201 }));

    const response = await forwardPublic(
      browserRequest("/api/public/access-requests", { body: { playerSlug: "arash-tehrani", about: "Scout." }, cookie: `other=1; ${ACCOUNT_COOKIE}=${TOKEN}` }),
      API,
      "access-requests",
      fetchMock,
    );

    expect(response.status).toBe(201);
    expect(fetchMock.mock.calls[0][0]).toBe("http://api:8080/public/access-requests");
    expect(new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers).get("X-Viewer-Account")).toBe(TOKEN);
  });

  it("passes refusals through with their ProblemDetails and Retry-After", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ status: 429, code: "email_code.too_many", title: "Wait a moment." }, { status: 429, headers: { "Retry-After": "42", "Content-Type": "application/problem+json" } }),
    );

    const response = await forwardPublic(browserRequest("/api/public/access-code", { body: { email: "j.demir@gmail.com" } }), API, "access-code", fetchMock);

    expect(response.status).toBe(429);
    expect(response.headers.get("Retry-After")).toBe("42");
    expect(response.headers.get("Content-Type")).toBe("application/problem+json");
    expect((await response.json()).code).toBe("email_code.too_many");
  });

  it("answers 502 when the API cannot be reached", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error("ECONNREFUSED"));

    const response = await forwardPublic(browserRequest("/api/public/access-code", { body: { email: "j.demir@gmail.com" } }), API, "access-code", fetchMock);

    expect(response.status).toBe(502);
  });
});

describe("signOut (POST /api/viewer/sign-out)", () => {
  it("ends the session at the API, clears the cookie and goes home", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));

    const response = await signOut(browserRequest("/api/viewer/sign-out", { contentType: "application/x-www-form-urlencoded", body: "", cookie: `${ACCOUNT_COOKIE}=${TOKEN}` }), API, fetchMock);

    expect(fetchMock.mock.calls[0][0]).toBe("http://api:8080/public/viewer/sign-out");
    expect(new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers).get("X-Viewer-Account")).toBe(TOKEN);
    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/");
    const cookie = response.headers.get("Set-Cookie") ?? "";
    expect(cookie).toContain(`${ACCOUNT_COOKIE}=;`);
    expect(cookie).toContain("Max-Age=0");
    expect(cookie).toContain("Path=/");
    expect(cookie).toContain("HttpOnly");
    expect(cookie).toContain("Secure");
  });

  it("clears the cookie even when the API cannot be reached", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error("down"));

    const response = await signOut(browserRequest("/api/viewer/sign-out", { contentType: null, cookie: `${ACCOUNT_COOKIE}=${TOKEN}` }), API, fetchMock);

    expect(response.status).toBe(303);
    expect(response.headers.get("Set-Cookie")).toContain("Max-Age=0");
  });

  it("refuses a cross-site sign-out (CSRF) and keeps the cookie", async () => {
    const fetchMock = vi.fn();

    const response = await signOut(browserRequest("/api/viewer/sign-out", { origin: "https://evil.example", cookie: `${ACCOUNT_COOKIE}=${TOKEN}` }), API, fetchMock);

    expect(response.status).toBe(403);
    expect(response.headers.get("Set-Cookie")).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("does not call the API when there is no cookie", async () => {
    const fetchMock = vi.fn();

    const response = await signOut(browserRequest("/api/viewer/sign-out", { contentType: null }), API, fetchMock);

    expect(response.status).toBe(303);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("forwardPublicPhoto", () => {
  it("streams the photo with its type and the API's cache header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(new Uint8Array([1, 2, 3]), { status: 200, headers: { "Content-Type": "image/webp", "Cache-Control": "public, max-age=3600" } }));

    const response = await forwardPublicPhoto(new Request("http://web:3000/api/public/players/arash-tehrani/photo"), API, "arash-tehrani", fetchMock);

    expect(fetchMock.mock.calls[0][0]).toBe("http://api:8080/public/players/arash-tehrani/photo");
    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toBe("image/webp");
    expect(response.headers.get("Cache-Control")).toBe("public, max-age=3600");
    expect(new Uint8Array(await response.arrayBuffer())).toEqual(new Uint8Array([1, 2, 3]));
  });

  it("is 404 for a missing photo or a slug that cannot exist", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));

    expect((await forwardPublicPhoto(new Request("http://web:3000/x"), API, "ivan-valeev", fetchMock)).status).toBe(404);
    expect((await forwardPublicPhoto(new Request("http://web:3000/x"), API, "a/b", vi.fn())).status).toBe(404);
  });
});
