import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import vectors from "../../contracts/hls-token-vectors.json";
import worker, { type Env } from "./index";

/**
 * V-5b on the edge (owner-approved 2026-11-12):
 * - Short links: everything that is not edge media goes on to the viewer site (Next.js) unchanged. A `/r/{code}` path
 *   whose code cannot be a link code (six characters from the link alphabet, no look-alikes) is answered 404 at the
 *   edge and never reaches the site.
 * - Document pages: `/pages/{token}/{folder}/{n}.webp` serves one page image from R2 with the same token rules as HLS
 *   (signature, expiry, session cookie, inside the token's folder), so a copied page URL opens nowhere else.
 */

/** Objects the fake R2 bucket holds, by key. */
const stored = new Map<string, string>([
  ["players/7f1c2a9e/documents/9a0b1c2d/v1/pages/1.webp", "page-one"],
  ["players/7f1c2a9e/documents/9a0b1c2d/v1/pages/12.webp", "page-twelve"],
  ["players/7f1c2a9e/documents/9a0b1c2d/v1/original.pdf", "the-pdf"],
]);

/** Keys the worker asked R2 for. */
const requested: string[] = [];

/** A stand-in for the R2 binding. */
const media = {
  async get(key: string) {
    requested.push(key);
    const body = stored.get(key);
    return body === undefined ? null : { body: new Response(body).body, size: body.length, httpEtag: '"etag"' };
  },
} as unknown as R2Bucket;

/** The environment with the shared key and the fake bucket. */
const env: Env = { HLS_SIGNING_KEY: vectors.signingKeyBase64, MEDIA: media };

/** What the site behind the edge answers (stubbed `fetch`). */
const origin = vi.fn(async (request: Request) => new Response(`site:${new URL(request.url).pathname}`, { status: 200 }));

/** The document folder the page tokens are signed for. */
const FOLDER = "players/7f1c2a9e/documents/9a0b1c2d/v1/pages";

/** The viewer's session cookie value. */
const SESSION = "viewer-session-cookie-value";

/** A fixed "now", Unix seconds. */
const NOW = 1_800_000_000;

/**
 * Encodes bytes as base64url without padding.
 *
 * @param bytes - The bytes.
 * @returns The text.
 */
function base64Url(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/**
 * Signs a token in the edge format, as the .NET issuer does.
 *
 * @param folder - The folder (`p`).
 * @param session - The session cookie the token is bound to.
 * @param expires - The expiry (`e`), Unix seconds.
 * @returns The token.
 */
async function sign(folder: string, session: string, expires: number): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(session)));
  const body = base64Url(new TextEncoder().encode(JSON.stringify({ v: 1, p: folder, s: base64Url(digest.slice(0, 16)), e: expires })));
  const keyBytes = Uint8Array.from(atob(vectors.signingKeyBase64), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("raw", keyBytes, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const signature = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(body)));
  return `${body}.${base64Url(signature)}`;
}

/**
 * Sends a request to the worker at {@link NOW}.
 *
 * @param path - Path on the edge host.
 * @param cookie - The session cookie value, or null for none.
 * @param method - The HTTP method.
 * @returns The worker's response.
 */
async function send(path: string, cookie: string | null = null, method = "GET"): Promise<Response> {
  vi.setSystemTime(NOW * 1000);
  const headers = new Headers();
  if (cookie !== null) {
    headers.set("Cookie", `${vectors.sessionCookieName}=${cookie}`);
  }
  return worker.fetch(new Request(`https://drive.ss-sport.co${path}`, { method, headers }), env);
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal("fetch", origin);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  origin.mockClear();
  requested.length = 0;
});

