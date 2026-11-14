import { isAudience, startSession } from "@/lib/auth/sign-in-server";
import { apiBaseUrl } from "../../../miniapp/api-base-url";

/**
 * `POST /api/auth/{viewer|admin}/session`: the Continue button of the sign-in page. Trades the one-time token for a
 * session, sets the `HttpOnly` cookie and redirects (W-1). Same-origin only.
 *
 * @param request - The incoming form post with the token.
 * @param context - The route context with the audience.
 * @returns A redirect, or 404 for an unknown audience.
 */
export async function POST(request: Request, context: RouteContext<"/api/auth/[audience]/session">): Promise<Response> {
  const { audience } = await context.params;
  return isAudience(audience) ? startSession(request, apiBaseUrl(), audience) : new Response(null, { status: 404 });
}
