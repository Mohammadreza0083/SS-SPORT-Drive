import { forwardPublicPhoto } from "@/lib/viewer/public-server";
import { apiBaseUrl } from "../../../../miniapp/api-base-url";

/**
 * `GET /api/public/players/{slug}/photo`: a published player's photo for the home cards and the locked page. A read
 * only, so no CSRF check is needed.
 *
 * @param request - The incoming request.
 * @param context - The route context with the slug.
 * @returns The image.
 */
export async function GET(request: Request, context: RouteContext<"/api/public/players/[slug]/photo">): Promise<Response> {
  return forwardPublicPhoto(request, apiBaseUrl(), (await context.params).slug);
}