describe("short links and the site behind the edge", () => {
  it.each(["/r/Ab3xYz", "/r/Ab3xYz/p/0b6f2c1e-1111-4a4a-8888-000000000001", "/", "/?q=Tehrani", "/api/links/Ab3xYz/session", "/_next/static/app.js"])(
    "passes %s on to the site unchanged",
    async (path) => {
      const response = await send(path, SESSION);

      expect(response.status).toBe(200);
      expect(origin).toHaveBeenCalledTimes(1);
      const forwarded = origin.mock.calls[0]?.[0] as Request;
      expect(forwarded.url).toBe(`https://drive.ss-sport.co${path}`);
      expect(forwarded.headers.get("Cookie")).toContain(SESSION);
    },
  );

  it("passes a POST to the site with its method", async () => {
    await send("/api/links/Ab3xYz/session", null, "POST");

    expect((origin.mock.calls[0]?.[0] as Request).method).toBe("POST");
  });

  it.each([
    ["too short", "/r/Ab3xY"],
    ["too long", "/r/Ab3xYzQ"],
    ["look-alike 0", "/r/Ab30Yz"],
    ["look-alike O", "/r/AbOxYz"],
    ["look-alike l", "/r/Ablxyz"],
    ["look-alike 1", "/r/Ab1xYz"],
    ["look-alike I", "/r/AbIxYz"],
    ["symbol", "/r/Ab-xYz"],
    ["bad player path", "/r/Ab3xYz/p/not-a-guid"],
  ])("answers a malformed link (%s) with 404 at the edge", async (_name, path) => {
    const response = await send(path);

    expect(response.status).toBe(404);
    expect(response.headers.get("Cache-Control")).toBe("no-store");
    expect(origin).not.toHaveBeenCalled();
  });

  it("still answers the health probe itself", async () => {
    const response = await send("/health");

    expect(response.status).toBe(200);
    expect(origin).not.toHaveBeenCalled();
  });
});

describe("signed document pages", () => {
  it("serves a page image to the session the token was signed for", async () => {
    const token = await sign(FOLDER, SESSION, NOW + 1800);

    const response = await send(`/pages/${token}/${FOLDER}/12.webp`, SESSION);

    expect(response.status).toBe(200);
    expect(await response.text()).toBe("page-twelve");
    expect(response.headers.get("Content-Type")).toBe("image/webp");
    expect(response.headers.get("Cache-Control")).toBe("private, max-age=1800");
    expect(response.headers.get("X-Content-Type-Options")).toBe("nosniff");
    expect(requested).toEqual([`${FOLDER}/12.webp`]);
    expect(origin).not.toHaveBeenCalled();
  });

  it.each([
    ["no cookie", null, NOW + 1800, `${FOLDER}/1.webp`],
    ["another session", "someone-else", NOW + 1800, `${FOLDER}/1.webp`],
    ["expired", SESSION, NOW, `${FOLDER}/1.webp`],
    ["the original PDF", SESSION, NOW + 1800, "players/7f1c2a9e/documents/9a0b1c2d/v1/original.pdf"],
    ["outside the folder", SESSION, NOW + 1800, "players/7f1c2a9e/documents/9a0b1c2d/v2/pages/1.webp"],
    ["not a page image", SESSION, NOW + 1800, `${FOLDER}/1.png`],
    ["not a page number", SESSION, NOW + 1800, `${FOLDER}/cover.webp`],
    ["a path climb", SESSION, NOW + 1800, `${FOLDER}/../original.pdf`],
  ])("refuses %s with a bare 403 and never reads R2", async (_name, cookie, expires, key) => {
    const token = await sign(FOLDER, SESSION, expires);

    const response = await send(`/pages/${token}/${key}`, cookie);

    expect(response.status).toBe(403);
    expect(await response.text()).toBe("Forbidden");
    expect(requested).toEqual([]);
  });

  it("does not let a page token open HLS files: /hls needs a folder ending in /hls, /pages one ending in /pages", async () => {
    const pageToken = await sign(FOLDER, SESSION, NOW + 1800);

    const hls = await send(`/hls/${pageToken}/${FOLDER}/index.m3u8`, SESSION);

    expect(hls.status).toBe(403);
    expect(requested).toEqual([]);
  });

  it("answers a page that is not stored with 404", async () => {
    const token = await sign(FOLDER, SESSION, NOW + 1800);

    const response = await send(`/pages/${token}/${FOLDER}/2.webp`, SESSION);

    expect(response.status).toBe(404);
  });

  it("allows only GET and HEAD", async () => {
    const token = await sign(FOLDER, SESSION, NOW + 1800);

    const response = await send(`/pages/${token}/${FOLDER}/1.webp`, SESSION, "POST");

    expect(response.status).toBe(405);
  });
});
