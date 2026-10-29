import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "../api-base-url";

/**
 * `GET /api/miniapp/me`: forwards the Mini App's signed initData to the API's `GET /miniapp/me`.
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @returns The API's answer.
 */
export async function GET(request: Request): Promise<Response> {
  return forwardToApi(request, apiBaseUrl(), "/miniapp/me");
}
