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

/** A country in the nationality picker. */
export interface Country {
  code: string;
  name: string;
}

/** The position codes, in the order the chips show them. */
export const POSITIONS = ["S", "OH", "OPP", "MB", "L", "DS"] as const;

/** A position code. */
export type PositionCode = (typeof POSITIONS)[number];

/** The new-player form as the API takes it. */
export interface NewPlayerForm {
  category: "Men" | "Women";
  name: string;
  nationalityCode: string;
  positions: PositionCode[];
  volleyboxUrl: string;
  driveFolderUrl: string;
}

/** The outcome of checking a Volleybox link. */
export type VolleyboxCheck =
  | { kind: "empty" }
  | { kind: "new"; volleyboxId: number }
  | { kind: "existing"; volleyboxId: number; playerName: string }
  | { kind: "invalid"; code: string | null };

/** The outcome of creating a player. */
export type CreatePlayerResult = { kind: "created"; player: { id: string; name: string; status: string } } | { kind: "rejected"; code: string | null };

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
 * Builds the request options every Mini App call shares: the `tma` Authorization header and no caching.
 *
 * @param initData - The raw initData from Telegram.
 * @returns The request options.
 */
function signedRequest(initData: string): RequestInit {
  return { headers: { Authorization: `tma ${initData}` }, cache: "no-store" };
}

/**
 * Reads the `code` of a ProblemDetails response, if it has one.
 *
 * @param response - The error response.
 * @returns The code, or `null`.
 */
async function problemCode(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { code?: unknown };
    return typeof body.code === "string" ? body.code : null;
  } catch {
    return null;
  }
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
    const response = await fetchImpl(PROFILE_ROUTE, signedRequest(initData));
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
 * Reads the country catalogue for the nationality picker.
 *
 * @param initData - The raw initData from Telegram.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The countries, or an empty list when they cannot be read.
 */
export async function fetchCountries(initData: string, fetchImpl: typeof fetch = fetch): Promise<Country[]> {
  try {
    const response = await fetchImpl("/api/miniapp/countries", signedRequest(initData));
    return response.ok ? ((await response.json()) as Country[]) : [];
  } catch {
    return [];
  }
}

/**
 * Checks a pasted Volleybox link: is it a player page, and does that player already exist?
 *
 * @param url - The pasted link.
 * @param initData - The raw initData from Telegram.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The check result.
 */
export async function checkVolleybox(url: string, initData: string, fetchImpl: typeof fetch = fetch): Promise<VolleyboxCheck> {
  const link = url.trim();
  if (!link) {
    return { kind: "empty" };
  }

  try {
    const response = await fetchImpl(`/api/miniapp/volleybox?url=${encodeURIComponent(link)}`, signedRequest(initData));
    if (!response.ok) {
      return { kind: "invalid", code: await problemCode(response) };
    }

    const body = (await response.json()) as { volleyboxId: number; existingPlayerId: string | null; existingPlayerName: string | null };
    return body.existingPlayerId
      ? { kind: "existing", volleyboxId: body.volleyboxId, playerName: body.existingPlayerName ?? "" }
      : { kind: "new", volleyboxId: body.volleyboxId };
  } catch {
    return { kind: "invalid", code: null };
  }
}

/**
 * Sends the new-player form.
 *
 * @param form - The filled form.
 * @param initData - The raw initData from Telegram.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The created player, or the code of the rule it broke.
 */
export async function createPlayer(form: NewPlayerForm, initData: string, fetchImpl: typeof fetch = fetch): Promise<CreatePlayerResult> {
  try {
    const response = await fetchImpl("/api/miniapp/players", {
      method: "POST",
      headers: { Authorization: `tma ${initData}`, "Content-Type": "application/json" },
      body: JSON.stringify(form),
      cache: "no-store",
    });
    if (response.ok) {
      return { kind: "created", player: (await response.json()) as { id: string; name: string; status: string } };
    }

    return { kind: "rejected", code: await problemCode(response) };
  } catch {
    return { kind: "rejected", code: null };
  }
}

/** The largest photo the API accepts: 15 MB. */
export const MAX_PHOTO_BYTES = 15 * 1024 * 1024;

/** A player's photo after an upload, as the API returns it. */
export interface PlayerPhoto {
  photoKey: string;
  width: number;
  height: number;
  isComplete: boolean;
  missingDetails: string[];
}

/** The outcome of uploading a photo. */
export type PhotoUpload = { kind: "uploaded"; photo: PlayerPhoto } | { kind: "rejected"; code: string | null };

