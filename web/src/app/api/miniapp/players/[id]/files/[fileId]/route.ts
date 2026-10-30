import { forwardPlayerRequest, isGuid } from "../../../../player-route";

/**
 * `PATCH /api/miniapp/players/{id}/files/{fileId}`: saves the edit-video sheet (T-7b). Both ids must be GUIDs, so
 * nothing else can be spliced into the API path.
 *
 * @param request - The incoming request with the changed fields.
 * @param context - The route context with the player and file ids.
 * @returns The API's answer, or 404 for an id that is not a GUID.
 */
export async function PATCH(request: Request, context: RouteContext<"/api/miniapp/players/[id]/files/[fileId]">): Promise<Response> {
  const { id, fileId } = await context.params;
  if (!isGuid(fileId)) {
    return new Response(null, { status: 404 });
  }

  return forwardPlayerRequest(request, id, `/files/${fileId}`);
}
