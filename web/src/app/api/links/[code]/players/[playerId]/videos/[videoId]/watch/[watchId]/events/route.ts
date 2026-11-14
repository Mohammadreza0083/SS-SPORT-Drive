import { forwardPlaybackEvent } from "@/lib/viewer/tracking-server";
import { apiBaseUrl } from "../../../../../../../../../miniapp/api-base-url";

/**
 * `POST /api/links/{code}/players/{playerId}/videos/{videoId}/watch/{watchId}/events`: a player event, `Play`, `Pause`,
 * `Seek` or `Ended` (W-2). Same-origin JSON only.
 *
 * @param request - The incoming request with the session cookie.
 * @param context - The route context with the link code, the player, video and watch ids.
 * @returns 204, or a bare error status.
 */
export async function POST(
  request: Request,
  context: RouteContext<"/api/links/[code]/players/[playerId]/videos/[videoId]/watch/[watchId]/events">,
): Promise<Response> {
  const { code, playerId, videoId, watchId } = await context.params;
  return forwardPlaybackEvent(request, apiBaseUrl(), code, playerId, videoId, watchId);
}
