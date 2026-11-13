import { forwardPublic } from "@/lib/viewer/public-server";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * `POST /api/public/access-requests`: sends a request for access (P-3, P-7, P-8). The signed-in viewer's cookie goes
 * to the API as a header. Same-origin JSON only.
 *
 * @param request - The incoming request with the player, the description and, for a visitor, the email, code and token.
 * @returns The API's answer.
 */
export async function POST(request: Request): Promise<Response> {
  return forwardPublic(request, apiBaseUrl(), "access-requests");
}
