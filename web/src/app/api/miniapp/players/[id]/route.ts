import { forwardPlayerRequest } from "../../player-route";

/**
 * `GET /api/miniapp/players/{id}`: reads the player card for update mode (T-5).
 *
 * @param request - The incoming request.
 * @param context - The route context with the player id.
 * @returns The API's answer.
 */
export async function GET(request: Request, context: RouteContext<"/api/miniapp/players/[id]">): Promise<Response> {
  return forwardPlayerRequest(request, (await context.params).id, "");
}

/**
 * `PATCH /api/miniapp/players/{id}`: saves changed details (T-5).
 *
 * @param request - The incoming request with the changed fields.
 * @param context - The route context with the player id.
 * @returns The API's answer.
 */
export async function PATCH(request: Request, context: RouteContext<"/api/miniapp/players/[id]">): Promise<Response> {
  return forwardPlayerRequest(request, (await context.params).id, "");
}
