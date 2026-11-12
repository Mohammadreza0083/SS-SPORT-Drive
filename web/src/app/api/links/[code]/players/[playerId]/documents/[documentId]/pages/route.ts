import { forwardPages } from "@/lib/viewer/media-server";
import { apiBaseUrl } from "../../../../../../../miniapp/api-base-url";

/**
 * `GET /api/links/{code}/players/{playerId}/documents/{documentId}/pages`: signs a document's page images for the
 * signed-in viewer (V-5b). The session cookie goes to the API as a header; the answer lists the edge page URLs, never
 * cached. A read only, so no CSRF check is needed.
 *
 * @param request - The incoming request with the session cookie.
 * @param context - The route context with the link code, the player id and the document id.
 * @returns The signed pages answer, or a bare 401/404.
 */
export async function GET(request: Request, context: RouteContext<"/api/links/[code]/players/[playerId]/documents/[documentId]/pages">): Promise<Response> {
  const { code, playerId, documentId } = await context.params;
  return forwardPages(request, apiBaseUrl(), code, playerId, documentId);
}
