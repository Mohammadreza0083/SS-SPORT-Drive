import { SESSION_COOKIE, contentType, isAllowed, readCookie } from "./hls";

/**
 * SS-SPORT Drive edge worker.
 * - `/hls/{token}/{key}`: signed HLS delivery from R2 (V-5a). The token must be valid, unexpired, for this video's
 *   folder and for this viewer's session cookie, so a copied playlist URL does not play anywhere else.
 * - Short links (`/r/{code}`) are routed with V-5b once L-1 defines the codes.
 */

/** Bindings and secrets the worker needs (wrangler.toml and `wrangler secret put`). */
export interface Env {
  /** The HLS token signing key, base64; the API's `Edge__HlsSigningKey`. */
  HLS_SIGNING_KEY: string;
  /** The R2 bucket that holds the HLS renditions. */
  MEDIA: R2Bucket;
}

/** How long a viewer's browser may keep a segment: the token's lifetime. */
const SEGMENT_MAX_AGE_SECONDS = 1800;

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
 * Serves one HLS file after checking its token.
 *
 * @param request - The incoming request.
 * @param env - The worker's bindings.
 * @param token - The token from the URL.
 * @param objectKey - The R2 key after the token.
 * @returns The file, or a refusal.
 */
async function serveHls(request: Request, env: Env, token: string, objectKey: string): Promise<Response> {
  if (request.method !== "GET" && request.method !== "HEAD") {
    return refuse(405, "Method not allowed");
  }

  const allowed = await isAllowed({
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
      return serveHls(request, env, hls[1] ?? "", hls[2] ?? "");
    }

    return refuse(404, "Not found");
  },
} satisfies ExportedHandler<Env>;

export default worker;
