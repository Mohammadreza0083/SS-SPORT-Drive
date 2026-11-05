import { forwardGate } from "@/lib/viewer/gate-server";
import { apiBaseUrl } from "../../../miniapp/api-base-url";

/**
 * `POST /api/links/{code}/verify`: checks a gate code (L-2); the right code sets the device and session cookies.
 * Same-origin JSON only.
 *
 * @param request - The incoming request with `{ email, code }`.
 * @param context - The route context with the link code.
 * @returns `{ ok: true }` with the cookies, or the API's refusal.
 */
export async function POST(request: Request, context: RouteContext<"/api/links/[code]/verify">): Promise<Response> {
  return forwardGate(request, apiBaseUrl(), (await context.params).code, "verify");
}
