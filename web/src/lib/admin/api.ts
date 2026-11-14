import { clientAddress, isSameOrigin, problem, readCookie } from "@/lib/viewer/gate-server";
import { ADMIN_COOKIE, ADMIN_HEADER } from "./session";

/**
 * How the console talks to the API. Server components read with {@link adminFetch}: the session from the cookie goes as
 * `X-Admin-Session` and nothing is cached. Browser actions (move a file, publish, retry, save settings, create a link) go
 * through the same-origin route `/api/admin/[...path]`, which {@link forwardAdmin} guards: same origin only, JSON bodies on
 * writes (CSRF), a session cookie, and only paths under the API's `/admin/`.
 */

/** The API answered with an error status. */
export class AdminApiError extends Error {
  /**
   * Creates the error.
   *
   * @param status - The HTTP status.
   * @param code - The ProblemDetails `code`, or null.
   */
  constructor(
    readonly status: number,
    readonly code: string | null,
  ) {
    super(`The API answered ${status}${code ? ` (${code})` : ""}.`);
    this.name = "AdminApiError";
  }
}

/** The image types an admin may upload as a player photo. */
const PHOTO_TYPES = ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

/**
 * Joins the API's address and a path.
 *
 * @param apiBaseUrl - Where the API is.
 * @param path - The path, starting with a slash.
 * @returns The full address.
 */
function apiUrl(apiBaseUrl: string, path: string): string {
  return `${apiBaseUrl.replace(/\/+$/, "")}${path}`;
}

/**
 * Reads the `code` of a ProblemDetails answer, if there is one.
 *
 * @param answer - The API's answer.
 * @returns The code, or null.
 */
async function problemCode(answer: Response): Promise<string | null> {
  try {
    const body = (await answer.json()) as { code?: unknown };
    return typeof body.code === "string" ? body.code : null;
  } catch {
    return null;
  }
}

/**
 * Calls an admin endpoint of the API as the signed-in admin.
 *
 * @typeParam T - The answer's shape; use `null` for endpoints that answer 204.
 * @param apiBaseUrl - Where the API is.
 * @param token - The admin session token.
 * @param path - The API path with its query, starting with `/admin/`.
 * @param init - The method and JSON body, if not a plain read.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The parsed answer; null for 204.
 * @throws {AdminApiError} When the API does not answer 2xx.
 */
export async function adminFetch<T>(apiBaseUrl: string, token: string, path: string, init: RequestInit = {}, fetchImpl: typeof fetch = fetch): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set(ADMIN_HEADER, token);
  if (init.body !== undefined && init.body !== null) {
    headers.set("Content-Type", "application/json");
  }

  const answer = await fetchImpl(apiUrl(apiBaseUrl, path), { ...init, headers, cache: "no-store" });
  if (!answer.ok) {
    throw new AdminApiError(answer.status, await problemCode(answer));
  }

  if (answer.status === 204 || answer.status === 202) {
    return null as T;
  }

  return (await answer.json()) as T;
}

/**
 * Whether a path segment may be forwarded: not empty, not a dot segment and without a separator, also after decoding.
 *
 * @param segment - The raw segment.
 * @returns True when it is safe to put in the API path.
 */
function isSafeSegment(segment: string): boolean {
  let decoded: string;
  try {
    decoded = decodeURIComponent(segment);
  } catch {
    return false;
  }

  return decoded !== "" && decoded !== "." && decoded !== ".." && !/[/\\\u0000-\u001f]/.test(decoded) && !segment.includes("%2f") && !segment.includes("%2F");
}

/**
 * `/api/admin/[...path]`: forwards a console action to the API's `/admin/…`. The CSRF checks come first; the session cookie
 * becomes the `X-Admin-Session` header and is never sent to the page.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param segments - The path after `/api/admin/`.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The API's answer, or an error answer.
 */
export async function forwardAdmin(request: Request, apiBaseUrl: string, segments: string[], fetchImpl: typeof fetch = fetch): Promise<Response> {
  const read = request.method === "GET" || request.method === "HEAD";
  if (read ? request.headers.get("Sec-Fetch-Site") === "cross-site" : !isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  const contentType = (request.headers.get("Content-Type") ?? "").toLowerCase();
  const isPhoto = request.method === "PUT" && segments.at(-1) === "photo" && PHOTO_TYPES.some((type) => contentType.startsWith(type));
  if (!read && !isPhoto && !contentType.startsWith("application/json")) {
    return problem(415, "Send the body as application/json.");
  }

  const token = readCookie(request.headers.get("Cookie"), ADMIN_COOKIE);
  if (!token) {
    return problem(401, "Sign in first.");
  }

  if (segments.length === 0 || !segments.every(isSafeSegment)) {
    return new Response(null, { status: 404 });
  }

  const headers = new Headers({ [ADMIN_HEADER]: token });
  const clientIp = clientAddress(request.headers);
  if (clientIp) {
    headers.set("X-Forwarded-For", clientIp);
  }

  let body: BodyInit | undefined;
  if (!read) {
    headers.set("Content-Type", isPhoto ? contentType : "application/json");
    body = isPhoto ? await request.arrayBuffer() : await request.text();
  }

  try {
    const path = `/admin/${segments.map((segment) => encodeURIComponent(decodeURIComponent(segment))).join("/")}${new URL(request.url).search}`;
    const answer = await fetchImpl(apiUrl(apiBaseUrl, path), { method: request.method, headers, body, cache: "no-store" });
    const out = new Headers({ "Cache-Control": "no-store" });
    for (const name of ["Content-Type", "Content-Disposition", "Retry-After"]) {
      const value = answer.headers.get(name);
      if (value) {
        out.set(name, value);
      }
    }

    return new Response([204, 202, 205, 304].includes(answer.status) ? null : answer.body, { status: answer.status, headers: out });
  } catch {
    return problem(502, "The server could not be reached.");
  }
}
