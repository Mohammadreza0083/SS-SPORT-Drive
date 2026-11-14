import { clientAddress, isSameOrigin, problem, readCookie } from "@/lib/viewer/gate-server";

/**
 * Admin sign-in on the web (W-1 admin half, owner 2026-11-14). The API (A-1) mails a one-time link to
 * `/admin/sign-in?token=…`; that page only shows a Continue button, so a mail scanner that opens the link cannot use it
 * up. The button posts the token to `/api/admin/session`, which trades it at `POST /auth/admin/session` and keeps the
 * session token in the `HttpOnly` cookie {@link ADMIN_COOKIE}. The browser never sees the token again; server code sends it
 * to the API as {@link ADMIN_HEADER}. Every route here answers same-origin requests only (CSRF).
 */

/** The cookie holding an admin's session token. */
export const ADMIN_COOKIE = "__Host-ssd_as";

/** The header the API reads the admin session token from. */
export const ADMIN_HEADER = "X-Admin-Session";

/** Who is signed in to the console. */
export type SignedInAdmin = { email: string; displayName: string | null; expiresAt: string };

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
 * Builds the headers of a JSON call to the API that carries the visitor's address, so the API's per-address limits count each visitor.
 *
 * @param request - The browser's request.
 * @param extra - More headers to send.
 * @returns The headers.
 */
function jsonHeaders(request: Request, extra: Record<string, string> = {}): Headers {
  const headers = new Headers({ "Content-Type": "application/json", ...extra });
  const clientIp = clientAddress(request.headers);
  if (clientIp) {
    headers.set("X-Forwarded-For", clientIp);
  }

  return headers;
}

/**
 * `POST /api/admin/sign-in-link`: asks the API to email a sign-in link. Whatever the API says, the browser is told 202, so
 * nobody can learn which addresses belong to admins; only a rate limit passes through (429).
 *
 * @param request - The browser's request, a JSON body with `email`.
 * @param apiBaseUrl - Where the API is.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns 202, 429, 403 (cross-site) or 415 (not JSON).
 */
export async function requestAdminLink(request: Request, apiBaseUrl: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) {
    return problem(415, "Send the body as application/json.");
  }

  let email = "";
  try {
    const body = (await request.json()) as { email?: unknown };
    email = typeof body.email === "string" ? body.email : "";
  } catch {
    return problem(400, "Send a JSON body with an email.");
  }

  try {
    const answer = await fetchImpl(apiUrl(apiBaseUrl, "/auth/admin/link"), {
      method: "POST",
      headers: jsonHeaders(request),
      body: JSON.stringify({ email }),
      cache: "no-store",
    });
    if (answer.status === 429) {
      const headers = new Headers();
      const retryAfter = answer.headers.get("Retry-After");
      if (retryAfter) {
        headers.set("Retry-After", retryAfter);
      }

      return new Response(null, { status: 429, headers });
    }
  } catch {
    // The visitor is told 202 either way: a failure here must not show which addresses are admins.
  }

  return new Response(null, { status: 202 });
}

/**
 * `POST /api/admin/session`: trades the one-time token for a session and keeps it in the cookie.
 *
 * @param request - The browser's form post with `token`.
 * @param apiBaseUrl - Where the API is.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns A redirect to the console with the cookie set, or back to the sign-in page when the link does not work.
 */
export async function startAdminSession(request: Request, apiBaseUrl: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  const failed = new Response(null, { status: 303, headers: { Location: "/admin/sign-in?error=link" } });
  let token = "";
  try {
    const value = (await request.formData()).get("token");
    token = typeof value === "string" ? value.trim() : "";
  } catch {
    return failed;
  }

  if (!token || token.length > 512) {
    return failed;
  }

  try {
    const answer = await fetchImpl(apiUrl(apiBaseUrl, "/auth/admin/session"), {
      method: "POST",
      headers: jsonHeaders(request),
      body: JSON.stringify({ token }),
      cache: "no-store",
    });
    if (!answer.ok) {
      return failed;
    }

    const session = (await answer.json()) as { sessionToken: string; expiresAt: string };
    const maxAge = Math.max(0, Math.floor((Date.parse(session.expiresAt) - Date.now()) / 1000));
    return new Response(null, {
      status: 303,
      headers: { Location: "/admin", "Set-Cookie": `${ADMIN_COOKIE}=${session.sessionToken}; Path=/; Max-Age=${maxAge}; HttpOnly; Secure; SameSite=Lax` },
    });
  } catch {
    return failed;
  }
}

/**
 * `POST /api/admin/sign-out`: ends the session at the API, clears the cookie and goes to the sign-in page. A POST, so a link
 * prefetch can never sign anyone out.
 *
 * @param request - The browser's form post.
 * @param apiBaseUrl - Where the API is.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns A redirect to the sign-in page that clears the cookie.
 */
export async function adminSignOut(request: Request, apiBaseUrl: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  if (!isSameOrigin(request)) {
    return problem(403, "Cross-site requests are not allowed.");
  }

  const token = readCookie(request.headers.get("Cookie"), ADMIN_COOKIE);
  if (token) {
    try {
      // The API's sign-in endpoints take JSON only (that is their CSRF check), so send an empty JSON body.
      await fetchImpl(apiUrl(apiBaseUrl, "/auth/admin/sign-out"), { method: "POST", headers: jsonHeaders(request, { [ADMIN_HEADER]: token }), body: "{}", cache: "no-store" });
    } catch {
      // The cookie is cleared below either way; the session also ends by itself.
    }
  }

  return new Response(null, {
    status: 303,
    headers: { Location: "/admin/sign-in", "Set-Cookie": `${ADMIN_COOKIE}=; Path=/; Max-Age=0; HttpOnly; Secure; SameSite=Lax` },
  });
}

/**
 * Reads who is signed in. A missing, ended or unreadable session is just "nobody".
 *
 * @param apiBaseUrl - Where the API is.
 * @param token - The session cookie's value, if any.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The admin, or null.
 */
export async function readAdmin(apiBaseUrl: string, token: string | undefined, fetchImpl: typeof fetch = fetch): Promise<SignedInAdmin | null> {
  if (!token) {
    return null;
  }

  try {
    const answer = await fetchImpl(apiUrl(apiBaseUrl, "/auth/admin/me"), { headers: { [ADMIN_HEADER]: token }, cache: "no-store" });
    if (!answer.ok) {
      return null;
    }

    const body = (await answer.json()) as { email: string; displayName?: string | null; expiresAt: string };
    return { email: body.email, displayName: body.displayName ?? null, expiresAt: body.expiresAt };
  } catch {
    return null;
  }
}
