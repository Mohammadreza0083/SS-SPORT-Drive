import { forwardStream } from "@/lib/viewer/media-server";
import { apiBaseUrl } from "../../../../../../../miniapp/api-base-url";

/**
 * `GET /api/links/{code}/players/{playerId}/videos/{videoId}/stream`: signs a video's stream for the signed-in viewer
 * (V-5b). The session cookie goes to the API as a header; the answer is the edge playlist URL, never cached. A read
 * only, so no CSRF check is needed.
 *
 * @param request - The incoming request with the session cookie.
 * @param context - The route context with the link code, the player id and the video id.
 * @returns The signed stream answer, or a bare 401/404.
 */
export async function GET(request: Request, context: RouteContext<"/api/links/[code]/players/[playerId]/videos/[videoId]/stream">): Promise<Response> {
  const { code, playerId, videoId } = await context.params;
  return forwardStream(request, apiBaseUrl(), code, playerId, videoId);
}
