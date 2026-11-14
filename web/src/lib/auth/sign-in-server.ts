import { clientAddress, isSameOrigin, problem, readCookie } from "@/lib/viewer/gate-server";
import { ACCOUNT_COOKIE, apiUrl } from "@/lib/viewer/public-server";

/**
 * The server side of magic-link sign-in (backlog W-1, the web part of A-1 and P-5; decisions §7a and §8a). The emailed
 * link opens `/sign-in?token=…` or `/admin/sign-in?token=…`; its Continue button posts the token to the same-origin
 * `/api/auth/{audience}/session` route, which turns it into a session at the API and keeps the session token only in an
 * `HttpOnly` `__Host-` cookie. The API never reads cookies: the web app forwards them as `X-Viewer-Account` or
 * `X-Admin-Session`.
 */

/** Who signs in. */
export type Audience = "viewer" | "admin";

/** The cookie holding an admin's console session token. */
export const ADMIN_COOKIE = "__Host-ssd_as";

/** How long a viewer's session cookie lasts: 7 days (decisions §7a). */
export const VIEWER_SESSION_SECONDS = 7 * 24 * 60 * 60;

/** How long an admin's session cookie lasts: 30 days (decisions §7a). */
export const ADMIN_SESSION_SECONDS = 30 * 24 * 60 * 60;

/** The header the API reads an admin's session token from. */
const ADMIN_HEADER = "X-Admin-Session";

/** The longest one-time token the form may carry; anything longer cannot be one of ours. */
const MAX_TOKEN_LENGTH = 200;

/** Who is signed in to the console. */
export type SignedInAdmin = { email: string; displayName: string };

/**
 * Whether a path segment names an audience.
 *
 * @param value - The segment from the address.
 * @returns True for `viewer` and `admin`.
 */
export function isAudience(value: string): value is Audience {
  return value === "viewer" || value === "admin";
}

/**
 * The page a visitor of an audience signs in on.
 *
 * @param audience - Who signs in.
 * @returns The path.
 */
function signInPath(audience: Audience): string {
  return audience === "admin" ? "/admin/sign-in" : "/sign-in";
}

/**
 * Builds a redirect that answers a form post (303, so the browser follows it with a GET).
 *
 * @param location - Where to go.
 * @param cookie - A `Set-Cookie` value, if any.
 * @returns The response.
 */
function redirect(location: string, cookie?: string): Response {
  const headers = new Headers({ Location: location });
  if (cookie) {
    headers.set("Set-Cookie", cookie);
  }

  return new Response(null, { status: 303, headers });
}

/**
 * Builds the headers of a JSON call to the API, with the visitor's address so the API's per-address limits count each person.
 *
 * @param request - The browser's request.
 * @returns The headers.
 */
function jsonHeaders(request: Request): Headers {
  const headers = new Headers({ "Content-Type": "application/json" });
  const clientIp = clientAddress(request.headers);
  if (clientIp) {
    headers.set("X-Forwarded-For", clientIp);
  }

  return headers;
}

/**
 * Forwards `POST /api/auth/{audience}/link` to the API: sends the sign-in link to the email. Cross-origin and non-JSON
 * requests are refused before anything is sent. The API answers 202 for every well-formed email, so the answer says
 * nothing about whether the address belongs to an admin; its 429 and `Retry-After` pass through.
 *
 * @param request - The browser's request, with `{ email }` as JSON.
 * @param apiBaseUrl - Where the API is.
 * @param audience - Who signs in.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The answer for the browser.
 */
export async function requestSignInLink(request: Request, apiBaseUrl: string, audience: Audience, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) {
    return problem(415, "Send JSON.");
  }

  let response: Response;
  try {
    response = await fetchImpl(apiUrl(apiBaseUrl, `/auth/${audience}/link`), {
      method: "POST",
      headers: jsonHeaders(request),
      body: await request.text(),
      cache: "no-store",
    });
  } catch {
    return problem(502, "The API is not reachable.");
  }

  const passed = new Headers({ "Content-Type": response.headers.get("Content-Type") ?? "application/json" });
  const retryAfter = response.headers.get("Retry-After");
  if (retryAfter) {
    passed.set("Retry-After", retryAfter);
  }

  return new Response(response.body, { status: response.status, headers: passed });
}

