import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "./api-base-url";

/** A player id: a GUID, so nothing else can be spliced into the API path. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Forwards a request about one player to the API, after checking the id.
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @param id - The player id from the route.
 * @param suffix - The rest of the API path after the id, e.g. `/folders` (empty for the player itself).
 * @returns The API's answer, or 404 for an id that is not a GUID.
 */
export async function forwardPlayerRequest(request: Request, id: string, suffix: string): Promise<Response> {
  if (!GUID.test(id)) {
    return new Response(null, { status: 404 });
  }

  return forwardToApi(request, apiBaseUrl(), `/miniapp/players/${id}${suffix}`);
}
