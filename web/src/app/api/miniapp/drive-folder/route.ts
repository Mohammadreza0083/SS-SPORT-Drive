import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "../api-base-url";

/**
 * `GET /api/miniapp/drive-folder?url=…`: forwards the live Drive folder check to the API.
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @returns The API's answer.
 */
export async function GET(request: Request): Promise<Response> {
  return forwardToApi(request, apiBaseUrl(), "/miniapp/drive-folder");
}
