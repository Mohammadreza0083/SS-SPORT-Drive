import { forwardToApi } from "@/lib/miniapp";
import { apiBaseUrl } from "../api-base-url";

/**
 * `GET /api/miniapp/countries`: forwards to the API's country catalogue for the nationality picker.
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @returns The API's answer.
 */
export async function GET(request: Request): Promise<Response> {
  return forwardToApi(request, apiBaseUrl(), "/miniapp/countries");
}
