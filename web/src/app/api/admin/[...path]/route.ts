import { forwardAdmin } from "@/lib/admin/api";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * Forwards one console action to the API's `/admin/…`: same origin only, JSON writes only, with the session cookie turned
 * into the `X-Admin-Session` header (see `forwardAdmin`).
 *
 * @param request - The browser's request.
 * @param context - The route context.
 * @param context.params - The path segments after `/api/admin/`.
 * @returns The API's answer.
 */
async function handle(request: Request, context: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return forwardAdmin(request, apiBaseUrl(), (await context.params).path);
}

export { handle as GET, handle as POST, handle as PUT, handle as PATCH, handle as DELETE };
