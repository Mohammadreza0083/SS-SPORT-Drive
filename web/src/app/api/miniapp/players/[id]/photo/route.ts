import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "../../../api-base-url";

/** A player id: a GUID, so nothing else can be spliced into the API path. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Forwards to the API's photo of one player, after checking the id.
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @param context - The route context with the player id.
 * @returns The API's answer, or 404 for an id that is not a GUID.
 */
async function forward(request: Request, context: RouteContext<"/api/miniapp/players/[id]/photo">): Promise<Response> {
  const { id } = await context.params;
  if (!GUID.test(id)) {
    return new Response(null, { status: 404 });
  }

  return forwardToApi(request, apiBaseUrl(), `/miniapp/players/${id}/photo`);
}

/**
 * `GET /api/miniapp/players/{id}/photo`: reads the player's photo (WebP).
 *
 * @param request - The incoming request.
 * @param context - The route context.
 * @returns The photo.
 */
export async function GET(request: Request, context: RouteContext<"/api/miniapp/players/[id]/photo">): Promise<Response> {
  return forward(request, context);
}

/**
 * `PUT /api/miniapp/players/{id}/photo`: uploads the player's photo (the raw image is the body).
 *
 * @param request - The incoming request.
 * @param context - The route context.
 * @returns The API's answer.
 */
export async function PUT(request: Request, context: RouteContext<"/api/miniapp/players/[id]/photo">): Promise<Response> {
  return forward(request, context);
}
