import { DEVICE_COOKIE, SESSION_COOKIE, type GateLink, type ViewerSession } from "./gate";

/**
 * The server side of the email gate (backlog L-2): the same-origin `/api/links/{code}/…` routes and the `/r/{code}`
 * page call the API from here. Tokens travel to the API in headers and back to the browser only as `HttpOnly`,
 * `Secure`, `SameSite=Lax`, `__Host-` cookies (decisions §1b).
 */

/** The header the API reads the device token from. */
const DEVICE_HEADER = "X-Viewer-Device";

/** The header the API reads the session token from. */
const SESSION_HEADER = "X-Viewer-Session";

/** The gate actions the routes forward. */
export type GateAction = "email-code" | "verify" | "session";

/** The tokens the API hands out at the gate. */
type ViewerAccess = {
  deviceToken: string | null;
  deviceExpiresAt: string | null;
  sessionToken: string;
  sessionExpiresAt: string;
};

/**
 * Builds an RFC 9457 ProblemDetails response, like the API's own errors.
 *
 * @param status - The HTTP status.
 * @param title - A short summary.
 * @returns The response.
 */
function problem(status: number, title: string): Response {
  return Response.json({ status, title }, { status, headers: { "Content-Type": "application/problem+json" } });
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

/**
 * The CSRF check of the cookie-carrying gate routes: the browser's `Origin` must be this site. Behind the reverse
 * proxy the public host arrives as `X-Forwarded-Host`.
 *
 * @param request - The incoming request.
 * @returns True when the request comes from this site.
 */
function isSameOrigin(request: Request): boolean {
  const origin = request.headers.get("Origin");
  const host = request.headers.get("X-Forwarded-Host") ?? request.headers.get("Host");
  if (!origin || !host) {
    return false;
  }

  try {
    return new URL(origin).host === host.split(",")[0].trim();
  } catch {
    return false;
  }
}

/**
 * Builds a `Set-Cookie` value for a gate cookie.
 *
 * @param name - The cookie name.
 * @param value - The token.
 * @param expiresAt - When it ends (ISO 8601).
 * @param now - The current time in milliseconds.
 * @returns The header value.
 */
function gateCookie(name: string, value: string, expiresAt: string, now: number): string {
  const maxAge = Math.max(0, Math.round((Date.parse(expiresAt) - now) / 1000));
  return `${name}=${value}; Path=/; Max-Age=${maxAge}; HttpOnly; Secure; SameSite=Lax`;
}

/**
 * Forwards a gate route to the API (`POST /api/links/{code}/email-code|verify|session`). Cross-origin and non-JSON
 * requests are refused before anything is sent. The device cookie goes to the API as a header; tokens that come back
 * become cookies and are left out of the body the browser sees.
 *
 * @param request - The browser's request.
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code from the URL.
 * @param action - Which gate step.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @param now - The current time in milliseconds (tests pass a fixed one).
 * @returns The answer for the browser.
 */
export async function forwardGate(
  request: Request,
  apiBaseUrl: string,
  linkCode: string,
  action: GateAction,
  fetchImpl: typeof fetch = fetch,
  now: number = Date.now(),
): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  const hasBody = action !== "session";
  if (hasBody && !(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) {
    return problem(415, "Send JSON.");
  }

  const headers = new Headers();
  if (hasBody) {
    headers.set("Content-Type", "application/json");
  }

  const device = readCookie(request.headers.get("Cookie"), DEVICE_COOKIE);
  if (device && action !== "email-code") {
    headers.set(DEVICE_HEADER, device);
  }

  let response: Response;
  try {
    response = await fetchImpl(`${apiBaseUrl.replace(/\/+$/, "")}/links/${encodeURIComponent(linkCode)}/${action}`, {
      method: "POST",
      headers,
      body: hasBody ? await request.text() : undefined,
      cache: "no-store",
    });
  } catch {
    return problem(502, "The API is not reachable.");
  }

  if (action !== "email-code" && response.ok) {
    const access = (await response.json()) as ViewerAccess;
    const answer = Response.json({ ok: true });
    if (access.deviceToken && access.deviceExpiresAt) {
      answer.headers.append("Set-Cookie", gateCookie(DEVICE_COOKIE, access.deviceToken, access.deviceExpiresAt, now));
    }

    answer.headers.append("Set-Cookie", gateCookie(SESSION_COOKIE, access.sessionToken, access.sessionExpiresAt, now));
    return answer;
  }

  const passed = new Headers({ "Content-Type": response.headers.get("Content-Type") ?? "application/json" });
  const retryAfter = response.headers.get("Retry-After");
  if (retryAfter) {
    passed.set("Retry-After", retryAfter);
  }

  return new Response(response.body, { status: response.status, headers: passed });
}

/**
 * Reads what the gate card shows (`GET /links/{code}`).
 *
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The link, or null when it does not open.
 * @throws When the API cannot be reached or fails, so the page shows its error rather than "not available".
 */
export async function resolveLink(apiBaseUrl: string, linkCode: string, fetchImpl: typeof fetch = fetch): Promise<GateLink | null> {
  const response = await fetchImpl(`${apiBaseUrl.replace(/\/+$/, "")}/links/${encodeURIComponent(linkCode)}`, { cache: "no-store" });
  if (response.status === 404) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`The API answered ${response.status} for the link.`);
  }

  return (await response.json()) as GateLink;
}

/**
 * Reads who a session belongs to (`GET /links/{code}/session`).
 *
 * @param apiBaseUrl - Where the API is.
 * @param linkCode - The link code.
 * @param sessionToken - The session cookie's value, if any.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The session, or null when there is none for this link.
 */
export async function readSession(apiBaseUrl: string, linkCode: string, sessionToken: string | undefined, fetchImpl: typeof fetch = fetch): Promise<ViewerSession | null> {
  if (!sessionToken) {
    return null;
  }

  const response = await fetchImpl(`${apiBaseUrl.replace(/\/+$/, "")}/links/${encodeURIComponent(linkCode)}/session`, {
    headers: { [SESSION_HEADER]: sessionToken },
    cache: "no-store",
  });
  return response.ok ? ((await response.json()) as ViewerSession) : null;
}
