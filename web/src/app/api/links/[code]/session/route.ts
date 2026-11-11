import { forwardGate } from "@/lib/viewer/gate-server";
import { apiBaseUrl } from "../../../miniapp/api-base-url";

/**
 * `POST /api/links/{code}/session`: opens the link from a trusted device without a code (L-2) and sets the session
 * cookie. Same-origin only.
 *
 * @param request - The incoming request with the device cookie.
 * @param context - The route context with the link code.
 * @returns `{ ok: true }` with the cookie, or 401 when the gate is needed.
 */
export async function POST(request: Request, context: RouteContext<"/api/links/[code]/session">): Promise<Response> {
  return forwardGate(request, apiBaseUrl(), (await context.params).code, "session");
}
