/**
 * The request-access flow of the public pages on the browser side (backlog P-3 UI, P-7). The browser only calls the
 * site's own `/api/public/…` routes; they forward to the API. The routes themselves come with the backend task.
 */
import type { AccessRequest, AccessSubmit, RequestAccessClient } from "@/components/viewer/public/types";
import { readProblem, type CodeRequest } from "./gate";

/** How long to wait before another code when the answer does not say. */
const DEFAULT_RESEND_SECONDS = 60;

/**
 * Posts JSON to one of the public routes.
 *
 * @param path - The route.
 * @param body - The JSON body.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The response.
 */
function post(path: string, body: unknown, fetchImpl: typeof fetch): Promise<Response> {
  return fetchImpl(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
}

/**
 * Asks for a six-digit code for an email.
 *
 * @param email - The typed email.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns Sent with the wait before the next code, or why not.
 */
export async function requestAccessCode(email: string, fetchImpl: typeof fetch = fetch): Promise<CodeRequest> {
  try {
    const response = await post("/api/public/access-code", { email: email.trim() }, fetchImpl);
    if (response.ok) {
      const body = (await response.json().catch(() => ({}))) as { resendAfterSeconds?: unknown };
      return { kind: "sent", resendAfterSeconds: typeof body.resendAfterSeconds === "number" ? body.resendAfterSeconds : DEFAULT_RESEND_SECONDS };
    }

    const retryAfter = Number.parseInt(response.headers.get("Retry-After") ?? "", 10);
    const { code, message } = await readProblem(response);
    return { kind: "refused", code, message, retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : null };
  } catch {
    return { kind: "refused", code: null, message: null, retryAfterSeconds: null };
  }
}

/**
 * Sends an access request.
 *
 * @param request - The player, the description and, for a signed-out visitor, the verified email and code.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns Sent, or why the request was refused.
 */
export async function submitAccessRequest(request: AccessRequest, fetchImpl: typeof fetch = fetch): Promise<AccessSubmit> {
  try {
    const response = await post("/api/public/access-requests", request, fetchImpl);
    if (response.ok) {
      return { kind: "sent" };
    }

    return { kind: "refused", ...(await readProblem(response)) };
  } catch {
    return { kind: "refused", code: null, message: null, attemptsLeft: null };
  }
}

/** The client the locked page uses by default. */
export const publicAccessClient: RequestAccessClient = {
  requestCode: (email) => requestAccessCode(email),
  submit: (request) => submitAccessRequest(request),
};
