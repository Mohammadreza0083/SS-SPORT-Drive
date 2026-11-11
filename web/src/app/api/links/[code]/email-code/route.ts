import { forwardGate } from "@/lib/viewer/gate-server";
import { apiBaseUrl } from "../../../miniapp/api-base-url";

/**
 * `POST /api/links/{code}/email-code`: asks the API to email a gate code (L-2). Same-origin JSON only.
 *
 * @param request - The incoming request with `{ email }`.
 * @param context - The route context with the link code.
 * @returns The API's answer.
 */
export async function POST(request: Request, context: RouteContext<"/api/links/[code]/email-code">): Promise<Response> {
  return forwardGate(request, apiBaseUrl(), (await context.params).code, "email-code");
}