/**
 * Uploads a player's photo: the file itself is the body, with its own content type. Files over 15 MB are refused
 * here, without sending them.
 *
 * @param playerId - The player.
 * @param file - The chosen photo.
 * @param initData - The raw initData from Telegram.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The stored photo, or the code of the rule it broke.
 */
export async function uploadPhoto(playerId: string, file: Blob, initData: string, fetchImpl: typeof fetch = fetch): Promise<PhotoUpload> {
  if (file.size > MAX_PHOTO_BYTES) {
    return { kind: "rejected", code: "player.photo_too_large" };
  }

  try {
    const response = await fetchImpl(`/api/miniapp/players/${encodeURIComponent(playerId)}/photo`, {
      method: "PUT",
      headers: { Authorization: `tma ${initData}`, "Content-Type": file.type || "application/octet-stream" },
      body: file,
      cache: "no-store",
    });
    return response.ok ? { kind: "uploaded", photo: (await response.json()) as PlayerPhoto } : { kind: "rejected", code: await problemCode(response) };
  } catch {
    return { kind: "rejected", code: null };
  }
}

/**
 * Reads a player's photo and makes a local URL for an `<img>`, since an image tag cannot send the `tma` header.
 *
 * @param playerId - The player.
 * @param initData - The raw initData from Telegram.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns An object URL (revoke it when done), or `null` when there is no photo.
 */
export async function fetchPhotoUrl(playerId: string, initData: string, fetchImpl: typeof fetch = fetch): Promise<string | null> {
  try {
    const response = await fetchImpl(`/api/miniapp/players/${encodeURIComponent(playerId)}/photo`, signedRequest(initData));
    return response.ok ? URL.createObjectURL(await response.blob()) : null;
  } catch {
    return null;
  }
}

/** A Drive id: 10 to 100 of A–Z, a–z, 0–9, `-` and `_` (the API's rule). */
const DRIVE_ID = /^[A-Za-z0-9_-]{10,100}$/;

/**
 * Checks that a pasted link points to a Google Drive folder, mirroring the API's `DriveFolderLink`.
 * The API checks again; this only gives instant feedback.
 *
 * @param text - The pasted link.
 * @returns `true` for a Drive folder link or a bare folder id.
 */
export function isDriveFolderLink(text: string): boolean {
  const value = text.trim();
  if (DRIVE_ID.test(value)) {
    return true;
  }

  let url: URL;
  try {
    url = new URL(value.includes("://") ? value : `https://${value}`);
  } catch {
    return false;
  }

  if (url.hostname.toLowerCase() !== "drive.google.com") {
    return false;
  }

  const segments = url.pathname.split("/").filter(Boolean);
  const folders = segments.indexOf("folders");
  if (segments[0] === "drive" && folders > 0) {
    return DRIVE_ID.test(segments[folders + 1] ?? "");
  }

  return url.pathname === "/open" && DRIVE_ID.test(url.searchParams.get("id") ?? "");
}

/**
 * Taps a position chip: the first tap chooses the primary, the second the secondary, a third is ignored, and tapping
 * a chosen position removes it.
 *
 * @param chosen - The positions chosen so far, primary first.
 * @param position - The tapped position.
 * @returns The new choice.
 */
export function togglePosition(chosen: readonly PositionCode[], position: PositionCode): PositionCode[] {
  if (chosen.includes(position)) {
    return chosen.filter((code) => code !== position);
  }

  return chosen.length < 2 ? [...chosen, position] : [...chosen];
}

/**
 * Forwards a Mini App request to one fixed API path, passing on only the method, the Authorization and Content-Type
 * headers, the query string and the body, so the browser never calls the API across origins.
 *
 * @param request - The incoming request.
 * @param apiBaseUrl - Where the Next.js server reaches the API (`API_BASE_URL`).
 * @param apiPath - The API path this route stands for, e.g. `/miniapp/players`.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The API's answer, 401 without an Authorization header, or 502 when the API is unreachable.
 */
export async function forwardToApi(request: Request, apiBaseUrl: string, apiPath: string, fetchImpl: typeof fetch = fetch): Promise<Response> {
  const authorization = request.headers.get("Authorization");
  if (!authorization) {
    return problem(401, "Sign-in is required.");
  }

  const headers: Record<string, string> = { Authorization: authorization };
  const contentType = request.headers.get("Content-Type");
  if (contentType) {
    headers["Content-Type"] = contentType;
  }

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  try {
    const response = await fetchImpl(`${apiBaseUrl.replace(/\/+$/, "")}${apiPath}${new URL(request.url).search}`, {
      method: request.method,
      headers,
      // Bytes, not text: photo uploads are binary.
      body: hasBody ? await request.arrayBuffer() : undefined,
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
