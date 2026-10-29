import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "../api-base-url";

/**
 * `GET /api/miniapp/volleybox?url=…`: forwards the live Volleybox link check to the API.
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @returns The API's answer.
 */
export async function GET(request: Request): Promise<Response> {
  return forwardToApi(request, apiBaseUrl(), "/miniapp/volleybox");
}
