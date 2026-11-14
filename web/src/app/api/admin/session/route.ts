import { startAdminSession } from "@/lib/admin/session";
import { apiBaseUrl } from "../../miniapp/api-base-url";

/**
 * `POST /api/admin/session`: trades the one-time sign-in token for a session cookie and opens the console (W-1).
 *
 * @param request - The Continue button's form post.
 * @returns A redirect to the console, or back to the sign-in page.
 */
export async function POST(request: Request): Promise<Response> {
  return startAdminSession(request, apiBaseUrl());
}
