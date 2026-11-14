import { isAudience, requestSignInLink } from "@/lib/auth/sign-in-server";
import { apiBaseUrl } from "../../../miniapp/api-base-url";

/**
 * `POST /api/auth/{viewer|admin}/link`: asks the API to email a sign-in link (W-1, P-5, A-1). Same-origin JSON only.
 *
 * @param request - The incoming request with `{ email }`.
 * @param context - The route context with the audience.
 * @returns The API's answer, or 404 for an unknown audience.
 */
export async function POST(request: Request, context: RouteContext<"/api/auth/[audience]/link">): Promise<Response> {
  const { audience } = await context.params;
  return isAudience(audience) ? requestSignInLink(request, apiBaseUrl(), audience) : new Response(null, { status: 404 });
}
