import { forwardPublic } from "@/lib/viewer/public-server";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * `POST /api/public/access-code`: asks the API to email a code before a visitor requests access (P-3, P-8).
 * Same-origin JSON only.
 *
 * @param request - The incoming request with `{ email }`.
 * @returns The API's answer.
 */
export async function POST(request: Request): Promise<Response> {
  return forwardPublic(request, apiBaseUrl(), "access-code");
}
