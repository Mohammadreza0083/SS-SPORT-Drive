/**
 * The email gate of `/r/{code}` on the browser side (backlog L-2). The browser only talks to the site's own
 * `/api/links/{code}/…` routes; they forward to the API and keep the device and session tokens in `HttpOnly` cookies,
 * so no script ever sees them.
 */

/** The cookie that remembers a verified device for 90 days. */
export const DEVICE_COOKIE = "__Host-ssd_dev";

/** The cookie of the one-day viewer session; the edge HLS tokens are bound to it (V-5a). */
export const SESSION_COOKIE = "__Host-ssd_vs";

/** How many digits a code has. */
export const CODE_LENGTH = 6;

/** What the gate card shows before the email is verified, as `GET /links/{code}` answers it. */
export type GateLink = {
  /** The link code. */
  code: string;
  /** One player, or several. */
  type: "Personal" | "Roster";
  /** How many players the link shows. */
  playerCount: number;
  /** The recipient's first email, masked (`m***@lube.it`). */
  recipientHint: string;
  /** The player of a Personal link; `null` for a Roster. */
  player: { name: string; position: string; nationality: { code: string; name: string } } | null;
};

/** Who a viewer session belongs to, as `GET /links/{code}/session` answers it. */
export type ViewerSession = {
  /** The viewer's email, laid over the videos. */
  email: string;
  /** Whether the email is a forward. */
  isForward: boolean;
  /** When the session ends (ISO 8601). */
  expiresAt: string;
};

/** The outcome of asking for a code. */
export type CodeRequest =
  | { kind: "sent"; resendAfterSeconds: number }
  | { kind: "refused"; code: string | null; message: string | null; retryAfterSeconds: number | null };

/**
 * The outcome of typing a code. `pending`: the email is a forward an admin has not kept yet (L-3); the device is
 * remembered, but the link stays closed.
 */
export type CodeCheck =
  | { kind: "verified" }
  | { kind: "pending" }
  | { kind: "refused"; code: string | null; message: string | null; attemptsLeft: number | null };

/** What opening the link from this device gave: a session, a forward still waiting for an admin, or the gate. */
export type DeviceOpening = "opened" | "pending" | "gate";

/** The code the API answers for a forward no admin has kept yet. */
const FORWARD_PENDING = "share_link.forward_pending";

/** The parts of a ProblemDetails body the gate reads. */
type Problem = { code: string | null; message: string | null; attemptsLeft: number | null };

/**
 * Reads the code, title and tries left of a ProblemDetails response.
 *
 * @param response - The error response.
 * @returns What it says; nulls when the body is not ProblemDetails.
 */
async function readProblem(response: Response): Promise<Problem> {
  try {
    const body = (await response.json()) as { code?: unknown; title?: unknown; attemptsLeft?: unknown };
    return {
      code: typeof body.code === "string" ? body.code : null,
      message: typeof body.title === "string" ? body.title : null,
      attemptsLeft: typeof body.attemptsLeft === "number" ? body.attemptsLeft : null,
    };
  } catch {
    return { code: null, message: null, attemptsLeft: null };
  }
}

/**
 * Posts JSON to one of the gate's same-origin routes.
 *
 * @param linkCode - The link code.
 * @param action - The route's last segment.
 * @param body - The JSON body, or undefined for none.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The response.
 */
function post(linkCode: string, action: "email-code" | "verify" | "session", body: unknown, fetchImpl: typeof fetch): Promise<Response> {
  return fetchImpl(`/api/links/${encodeURIComponent(linkCode)}/${action}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    cache: "no-store",
  });
}

/**
 * Asks for a code for an email.
 *
 * @param linkCode - The link code.
 * @param email - The typed email.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns Sent with the wait before the next code, or why not.
 */
export async function requestCode(linkCode: string, email: string, fetchImpl: typeof fetch = fetch): Promise<CodeRequest> {
  try {
    const response = await post(linkCode, "email-code", { email: email.trim() }, fetchImpl);
    if (response.ok) {
      const body = (await response.json()) as { resendAfterSeconds: number };
      return { kind: "sent", resendAfterSeconds: body.resendAfterSeconds };
    }

    const retryAfter = Number.parseInt(response.headers.get("Retry-After") ?? "", 10);
    const { code, message } = await readProblem(response);
    return { kind: "refused", code, message, retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : null };
  } catch {
    return { kind: "refused", code: null, message: null, retryAfterSeconds: null };
  }
}

/**
 * Types a code; on success the route has set the device and session cookies (only the device cookie for a forward
 * that waits for an admin).
 *
 * @param linkCode - The link code.
 * @param email - The email the code went to.
 * @param code - The six digits.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns Verified, pending approval, or why not.
 */
export async function verifyCode(linkCode: string, email: string, code: string, fetchImpl: typeof fetch = fetch): Promise<CodeCheck> {
  try {
    const response = await post(linkCode, "verify", { email: email.trim(), code }, fetchImpl);
    if (!response.ok) {
      return { kind: "refused", ...(await readProblem(response)) };
    }

    const body = (await response.json()) as { awaitingApproval?: unknown };
    return body.awaitingApproval === true ? { kind: "pending" } : { kind: "verified" };
  } catch {
    return { kind: "refused", code: null, message: null, attemptsLeft: null };
  }
}

/**
 * Opens the link from a device verified before (or a link that skips the gate); on success the route has set the
 * session cookie.
 *
 * @param linkCode - The link code.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns `opened`, `pending` for a forward still waiting for an admin (L-3), or `gate` when the email gate is needed.
 */
export async function openWithDevice(linkCode: string, fetchImpl: typeof fetch = fetch): Promise<DeviceOpening> {
  try {
    const response = await post(linkCode, "session", undefined, fetchImpl);
    if (response.ok) {
      return "opened";
    }

    return response.status === 403 && (await readProblem(response)).code === FORWARD_PENDING ? "pending" : "gate";
  } catch {
    return "gate";
  }
}

/**
 * Keeps the digits of a typed or pasted code, at most six.
 *
 * @param value - What was typed.
 * @returns The digits.
 */
export function codeDigits(value: string): string {
  return value.replace(/\D/g, "").slice(0, CODE_LENGTH);
}

/**
 * Formats a countdown as `m:ss`.
 *
 * @param seconds - Seconds left; below zero shows `0:00`.
 * @returns The text.
 */
export function formatCountdown(seconds: number): string {
  const left = Math.max(0, Math.ceil(seconds));
  return `${Math.floor(left / 60)}:${String(left % 60).padStart(2, "0")}`;
}
