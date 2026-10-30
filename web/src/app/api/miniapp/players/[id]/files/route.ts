import { forwardPlayerRequest } from "../../../player-route";

/**
 * `GET /api/miniapp/players/{id}/files`: reads one page of the player's files in page order (T-7).
 *
 * @param request - The incoming request, with the page in its query.
 * @param context - The route context with the player id.
 * @returns The API's answer.
 */
export async function GET(request: Request, context: RouteContext<"/api/miniapp/players/[id]/files">): Promise<Response> {
  return forwardPlayerRequest(request, (await context.params).id, "/files");
}
