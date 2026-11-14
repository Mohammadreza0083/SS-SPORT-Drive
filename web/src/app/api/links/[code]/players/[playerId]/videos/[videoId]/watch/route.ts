import { forwardWatchStart } from "@/lib/viewer/tracking-server";
import { apiBaseUrl } from "../../../../../../../miniapp/api-base-url";

/**
 * `POST /api/links/{code}/players/{playerId}/videos/{videoId}/watch`: starts a watch of a video for the signed-in viewer
 * (W-2). Same-origin JSON only; the session cookie goes to the API as a header.
 *
 * @param request - The incoming request with the session cookie.
 * @param context - The route context with the link code, the player id and the video id.
 * @returns The watch id and heartbeat interval, or a bare error status.
 */
export async function POST(request: Request, context: RouteContext<"/api/links/[code]/players/[playerId]/videos/[videoId]/watch">): Promise<Response> {
  const { code, playerId, videoId } = await context.params;
  return forwardWatchStart(request, apiBaseUrl(), code, playerId, videoId);
}
