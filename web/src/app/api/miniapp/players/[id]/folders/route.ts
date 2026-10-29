import { forwardPlayerRequest } from "../../../player-route";

/**
 * `POST /api/miniapp/players/{id}/folders`: adds a Drive folder to the player (T-5).
 *
 * @param request - The incoming request with the folder link.
 * @param context - The route context with the player id.
 * @returns The API's answer.
 */
export async function POST(request: Request, context: RouteContext<"/api/miniapp/players/[id]/folders">): Promise<Response> {
  return forwardPlayerRequest(request, (await context.params).id, "/folders");
}
