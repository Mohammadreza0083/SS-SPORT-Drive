import { forwardProfileRequest } from "@/lib/miniapp";

/**
 * `GET /api/miniapp/me`: forwards the Mini App's signed initData to the API's `GET /miniapp/me`.
 *
 * @param request - The incoming request with `Authorization: tma <initData>`.
 * @returns The API's answer.
 */
export async function GET(request: Request): Promise<Response> {
  return forwardProfileRequest(request, process.env.API_BASE_URL ?? "http://localhost:5080");
}