/**
 * Builds the `Set-Cookie` value of a session cookie.
 *
 * @param audience - Who signed in.
 * @param token - The session token.
 * @returns The header value. Viewers use `SameSite=Lax` (they arrive from an emailed link); admins use `Strict`.
 */
function sessionCookie(audience: Audience, token: string): string {
  return audience === "admin"
    ? `${ADMIN_COOKIE}=${token}; Path=/; Max-Age=${ADMIN_SESSION_SECONDS}; HttpOnly; Secure; SameSite=Strict`
    : `${ACCOUNT_COOKIE}=${token}; Path=/; Max-Age=${VIEWER_SESSION_SECONDS}; HttpOnly; Secure; SameSite=Lax`;
}

/**
 * Handles `POST /api/auth/{audience}/session`, the Continue button of the sign-in page: trades the one-time token for
 * a session at the API and sets the cookie. A used, expired or missing token goes back to the sign-in page with
 * `?error=expired`; an API that cannot be reached with `?error=unavailable`. A cross-site post is refused before the
 * token is spent, so another site cannot sign a visitor in to its own account.
 *
 * @param request - The browser's form post (field `token`).
 * @param apiBaseUrl - Where the API is.
 * @param audience - Who signs in.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns A redirect: home or the console on success, the sign-in page otherwise.
 */
export async function startSession(request: Request, apiBaseUrl: string, audience: Audience, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  const expired = redirect(`${signInPath(audience)}?error=expired`);
  let token: string;
  try {
    token = String((await request.formData()).get("token") ?? "").trim();
  } catch {
    return expired;
  }

  if (!token || token.length > MAX_TOKEN_LENGTH) {
    return expired;
  }

  let response: Response;
  try {
    response = await fetchImpl(apiUrl(apiBaseUrl, `/auth/${audience}/session`), {
      method: "POST",
      headers: jsonHeaders(request),
      body: JSON.stringify({ token }),
      cache: "no-store",
    });
  } catch {
    return redirect(`${signInPath(audience)}?error=unavailable`);
  }

  if (response.status >= 500) {
    return redirect(`${signInPath(audience)}?error=unavailable`);
  }

  if (!response.ok) {
    return expired;
  }

  const session = (await response.json()) as { sessionToken?: string };
  if (!session.sessionToken) {
    return redirect(`${signInPath(audience)}?error=unavailable`);
  }

  return redirect(audience === "admin" ? "/admin" : "/", sessionCookie(audience, session.sessionToken));
}

/**
 * Handles `POST /api/admin/sign-out`: ends the admin's session at the API (best effort), clears the cookie and goes to
 * the sign-in page. The same-origin check keeps another site from signing an admin out.
 *
 * @param request - The browser's request (a plain form post).
 * @param apiBaseUrl - Where the API is.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns A redirect that clears the cookie.
 */
export async function signOutAdmin(request: Request, apiBaseUrl: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  const token = readCookie(request.headers.get("Cookie"), ADMIN_COOKIE);
  if (token) {
    // The API's sign-in endpoints take JSON only (that is their CSRF check), so send an empty JSON body.
    const headers = jsonHeaders(request);
    headers.set(ADMIN_HEADER, token);
    try {
      await fetchImpl(apiUrl(apiBaseUrl, "/auth/admin/sign-out"), { method: "POST", headers, body: "{}", cache: "no-store" });
    } catch {
      // The cookie is cleared below either way; the session also ends by itself.
    }
  }

  return redirect("/admin/sign-in", `${ADMIN_COOKIE}=; Path=/; Max-Age=0; HttpOnly; Secure; SameSite=Strict`);
}

/**
 * Reads who is signed in to the console. A missing, ended or unreadable session is nobody.
 *
 * @param apiBaseUrl - Where the API is.
 * @param token - The admin cookie's value, if any.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The admin, or null.
 */
export async function readAdmin(apiBaseUrl: string, token: string | undefined, fetchImpl: typeof fetch = fetch): Promise<SignedInAdmin | null> {
  if (!token) {
    return null;
  }

  try {
    const response = await fetchImpl(apiUrl(apiBaseUrl, "/auth/admin/me"), { headers: { [ADMIN_HEADER]: token }, cache: "no-store" });
    if (!response.ok) {
      return null;
    }

    const admin = (await response.json()) as { email: string; displayName?: string | null };
    return { email: admin.email, displayName: admin.displayName ?? admin.email };
  } catch {
    return null;
  }
}
