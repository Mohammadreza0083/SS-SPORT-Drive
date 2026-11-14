/**
 * The console's view of the admin session. Signing in and out is W-1 (`lib/auth/sign-in-server`, the `/admin/sign-in`
 * page and the `/api/auth/*` routes); the session lives in the `HttpOnly` cookie {@link ADMIN_COOKIE} and server code
 * sends it to the API as {@link ADMIN_HEADER}. This file only reads who is signed in and when the session ends.
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
