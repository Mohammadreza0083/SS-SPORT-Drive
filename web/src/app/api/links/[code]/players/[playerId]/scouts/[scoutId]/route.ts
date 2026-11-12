import { forwardScoutReport } from "@/lib/viewer/media-server";
import { apiBaseUrl } from "../../../../../../miniapp/api-base-url";

/**
 * `GET /api/links/{code}/players/{playerId}/scouts/{scoutId}`: a DataVolley file as a report for the signed-in viewer
 * (V-8). The API parses the file; only the match and this player's stats reach the browser. The session cookie goes to
 * the API as a header; the answer is never cached. A read only, so no CSRF check is needed.
 *
 * @param request - The incoming request with the session cookie.
 * @param context - The route context with the link code, the player id and the scout file id.
 * @returns The report, or a bare 401/404/422.
 */
export async function GET(request: Request, context: RouteContext<"/api/links/[code]/players/[playerId]/scouts/[scoutId]">): Promise<Response> {
  const { code, playerId, scoutId } = await context.params;
  return forwardScoutReport(request, apiBaseUrl(), code, playerId, scoutId);
}
