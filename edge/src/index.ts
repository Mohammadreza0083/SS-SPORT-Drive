import { SESSION_COOKIE, contentType, isAllowed, readCookie, type MediaKind } from "./hls";

/**
 * SS-SPORT Drive edge worker, in front of the viewer site on `drive.ss-sport.co/*`.
 * - `/hls/{token}/{key}`: signed HLS delivery from R2 (V-5a). The token must be valid, unexpired, for this video's
 *   folder and for this viewer's session cookie, so a copied playlist URL does not play anywhere else.
 * - `/pages/{token}/{key}`: signed document page images from R2 (V-5b), with the same checks.
 * - `/r/{code}`: short links (V-5b). A path that cannot be a link (a code that is not six characters of the link
 *   alphabet, or a player that is not a GUID) is answered 404 here and never reaches the site. Every other path goes on to
 *   the viewer site unchanged, and the site answers its own 404s.
 */
/** Bindings and secrets the worker needs (wrangler.toml and `wrangler secret put`). */
export interface Env {
  /** The HLS token signing key, base64; the API's `Edge__HlsSigningKey`. */
  HLS_SIGNING_KEY: string;
  /** The R2 bucket that holds the HLS renditions. */
  MEDIA: R2Bucket;
}

/** How long a viewer's browser may keep a segment or a page image: the token's lifetime. */
const SEGMENT_MAX_AGE_SECONDS = 1800;

/**
 * What a link code is made of: six characters from `ShareLinkCode.Alphabet` in the API (no 0, O, 1, l or I). Keep the
 * two in step.
 */
const LINK_CODE = /^[ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789]{6}$/;

/** A player id as the API issues them. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * A plain refusal. It never says why, so nothing about tokens or files can be probed.
 *
 * @param status - The HTTP status.
 * @param text - The body.
 * @returns The response.
 */
function refuse(status: number, text: string): Response {
  return new Response(text, { status, headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" } });
}

/**
 * Serves one file from R2 after checking its token: an HLS file or a page image.
 *
 * @param request - The incoming request.
 * @param env - The worker's bindings.
 * @param kind - Which URL the token is used on.
 * @param token - The token from the URL.
 * @param objectKey - The R2 key after the token.
 * @returns The file, or a refusal.
 */
async function serveMedia(request: Request, env: Env, kind: MediaKind, token: string, objectKey: string): Promise<Response> {
  if (request.method !== "GET" && request.method !== "HEAD") {
    return refuse(405, "Method not allowed");
  }

  const allowed = await isAllowed({
    kind,
    token,
    objectKey,
    sessionCookie: readCookie(request.headers.get("Cookie"), SESSION_COOKIE),
    nowUnix: Math.floor(Date.now() / 1000),
    signingKeyBase64: env.HLS_SIGNING_KEY,
  });
  if (!allowed) {
    return refuse(403, "Forbidden");
  }

  const object = await env.MEDIA.get(objectKey);
  if (!object) {
    return refuse(404, "Not found");
  }

  const playlist = objectKey.endsWith(".m3u8");
  const headers = new Headers({
    "Content-Type": contentType(objectKey),
    "Cache-Control": playlist ? "private, no-store" : `private, max-age=${SEGMENT_MAX_AGE_SECONDS}`,
    "X-Content-Type-Options": "nosniff",
    "Content-Length": String(object.size),
    ETag: object.httpEtag,
  });
  return new Response(request.method === "HEAD" ? null : object.body, { status: 200, headers });
}

/**
 * Whether a `/r/...` path can be a link: `/r/{code}` or `/r/{code}/p/{playerId}`, with an optional trailing slash.
 *
 * @param pathname - The request path, which starts with `/r/`.
 * @returns True when the shape and the characters are right.
 */
function isLinkPath(pathname: string): boolean {
  const [, , code, player, id, ...rest] = pathname.replace(/\/$/, "").split("/");
  if (!code || !LINK_CODE.test(code)) {
    return false;
  }
  if (player === undefined) {
    return true;
  }
  return player === "p" && id !== undefined && GUID.test(id) && rest.length === 0;
}

const worker = {
  /**
   * Handles every request that reaches the edge.
   *
   * @param request - The incoming request.
   * @param env - The worker's bindings.
   * @returns The response for the request.
   */
  async fetch(request: Request, env: Env): Promise<Response> {
    const { pathname } = new URL(request.url);

    if (pathname === "/health") {
      return new Response("ok", { status: 200 });
    }

    const hls = /^\/hls\/([^/]+)\/(.+)$/.exec(pathname);
    if (hls) {
      return serveMedia(request, env, "hls", hls[1] ?? "", hls[2] ?? "");
    }

    const pages = /^\/pages\/([^/]+)\/(.+)$/.exec(pathname);
    if (pages) {
      return serveMedia(request, env, "pages", pages[1] ?? "", pages[2] ?? "");
    }

    if (pathname.startsWith("/r/") && !isLinkPath(pathname)) {
      return refuse(404, "Not found");
    }

    // Everything else is the viewer site's: on Cloudflare this goes to the origin behind the route.
    return fetch(request);
  },
} satisfies ExportedHandler<Env>;

export default worker;
