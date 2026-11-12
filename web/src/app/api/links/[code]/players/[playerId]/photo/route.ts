import { forwardPhoto } from "@/lib/viewer/content-server";
import { apiBaseUrl } from "../../../../../miniapp/api-base-url";

/**
 * `GET /api/links/{code}/players/{playerId}/photo`: a player's photo for a signed-in viewer (V-2b). The session cookie
 * goes to the API as a header; the image comes back with a private cache. A read only, so no CSRF check is needed.
 *
 * @param request - The incoming request with the session cookie.
 * @param context - The route context with the link code and the player id.
 * @returns The image, or a bare 401/404.
 */
export async function GET(request: Request, context: RouteContext<"/api/links/[code]/players/[playerId]/photo">): Promise<Response> {
  const { code, playerId } = await context.params;
  return forwardPhoto(request, apiBaseUrl(), code, playerId);
}
