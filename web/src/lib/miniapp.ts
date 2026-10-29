/** The signed-in team member, as `GET /miniapp/me` returns them. */
export interface MiniAppProfile {
  id: string;
  telegramId: number;
  displayName: string;
  username: string | null;
  role: "Analyst" | "Admin";
}

/** The outcome of signing in to the Mini App. */
export type MiniAppSignIn =
  | { kind: "ok"; profile: MiniAppProfile }
  | { kind: "unauthorized" }
  | { kind: "forbidden" }
  | { kind: "error" };

/** The part of `window` the Telegram Mini App script fills in. */
export interface TelegramWindow {
  Telegram?: { WebApp?: { initData?: string } };
}

/** Where the browser asks for the profile: a same-origin route that forwards to the API. */
export const PROFILE_ROUTE = "/api/miniapp/me";

/**
 * Reads the signed initData Telegram hands a Mini App.
 *
 * @param source - The window (or a stand-in) after `telegram-web-app.js` has loaded.
 * @returns The raw initData, or `null` outside Telegram.
 */
export function readInitData(source: TelegramWindow): string | null {
  const initData = source.Telegram?.WebApp?.initData;
  return initData ? initData : null;
}

/**
 * Signs in with the initData and reads who the caller is.
 *
 * @param initData - The raw initData from Telegram.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The profile, or why it could not be read.
 */
export async function fetchMiniAppProfile(initData: string, fetchImpl: typeof fetch = fetch): Promise<MiniAppSignIn> {
  try {
    const response = await fetchImpl(PROFILE_ROUTE, {
      headers: { Authorization: `tma ${initData}` },
      cache: "no-store",
    });
    if (response.ok) {
      return { kind: "ok", profile: (await response.json()) as MiniAppProfile };
    }

    if (response.status === 401) {
      return { kind: "unauthorized" };
    }

    return response.status === 403 ? { kind: "forbidden" } : { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * Forwards a profile request to the API (`GET /miniapp/me`), passing on only the Authorization header, so the
 * browser never calls the API across origins.
 *
 * @param request - The incoming request.
 * @param apiBaseUrl - Where the Next.js server reaches the API (`API_BASE_URL`).
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The API's answer, 401 without an Authorization header, or 502 when the API is unreachable.
 */
export async function forwardProfileRequest(request: Request, apiBaseUrl: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  const authorization = request.headers.get("Authorization");
  if (!authorization) {
    return problem(401, "Sign-in is required.");
  }

  try {
    const response = await fetchImpl(`${apiBaseUrl.replace(/\/+$/, "")}/miniapp/me`, {
      headers: { Authorization: authorization },
      cache: "no-store",
    });
    return new Response(response.body, {
      status: response.status,
      headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
    });
  } catch {
    return problem(502, "The API is not reachable.");
  }
}

/**
 * Builds an RFC 9457 ProblemDetails response, like the API's own errors.
 *
 * @param status - The HTTP status.
 * @param title - A short, human-readable summary.
 * @returns The response.
 */
function problem(status: number, title: string): Response {
  return new Response(JSON.stringify({ status, title }), {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}
