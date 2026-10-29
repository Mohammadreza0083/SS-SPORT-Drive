import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "../api-base-url";

/**
 * `POST /api/miniapp/players`: forwards the new-player form to the API.
 *
 * @param request - The incoming request with `Authorization: tma <initData>` and the JSON form.
 * @returns The API's answer.
 */
export async function POST(request: Request): Promise<Response> {
  return forwardToApi(request, apiBaseUrl(), "/miniapp/players");
}
