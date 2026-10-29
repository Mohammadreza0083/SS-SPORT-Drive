import { forwardPlayerRequest } from "../../../player-route";

/**
 * `GET /api/miniapp/players/{id}/photo`: reads the player's photo (WebP).
 *
 * @param request - The incoming request.
 * @param context - The route context with the player id.
 * @returns The photo.
 */
export async function GET(request: Request, context: RouteContext<"/api/miniapp/players/[id]/photo">): Promise<Response> {
  return forwardPlayerRequest(request, (await context.params).id, "/photo");
}

/**
 * `PUT /api/miniapp/players/{id}/photo`: uploads the player's photo (the raw image is the body).
 *
 * @param request - The incoming request.
 * @param context - The route context with the player id.
 * @returns The API's answer.
 */
export async function PUT(request: Request, context: RouteContext<"/api/miniapp/players/[id]/photo">): Promise<Response> {
  return forwardPlayerRequest(request, (await context.params).id, "/photo");
}
