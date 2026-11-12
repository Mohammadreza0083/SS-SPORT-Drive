/**
 * Edge media tokens (backlog V-5a for HLS, V-5b for document pages). The API signs them (HmacHlsTokenIssuer in .NET);
 * this file checks them. One token format serves two kinds of URL: `/hls/{token}/{folder}/{file}` and
 * `/pages/{token}/{folder}/{n}.webp`; a token works only on its own kind, by the folder it was signed for.
 * Format, pinned by contracts/hls-token-vectors.json:
 *   token = base64url(JSON {v, p, s, e}) + "." + base64url(HMAC-SHA256(key, first part))
 *   p = the video's HLS folder, s = base64url(first 16 bytes of SHA-256(session cookie)), e = expiry (Unix seconds)
 *   URL = /hls/{token}/{p}/{file}   or   /pages/{token}/{p}/{n}.webp
 */

/** Name of the viewer's session cookie (set by the site after the email gate, L-2). */
export const SESSION_COOKIE = "__Host-ssd_vs";

/** The token format version this worker understands. */
const FORMAT_VERSION = 1;

/** File names the edge serves: HLS playlists, MPEG-TS and fMP4 segments, and an fMP4 init segment. */
const SERVABLE_FILE = /^(?:[A-Za-z0-9_-]+\.(?:m3u8|ts|m4s)|init\.mp4)$/;

/** The page images the edge serves: `1.webp`, `2.webp`, and so on (no leading zero, at most four digits). */
const PAGE_FILE = /^[1-9][0-9]{0,3}\.webp$/;

/** What each kind of URL may serve: the end of the token's folder and the file names. */
const KINDS = {
  hls: { folderEnd: "/hls", file: SERVABLE_FILE },
  pages: { folderEnd: "/pages", file: PAGE_FILE },
} as const;

/** The kind of media URL a token is used on. */
export type MediaKind = keyof typeof KINDS;

/** A safe path segment inside a video's HLS folder or a document's pages folder. */
const SAFE_SEGMENT = /^[A-Za-z0-9_.-]+$/;

/** The parts of a verified token the worker needs. */
type TokenPayload = { v: number; p: string; s: string; e: number };

/**
 * Decodes base64url (no padding) to bytes.
 *
 * @param value - The encoded text.
 * @returns The bytes, or null when the text is not base64url.
 */
function base64UrlDecode(value: string): Uint8Array | null {
  if (!/^[A-Za-z0-9_-]*$/.test(value)) {
    return null;
  }
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
  try {
    return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
  } catch {
    return null;
  }
}

/**
 * Encodes bytes as base64url without padding.
 *
 * @param bytes - The bytes.
 * @returns The encoded text.
 */
function base64UrlEncode(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/**
 * The session hash a token carries for a cookie value.
 *
 * @param cookie - The session cookie value.
 * @returns base64url of the first 16 bytes of its SHA-256.
 */
async function sessionHash(cookie: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(cookie)));
  return base64UrlEncode(digest.slice(0, 16));
}

/**
 * Reads one cookie from a Cookie header.
 *
 * @param header - The Cookie header, or null.
 * @param name - The cookie name.
 * @returns The value, or null when absent.
 */
export function readCookie(header: string | null, name: string): string | null {
  for (const part of (header ?? "").split(";")) {
    const [key, ...rest] = part.trim().split("=");
    if (key === name && rest.length > 0) {
      return rest.join("=");
    }
  }
  return null;
}

/** What the worker needs to check one HLS request. */
export type HlsRequest = {
  /** Which URL the token is used on: HLS files or page images. */
  kind: MediaKind;
  /** The token from the URL. */
  token: string;
  /** The object key after the token, e.g. `players/…/hls/720p/seg_00042.ts`. */
  objectKey: string;
  /** The viewer's session cookie value, or null. */
  sessionCookie: string | null;
  /** The current time, Unix seconds. */
  nowUnix: number;
  /** The shared signing key, base64. */
  signingKeyBase64: string;
};

/**
 * Checks a request against its token: the signature, the expiry, the viewer's session, that the file is inside the
 * token's video folder, and that it is an HLS file type. Any failure means "no".
 *
 * @param request - The request to check.
 * @returns True when the file may be served.
 */
export async function isAllowed(request: HlsRequest): Promise<boolean> {
  const { kind, token, objectKey, sessionCookie, nowUnix, signingKeyBase64 } = request;
  if (!sessionCookie) {
    return false;
  }

  const [body, signature, extra] = token.split(".");
  if (!body || !signature || extra !== undefined) {
    return false;
  }
  const signatureBytes = base64UrlDecode(signature);
  const keyBytes = Uint8Array.from(atob(signingKeyBase64), (c) => c.charCodeAt(0));
  if (!signatureBytes || keyBytes.length < 32) {
    return false;
  }

  const key = await crypto.subtle.importKey("raw", keyBytes, { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
  // crypto.subtle.verify compares in constant time.
  if (!(await crypto.subtle.verify("HMAC", key, signatureBytes, new TextEncoder().encode(body)))) {
    return false;
  }

  let payload: TokenPayload;
  try {
    payload = JSON.parse(new TextDecoder().decode(base64UrlDecode(body) ?? new Uint8Array())) as TokenPayload;
  } catch {
    return false;
  }
  if (payload.v !== FORMAT_VERSION || typeof payload.p !== "string" || typeof payload.e !== "number" || nowUnix >= payload.e) {
    return false;
  }
  // A token signed for a video's folder never opens page images, nor the other way round.
  if (!payload.p.endsWith(KINDS[kind].folderEnd)) {
    return false;
  }
  if (payload.s !== (await sessionHash(sessionCookie))) {
    return false;
  }

  const segments = objectKey.split("/");
  const fileName = segments.at(-1) ?? "";
  return (
    objectKey.startsWith(`${payload.p}/`) &&
    segments.every((segment) => SAFE_SEGMENT.test(segment) && segment !== "." && segment !== "..") &&
    KINDS[kind].file.test(fileName)
  );
}

/**
 * The content type for an HLS file or a page image.
 *
 * @param objectKey - The file's key.
 * @returns The MIME type.
 */
export function contentType(objectKey: string): string {
  if (objectKey.endsWith(".m3u8")) {
    return "application/vnd.apple.mpegurl";
  }
  if (objectKey.endsWith(".ts")) {
    return "video/mp2t";
  }
  if (objectKey.endsWith(".webp")) {
    return "image/webp";
  }
  return "video/mp4";
}
