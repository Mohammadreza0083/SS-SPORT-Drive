import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "../api-base-url";

/**
 * `GET /api/miniapp/players`: forwards My players (page, filter and search in the query) to the API (T-7).
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @returns The API's answer.
 */
export async function GET(request: Request): Promise<Response> {
  return forwardToApi(request, apiBaseUrl(), "/miniapp/players");
}

/**
 * `POST /api/miniapp/players`: forwards the new-player form to the API.
 *
 * @param request - The incoming request with `Authorization: tma <initData>` and the JSON form.
 * @returns The API's answer.
 */
export async function POST(request: Request): Promise<Response> {
  return forwardToApi(request, apiBaseUrl(), "/miniapp/players");
}
